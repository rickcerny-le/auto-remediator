# Local repository mode: manual integration test

This walkthrough runs the whole system on one machine against git repositories and a NuGet folder feed on
disk, with no Azure DevOps and (by default here) no model. It was executed once end to end on 2026-10-14; the
**Actual** columns and the [Findings](#findings) are what that run observed. Everything here is dev and test
only: local repository mode (`AzureDevOps:Mode=Local`) uses the `git` binary and is exempt from the product's
no-clone rule.

## Prerequisites

- .NET 10 SDK
- Docker (Azurite and the Service Bus emulator always; Ollama only if the model is enabled)
- the Aspire CLI (`aspire`)
- PowerShell 7 (`pwsh`); Windows PowerShell 5.1 is not supported by the generator
- `git` on PATH (needed by `api`, `remediation`, `held-run-seeder` and the generator)

## 1. Generate the scenarios

```powershell
pwsh -NoProfile -File tools/local-scenarios/New-LocalScenarios.ps1
```

It deletes and recreates `.local/feed` (29 nupkgs for the Contoso/Fabrikam/Northwind/Tailspin/AdventureWorks
families), `.local/repos` (14 demo git repos on `main`, one commit each; `held-review` also has a
`scenario/agent-fix` branch) and `.local/prs` (empty). It builds every repo as a self-check and **purges the
generated package ids from the NuGet global packages cache** (`~/.nuget/packages`, or `NUGET_PACKAGES`), so a stale
cached package cannot give a wrong API or an NU1403 hash mismatch. It took about 85 s on the walkthrough machine.
Each repo's `NuGet.config` uses the absolute path of `.local/feed`. Re-run it any time to reset everything.

## 2. Start the app

```powershell
$env:Model__Enabled='false'; $env:LocalRepos__Enabled='true'; aspire start
aspire describe
```

`aspire start` returns once the AppHost is up; the `api`, `scheduler`, `remediation` and `web` resources stay in
`Waiting` for a while (about a minute) until the Service Bus emulator is healthy. Re-run `aspire describe` until
they show `Running`.

- **API URL**: the `api` resource's `http` url (here `http://localhost:5228`).
- **Web URL**: the `web` resource's `http` url (here `http://localhost:53334`). Use this one, not the `PORT` env var.
- **Azurite connection strings**: `aspire describe --format Json` lists each resource's `environment`; take
  `ConnectionStrings__tables` and `ConnectionStrings__blobs` from the `api` entry. They address Azurite as
  `127.0.0.1`. Do not substitute the `TABLES_URI` / `BLOBS_URI` values, which say `localhost` (that can resolve to
  IPv6 and miss Azurite). Note that `aspire describe --format Json` writes UTF-16 when redirected to a file.
- The dashboard URL (with login token) is printed by `aspire start`.

With the AppHost in local mode, `api`, `scheduler`, `remediation` and the seeder all get `AzureDevOps__Mode=Local`,
`AzureDevOps__LocalRoot=<repo>/.local/repos` and `AzureDevOps__LocalPullRequestsRoot=<repo>/.local/prs`. No PAT is
needed. Azurite data is not persisted between `aspire start` runs, so run history starts empty each time.

## 3. Non-agentic mode (the model is disabled)

With `Model__Enabled=false` there is no model resource and `remediation` has no `ConnectionStrings__chat`. The
agent reports itself unavailable, so there is **no AI repair**:

- A bump that breaks the build ends `VerificationFailed` with **0 remediation attempts** and a transcript that says
  "No model is configured, so no repair was attempted."
- Nothing is pushed: no `autoremediator/dependency-updates` branch, no `.local/prs/<repo>` record, no PR link.
- The UI shows `VerificationFailed` in **amber** (warning tone), not red: the tool worked, the bump did not.
- Restore (`NU...`) failures never enter the repair loop in any mode.

Leaving the model enabled (the default) starts Ollama with `phi4`, which is a large first download, and makes
compile-break repairs possible. The expected results below assume the model is **disabled**.

## 4. Enroll a profile

```powershell
pwsh -NoProfile -File tools/local-scenarios/Set-LocalScenarioConfig.ps1 -ApiUrl http://localhost:5228 -Profile baseline
```

This lists, then creates or updates each repo by name (so it is safe to repeat) and applies the global targeting
settings for the profile. `-Only a,b` enables just those repos and disables the rest. The Configuration page in the
UI shows the same repositories and settings and can edit them (enter feed `C:\...\.local\feed`, patterns
`Contoso.*`, `Fabrikam.*`, `Northwind.*`, `Tailspin.*`, ignore `Tailspin.Legacy.*`).

| Profile | Strategy | Allow pre-release |
| --- | --- | --- |
| baseline | Minor | no |
| patch | Patch | no |
| major | Major | no |
| prerelease | Minor | yes |

`disabled-repo` is enrolled disabled, `missing-branch` targets `develop`, and `does-not-exist` has no folder.

## 5. Trigger runs

The scheduler runs once when it starts, then exits. Restart it to run a pass over every enabled repo:

```powershell
aspire resource scheduler restart
aspire logs remediation        # per-run progress (verbose: Service Bus receiver logging)
```

(or the Restart button on `scheduler` in the dashboard). A pass over all 14 enabled repos took roughly one to two
minutes here, because the restore and build in each temp workspace are fast. Optionally set
`Scheduler__DevLoopEnabled=true` (and `Scheduler__DevLoopIntervalSeconds`) before `aspire start` to re-trigger
automatically.

## 6. What to observe

- **Runs page** (`/runs`): one row per run with status and a PR link for `Completed` runs.
- **Run detail** (`/runs/<id>`): the updates table (kind `matched` / `collateral`, and a beyond-policy flag),
  the verification outcome and diagnostics (capped on the run; the **full log** link has everything), the
  **transcript** link for runs that entered the repair loop (`major-breaking`), and the PR link.
- **Pull request changes** panel (read-only, local mode, `Completed` runs only): the files of the PR with before
  and after content, no action buttons. It should list exactly the files of
  `git -C .local/repos/<repo> diff --name-only main..autoremediator/dependency-updates`.
  The same data is `GET /api/runs/<id>/pull-request-diff`.
- **Dependency Map** (`/dependency-map`): `Outdated`, `UpToDate` or `Ignored` per package against the active
  profile. It is computed live from the repo and current settings, so it changes when you switch profile.
- Git state: `git -C .local/repos/<repo> log --oneline --all` and
  `git -C .local/repos/<repo> diff main..autoremediator/dependency-updates`.
- PR records: `.local/prs/<repo>/*.json` with `sourceBranch`, `targetBranch`, `baseCommit` (merge-base),
  `headCommit` (branch head), `status`, `url`. Re-running refreshes `headCommit` and `updatedAtUtc` but never
  creates a second record.

## 7. Expected results and actual results

Each repo, per profile. "Actual" is what was observed; run ids are the first 8 characters. `Completed` always means
the update branch exists, `.local/prs/<repo>/1.json` exists, a PR link is shown and verification was `Verified`.
Where the Actual column says "as expected" the outcome matched the expected cell exactly (statuses, versions and
flags were read from `/api/runs/<id>` and the git repo, not inferred).

### baseline (Minor)

| Repo | Expected | Actual |
| --- | --- | --- |
| patch-bump | Contoso.Logging 1.2.0 -> 1.2.3, Completed | as expected (67f700cb) |
| minor-bump | Contoso.Http 2.1.0 -> 2.4.1, both csproj edited, Completed | as expected (2295de34); diff = `src/Api/Api.csproj`, `src/Worker/Worker.csproj` |
| major-compiles | NoUpdates (policy cap) | as expected (8da91461) |
| major-breaking | NoUpdates | as expected (7c7390bd) |
| up-to-date | NoUpdates, map UpToDate | as expected (4e5f2a2d) |
| no-matches | NoUpdates, nothing matched | as expected (4afdaae9); not in the map |
| ignored-package | Core -> 1.0.2 only, Legacy Ignored | as expected (ab5e4905); map shows `Tailspin.Legacy.Data` Ignored |
| prerelease | Fabrikam.Telemetry -> 1.0.1 | as expected (78c6975e) |
| family-alignment | SqlServer and Data -> 1.2.0 matched, Abstractions -> 2.0.0 collateral and beyond policy, Completed | as expected (b21e72a5): 3 updates, Abstractions `Collateral` + beyond policy, Verified |
| restore-conflict | Reporting -> 1.1.0, VerificationFailed with NU diagnostic, repair loop not entered | as expected (a22a4b14): `NU1605` x3 (a package downgrade, not NU1102), classification DependencyFailure, remediation null, no branch. See finding 5 |
| lock-file | -> 1.2.3, pushed commit includes the regenerated lock file | as expected (f36df522): commit changes `Directory.Packages.props` and `src/App/packages.lock.json` |
| held-review | seeded AwaitingReview; scheduler records SkippedHeld | see [Review gate](#8-review-gate). Un-seeded, the repo is a normal repo: NoUpdates (8a6909c8) |
| missing-branch | NoUpdates (known gap) | as expected (9977db7c): NoUpdates, no error. The PRD "missing branch" known defect behaves as written |
| does-not-exist | NoUpdates (known gap) | as expected (3fb456b3): NoUpdates, no error. Same known defect |
| disabled-repo | no run created | as expected: no run in any pass |

Re-running the scheduler without resetting produced no second PR record for any repo (see finding 1 for the one
side effect).

### patch

| Repo | Expected | Actual |
| --- | --- | --- |
| patch-bump | same (1.2.3) | as expected (ac9c2809) |
| minor-bump | Contoso.Http -> 2.1.3 | as expected (237c2ab6) |
| major-compiles / major-breaking | NoUpdates | as expected (b65ce8df / b28198c9) |
| up-to-date, no-matches | NoUpdates | as expected (c08f8bd5, 8464bc18) |
| ignored-package | Core -> 1.0.2 | as expected (c14bc7f4) |
| prerelease | -> 1.0.1 | as expected (1c51019c) |
| family-alignment | NoUpdates (to confirm) | confirmed NoUpdates (f4d9f81b) |
| restore-conflict | NoUpdates | as expected (7d79f515) |
| lock-file | -> 1.2.3 with lock file | as expected (8dab4f62) |
| held-review, missing-branch, does-not-exist | NoUpdates | as expected (f7791e93, 45e55140, 1e9fb8bd) |

### major

| Repo | Expected | Actual |
| --- | --- | --- |
| patch-bump | same | as expected (b99600d2) |
| minor-bump | Contoso.Http -> 3.0.0 | as expected (5220b4ee) |
| major-compiles | Northwind.Serialization -> 2.0.0, Completed | as expected (264d6a8e) |
| major-breaking | VerificationFailed, CS1061, 0 attempts, "no model" transcript, no branch | as expected (9bc6385b-c7b5-4bae-9fb4-69868b8738b5); see [the non-agentic check](#non-agentic-check-actual) |
| up-to-date, no-matches | NoUpdates | as expected (2973c0ff, 6049e34a) |
| ignored-package | Core -> 1.0.2 only | as expected (48b7ecf4) |
| prerelease | -> 1.0.1 | as expected (d49001c4) |
| family-alignment | all -> highest, Abstractions matched | as expected (271fd7cc): Abstractions 2.0.0 `Matched`, Data and SqlServer 1.2.0 |
| restore-conflict | as baseline plus Abstractions -> 2.0.0 | as expected (5d43038d): 2 updates, still `NU1605` x3 |
| lock-file | -> 1.2.3 with lock file | as expected (a8b07ef0) |
| held-review | (normal repo here) | VerificationFailed, CS1061, 0 attempts (1b1b26a1), same as `major-breaking`: it has the same code |
| missing-branch, does-not-exist | NoUpdates | as expected (9906e56e, 8718ca92) |

### prerelease (Minor + pre-release)

| Repo | Expected | Actual |
| --- | --- | --- |
| prerelease | Fabrikam.Telemetry -> 1.1.0-beta.2 | as expected (ab76b613) |
| patch-bump, ignored-package, lock-file | as baseline | as expected (aae86ee6, a2356652, ac504457) |
| minor-bump | -> 2.4.1 | as expected (2c13cc47) |
| major-compiles, major-breaking, up-to-date, no-matches | NoUpdates | as expected (664a06e0, 1c3a05c6, 002c02b3, a685c55a) |
| family-alignment | as baseline | as expected (a8d687e7): Abstractions collateral, beyond policy |
| restore-conflict | as baseline | as expected (a3bc419c): NU1605 |
| held-review, missing-branch, does-not-exist | NoUpdates | as expected (b8858864, 9e5d9121, afc6a3b2) |

### Non-agentic check (actual)

Under the `major` profile with `Model__Enabled=false`, `major-breaking` run
`9bc6385b-c7b5-4bae-9fb4-69868b8738b5`:

- status `VerificationFailed`; update `Northwind.Messaging 3.2.0 -> 4.0.0`
- verification `DependencyFailure` (shown as "rejected"), one diagnostic `CS1061` at `src/App/Program.cs:1:38`
  (`'MessageBus' does not contain a definition for 'Publish'`)
- remediation: 0 attempts; the transcript contains "No model is configured, so no repair was attempted."
- the status pill renders with the `warn` (amber) tone (`ar-pill warn`, confirmed in the rendered DOM and a screenshot)
- `git -C .local/repos/major-breaking branch -a` shows only `main`, there is no `.local/prs/major-breaking` folder
  and the run has no PR link

### PR diff panel check (actual)

For every `Completed` run in every pass, the file list from `/api/runs/<id>/pull-request-diff` equalled
`git diff --name-only main..autoremediator/dependency-updates` (for example `minor-bump`: `src/Api/Api.csproj`,
`src/Worker/Worker.csproj`; `lock-file`: `Directory.Packages.props`, `src/App/packages.lock.json`;
`held-review` after Approve: `Directory.Packages.props`, `src/App/Program.cs`). The page-level Playwright tests also
passed against these runs (see [Automated checks](#9-automated-checks-against-the-running-app)).

## Profiles and resetting between them

Settings are global, so switching profile changes behaviour for every repo. A re-run reuses the existing
`autoremediator/dependency-updates` branch and PR record, which is where the PRD "base mismatch in planning" known
defect can show up. Reset between profiles by either:

```powershell
pwsh -NoProfile -File tools/local-scenarios/New-LocalScenarios.ps1        # full reset, ~85 s, purges the NuGet cache
# or, quicker: delete the update branches and PR records
git -C .local/repos/<repo> branch -D autoremediator/dependency-updates     # for each repo that has one
Remove-Item -Recurse .local/prs/*
```

The walkthrough used the quick reset between profiles (after the first, freshly generated, pass). When profile
`baseline` was re-applied on top of the `patch` results **without** resetting, `minor-bump` still pushed the correct
2.4.1 content (the planner reads `main`, and the push overwrites the files), so the base-mismatch defect did not
show up in this scenario; it only added another commit to the branch.

## 8. Review gate

`AwaitingReview` normally needs a model to repair a compile break, so it is seeded. The seeder is an
explicit-start Aspire resource that exists only in local mode and gets the Azurite connection strings injected.

```powershell
pwsh -NoProfile -File tools/local-scenarios/Set-LocalScenarioConfig.ps1 -ApiUrl http://localhost:5228 -Only held-review
aspire resource held-run-seeder start
aspire logs held-run-seeder    # prints: Seeded held run <id> ... and Web: /runs/<id>
```

(The seeder's console output is dominated by Azure SDK HTTP logging; the two lines that matter are the last ones.)
The held run's base commit is `held-review`'s real `main` head and its proposal is the real difference between
`main` and `scenario/agent-fix` (`Directory.Packages.props` and `src/App/Program.cs`). It appears on Home and Runs
as Awaiting review. Starting the seeder again while a held run exists prints the existing run id and creates
nothing (verified: still one held run). The commands, sent here with the same `POST /api/runs/<id>/review/<command>`
the buttons use (HTTP 202 each):

| Action | Expected | Actual (run 590a8dc0 unless stated) |
| --- | --- | --- |
| Retry | blocked: no model configured | stays `AwaitingReview`, `reviewNote` "no model is configured, so retry could not run" |
| Rebuild | re-verify at the current head | stays `AwaitingReview`, note cleared, proposal `verifiedAtUtc` refreshed, `reviewCommandCount` 2 |
| Approve | push the stored proposal, open the PR | `Completed`, branch commit with both files, `.local/prs/held-review/1.json`, PR link and the "Pull request changes" panel listing both files. The run's `updates` list is empty afterwards (finding 3) |
| Discard | run closed, nothing pushed | run e19138d8: `Discarded`; the update branch from the Approve was left as it was |
| Scheduler pass while held | `SkippedHeld` | restart `scheduler` with a held run: `SkippedHeld` run ddc0e900 for `held-review` (others unaffected) |

After Approve or Discard the run is no longer held, so starting the seeder again seeds a new run (e19138d8, then
0bc07dee). To reset the scenario completely, regenerate.

## 9. Automated checks against the running app

With a Completed local run (with a PR diff) and a held run present, run the functional suite, pointing at the app:

```powershell
$env:WEB_BASE_URL='http://localhost:53334'
$env:ConnectionStrings__tables='<from the api resource environment, 127.0.0.1>'
$env:ConnectionStrings__blobs='<same>'
dotnet test tests/AutoRemediator.Web.FunctionalTests
```

Result on the walkthrough machine: **30 passed, 0 failed, 0 skipped**, including `PullRequestDiffTests`,
`ReviewGateTests` (3), `FocusVisibleTests` (12, both themes and forced colors), the axe WCAG A/AA tests (both themes,
run detail with the PR diff panel included), `HomePageSmokeTests` (2). The suite finds "a run with a PR diff" as
the first run whose diff link is set; it passed here after a clean baseline pass (see troubleshooting for stale runs).

## Troubleshooting

- **Run ends `Skipped` with NU1301**: a feed path problem (source unreachable is classed as environmental, and the
  change would still be pushed "verification skipped"). The generated `NuGet.config` files use absolute paths; a
  relative path in a demo repo's `NuGet.config` breaks inside the temp workspace.
- **NU1403 or the wrong API**: a stale NuGet global packages cache. Re-run the generator (it purges the ids it makes).
- **"git was not found on PATH"**: install git or fix PATH, then restart the AppHost so `api` and `remediation`
  inherit it.
- **`file://` links do nothing in the browser**: Chromium blocks navigation from an http page to `file://`. Copy the
  link, or use the "Pull request changes" panel.
- **The "Pull request changes" panel is missing or says "not available: 404 Not Found"** for old runs: the panel shows
  the PR record's *current* state by repo, so a run whose repo has no PR record any more (after a reset or a
  regeneration) has a diff link that 404s, and its commits no longer exist after regeneration. Only the latest
  pass's runs are meaningful. Run id `264d6a8e` (a `major-compiles` run from an earlier pass) showed this.
- **Uncommitted edits to a demo repo are invisible**: verification and planning use `git archive` of committed
  state, as Azure DevOps would. Commit your edit to `main` (the generator's commit identity is not required).
- **Restarting a resource vs `aspire stop`**: `aspire resource <name> restart` restarts one resource and keeps
  Azurite data; `aspire stop` stops the whole AppHost and the Azurite and Service Bus emulator containers' data
  is not persisted, so run history is gone on the next `aspire start`. Always `aspire stop` before building the
  solution (a running AppHost holds file locks on `bin`/`obj`).
- **Run history surviving regeneration**: if Azurite is kept running while you regenerate, old runs still reference
  commits that no longer exist (see the 404 above). Restart the AppHost for a clean history.
- **Scheduler restarted but nothing happens**: the resource restart returns immediately; follow
  `aspire logs remediation`. A pass over 14 repos took 1-2 minutes.

## Findings

Divergences and surprises from the executed walkthrough. None of these were fixed (out of scope).

1. **Re-running pushes an empty commit.** Re-running the scheduler without a reset, for a repo that already has the
   update branch, pushes a new commit with an identical tree onto `autoremediator/dependency-updates`
   (`patch-bump`: `1eced66` after `c5812dc`, no file difference; run `a89fedff-a847-4661-a417-855661952621`,
   status `Completed`, 1 update). The PR record is refreshed (`headCommit`, `updatedAtUtc`) and no second record
   is created, as designed, but the branch grows by an empty commit per pass.
2. **"AI remediation" wording is wrong for a no-model run.** Run `9bc6385b-c7b5-4bae-9fb4-69868b8738b5` (0 attempts,
   transcript "No model is configured") renders: "A compile break in this run was handed to an AI agent, which proposed
   source edits that were re-verified. The attempts did not produce a building tree, so no pull request was opened."
   No agent acted and nothing was proposed. The section also shows an info-toned "0 attempts" pill.
3. **Approve clears the run's `updates`.** Run `590a8dc0-f685-4113-ad5b-073351671598` listed
   `Northwind.Messaging 3.2.0 -> 4.0.0` while `AwaitingReview`; after Approve it is `Completed` with an empty `updates`
   list (so the Updates table disappears) and no `verification` object, so there is no "Local verification" section
   or log link for an approved run. Discard (run `e19138d8`) kept its update and also has no verification.
4. **Stale diff links 404.** `pullRequestDiffUrl` is non-null for every `Completed` local run, including ones whose PR
   record no longer exists (e.g. run `264d6a8e-f707-46d5-92e6-fbca2a6c82e8`); the page then shows "Pull request changes
   are not available: 404 Not Found". Expected by the design notes, but the message is terse.
5. **restore-conflict emits `NU1605`, and three diagnostics for one problem.** Under baseline (run
   `a22a4b14-6498-4622-b7d5-3e8713293d52`) and the other profiles that run it the restore failure is
   `NU1605` (package downgrade, Abstractions 3.0.0 -> 1.0.0), not `NU1102`. The one error appears as three
   `NU1605` diagnostics because the "App -> ..." dependency-chain continuation lines are parsed as separate
   diagnostics. Under `major` (run `5d43038d`) it still fails (Abstractions 2.0.0 vs required >= 3.0.0). The AI loop is
   not entered, as expected.
6. **Cosmetic, seeder.** The seeded held run's `startedAtUtc` is about 5 minutes before the time it was seeded
   (fake run times), and the seeder's console output is mostly Azure SDK HTTP logging.
7. **PRD "missing branch / unknown repository" known defect** behaves exactly as written: `missing-branch` (run
   `9977db7c`) and `does-not-exist` (run `3fb456b3`) end `NoUpdates` with no error.

No expected-results cell diverged (every status, version and flag in the matrices matched), so the findings above
are product or doc observations rather than matrix failures.
