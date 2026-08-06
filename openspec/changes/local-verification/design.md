## Context

The remediation run is currently REST-shaped end to end. `RemediationRunner.RunAsync` walks `Reading → Analyzing → Applying → CreatingPR`, where `Applying` both computes edited manifest text and pushes it as a commit via `IAzureDevOpsClient.PushFilesAsync`. There is no working tree, no `git` binary, and no SDK in the remediation worker image (`dotnet/runtime:10.0`). `update-execution` codifies this: "Validation delegated to the repository CI."

Two forces break that premise:

1. **`packages.lock.json`.** Nothing in the codebase reads or writes lock files (`GetManifestsAsync` filters to `Directory.Packages.props` and `*.csproj` only). A repository committing a lock file with `RestoreLockedMode` therefore receives a PR that fails restore with `NU1004` before its CI even reaches the build. A lock file is only producible by a real `dotnet restore`.
2. **The AI remediation loop (Slice 5)** repairs compile/API breaks by editing source. That needs both a working tree and compiler diagnostics — neither of which the current shape can produce.

The roadmap framed the collateral/breakage signal as a spectrum from metadata-only through restore-only and full build to CI-feedback, and left the choice open. This design resolves it: the run verifies locally, before the push, and the target repository's own Azure DevOps CI remains the authority on the resulting pull request.

Constraint that makes this tractable: all target repositories are .NET 8/10, so a single SDK image can build any of them. The build-environment heterogeneity that made in-job builds look expensive (net472, workloads, mixed SDK pins) does not apply here.

## Goals / Non-Goals

**Goals:**
- Verify the computed bump set by restoring and building the repository in-job, before anything is pushed.
- Gate build behind restore, so cheap failures are reported cheaply.
- Emit a correct `packages.lock.json` for repositories that commit one.
- Never regress: an environment problem that prevents verification must still produce the pull request today's pipeline would have produced.
- Leave behind the two things Slice 5 needs — a materialized working tree and structured compiler diagnostics — without building any part of the AI loop.
- Surface the verification outcome and logs in the existing run history UI.

**Non-Goals:**
- Running the target repository's tests. Its CI keeps that job, along with analyzers and custom targets.
- Any AI/agent step, source editing, or iteration loop (Slice 5).
- Polling or reacting to the pull request's CI result. The PR is still validated by CI; the run simply does not wait for it.
- Replacing feed-metadata family alignment (`dependency-graph-alignment`). Verification consumes its output.
- Supporting non-.NET-8/10 target repositories or `packages.config`/`net472` projects.
- Caching the NuGet global packages folder or the downloaded tree across runs (a later optimization).

## Decisions

### Local in-job build, not CI-feedback polling

Alternatives considered:

| | Local verify | CI-feedback |
|---|---|---|
| Run shape | one synchronous process | suspend/resume across minutes–hours |
| PR history | one commit, already green | broken commit + N "AI fix attempt" commits |
| Diagnostic quality | structured MSBuild errors + the source at hand | a scraped log, no source in-job |
| Cost per iteration | seconds, our compute | minutes, the target team's build agents |
| Local development | works offline against a real repo | requires a live pipeline on a real repo |

CI-feedback also demands durable run suspension — an `AwaitingCi` status, webhook or polling infrastructure, per-iteration timeouts, and re-entrant rehydration of `RemediationRun`. That machinery is incidental to the actual goal and is arguably a larger change than the AI loop it would serve. Local verification keeps `RemediationRunner` a straight-line state machine.

Chosen: verify locally. The two builds serve different roles and both are kept — the local build is the *iteration signal* (fast, approximate, pre-push), and the target repository's CI is the *authority* (definitive, post-push, on the PR). Delegating validation to CI is not being abandoned; an inner loop is being added in front of it.

### Working tree via the Azure DevOps zip endpoint, not `git clone`

`GET /_apis/git/repositories/{repositoryId}/items?scopePath=/&versionDescriptor.version={commit}&versionDescriptor.versionType=commit&$format=zip&download=true` returns the whole tree at a commit. This reuses the existing PAT-authenticated `HttpClient` — no `git` binary in the image, no second credential path, and no partial-clone tuning.

Selective file fetching was rejected: restore needs `NuGet.config`, `global.json`, `Directory.Build.props`/`.targets`, `*.sln`, and whatever those transitively import. MSBuild imports are unbounded, so "fetch just the project files" cannot be made correct. Downloading the tree once is both simpler and forward-compatible with Slice 5, which needs arbitrary source anyway.

The tree is extracted to a per-run temporary directory and deleted when the run ends. The commit used is the same `baseCommit` the push is based on, so the verified tree and the pushed commit's parent agree.

Trade-offs accepted: no incremental fetch (whole tree per run), and repositories using Git LFS or submodules are not supported.

### Restore gates build

`dotnet restore` runs first. Only on success does `dotnet build --no-restore` run. Rationale: restore is much cheaper, and its failure modes (`NU1107` version conflict, `NU1605` downgrade, unresolvable package) are exactly the class of problem that feed-metadata alignment cannot see — it reasons only over the declared `Orion180.*` family, so third-party and transitive conflicts are invisible to it. Reporting a restore failure without paying for a doomed build is strictly better, and a build attempted on a failed restore produces misleading noise.

### Feed credentials via a generated `NuGet.config`

`FeedVersionResolver` already authenticates to private feeds with the Azure DevOps PAT from `AzureDevOpsOptions`. Restore reuses that PAT: the run writes a `NuGet.config` into the extracted tree root containing the configured feeds with `<packageSourceCredentials>`, using `<clear/>` so the target repository's own sources cannot shadow it. No new secret and no Slice-1 secrets rework.

Alternative rejected: the Azure Artifacts Credential Provider. It adds an image dependency and an environment-variable protocol to achieve exactly what an explicit config file already does.

The generated `NuGet.config` is a verification artifact only — it is never included in the pushed commit.

### Three outcomes, not two

Verification classifies into three, because "failed" conflates two very different situations:

```
  restore/build result
    ├─ success ─────────────────▶ Verified            → push → PR
    ├─ dependency diagnostics ──▶ DependencyFailure   → NO push, NO PR, terminal
    └─ environment failure ─────▶ Skipped             → push → PR + "not verified" note
```

- **Verified** — restore and build both succeeded. Push, including any regenerated lock file.
- **DependencyFailure** — restore or the compiler rejected the change (`NU1xxx`, `MSB` package resolution errors, `CS` compile errors). The bump set is wrong; pushing it would create a knowingly-broken PR. The run ends in a new terminal status with the diagnostics recorded and no PR. This is the state Slice 5 will intercept to attempt a repair.
- **Skipped** — verification could not run or could not be trusted: tree download failed, the SDK could not satisfy `global.json`, the feed was unreachable, the process timed out. This is *not* evidence the bump is bad, so the run degrades to exactly today's behavior — push, open the PR, and note in the description that verification was skipped and why.

The Skipped path is what makes this change safe to ship: its floor is the current pipeline. Misclassifying an environment problem as a dependency failure would silently stop producing PRs, so the classifier defaults to `Skipped` when a failure cannot be positively identified as dependency-related.

### Pipeline restructure

`Applying` currently means "edit and push", so verification cannot be inserted without splitting it:

```
  Reading  →  Analyzing  →  Applying   →  CreatingPr  →  Completed
                            (edit+push)

  Reading  →  Analyzing  →  Applying    →  Verifying  →  Pushing  →  CreatingPr  →  Completed
                            (edits into                  (commit incl.              | VerificationFailed
                             the tree,                    lock file)                | NoUpdates | Failed
                             no push)
```

`RunStatus` gains `Verifying`, `Pushing`, and terminal `VerificationFailed`. `RemediationRun` carries a verification outcome (classification, a bounded set of structured diagnostics, and the blob path of the full log). The existing `NoUpdates` short-circuit stays ahead of tree download, so a run with nothing to do never pays for a download or a restore.

`Failed` retains its meaning: an unexpected error in the runner itself. A verification that legitimately rejected the change is `VerificationFailed`, which is a *result*, not a crash — the distinction matters for the runs feed, where operators need to tell "the tool broke" from "the bump doesn't compile".

### Diagnostics: structured, bounded, with the full log in blob storage

Restore and build run with `--nologo` and MSBuild's console logger configured for parseable diagnostics (`-p:GenerateFullPaths=true`), and stdout/stderr are captured. Lines matching the MSBuild diagnostic form (`path(line,col): error CODE: message`) are parsed into structured diagnostics; NuGet's `NU`-prefixed errors are captured with their code and message. Paths are normalized to be repository-relative so they survive the temp directory and mean something in the UI and later to the agent.

Only a bounded number of diagnostics (first N distinct) is persisted on the run record, since Table Storage entity size is limited and a cascade of compile errors is highly repetitive. The complete log always goes to blob storage via `IBlobStore` under a per-run path, and the run record holds the reference. This keeps the run record small while losing nothing.

Process execution is wrapped with a timeout and cancellation-token propagation; a timeout classifies as `Skipped`, not a dependency failure.

### Lock file handling

If restore produced or modified any `packages.lock.json` in the tree, those files are added to the pushed change set alongside the edited manifests. Detection is by comparing content against what was extracted, so repositories that do not use lock files are entirely unaffected — no empty lock files are ever introduced.

This is the one place where verification changes the *output* rather than just gating it, and it is the fix for the latent `NU1004` defect described in the proposal.

### SDK base image for the remediation worker

`src/AutoRemediator.Worker.Remediation/Dockerfile`'s final stage moves from `mcr.microsoft.com/dotnet/runtime:10.0` to `mcr.microsoft.com/dotnet/sdk:10.0`. This contradicts the current `container-images` requirement that workers use the runtime base, so that spec is amended for `remediation` specifically; `scheduler` stays on the runtime base.

Locally this costs nothing: `AppHost.cs` registers the worker with `AddProject`, so it runs on the host where the SDK already exists. Only the deployed image grows.

## Risks / Trade-offs

- **Image size grows from ~200 MB to ~1 GB** → Accepted. The remediation worker is a queue-scaled ACA Job, not a latency-sensitive service, and cold-start cost is amortized over a run that already takes a restore and a build. Only the `remediation` image changes.

- **Run duration increases substantially** (restore + build per run, no warm package cache) → Accepted for this slice. If it becomes painful, mounting a persistent NuGet packages folder across runs is the obvious next step and needs no spec change.

- **Environment failures misclassified as dependency failures would silently stop producing PRs** → The classifier only returns `DependencyFailure` on positively-identified dependency/compile diagnostics and defaults to `Skipped` otherwise. Both classifications are visible in the runs feed and both persist their full log, so a misclassification is diagnosable rather than invisible.

- **Local green does not guarantee CI green** (their CI runs tests, analyzers, custom targets we skip) → By design. Local verification is an approximate fast signal, not a replacement gate; the PR is still validated by the repository's CI. Documented in the modified `update-execution` requirement so the boundary is not later mistaken for a promise.

- **Restore reaches the network for every run and can be flaky** → Transient feed failures classify as `Skipped`, so flakiness degrades to today's unverified-PR behavior rather than blocking updates or producing false rejections.

- **The generated `NuGet.config` uses `<clear/>` and could drop a source the repository legitimately needs** → If restore then fails to resolve a third-party package, it surfaces as a dependency failure that is really a configuration problem. Mitigation: merge the repository's existing configured sources into the generated config, keeping `<clear/>` only to control ordering and credentials, and treat "package not found on any source" as a candidate for `Skipped` rather than `DependencyFailure` when the package is outside the managed patterns.

- **The PAT now grants tree download in addition to manifest reads** → No scope change in practice; `Code (Read)` already covers both, and the same PAT already reads every manifest.

- **Temp disk exhaustion across concurrent runs** → Each run extracts to its own temporary directory and deletes it in a `finally`, including on failure and cancellation.

- **LFS and submodule repositories will not verify correctly** → Out of scope; the zip archive omits both. Such a repository fails restore or build for a reason unrelated to the bump, which the classifier should treat as `Skipped`. Worth revisiting if a target repository turns out to use either.
