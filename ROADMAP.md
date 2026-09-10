# AutoRemediator — Application Roadmap

North star for the application (product) capabilities, built **walking-skeleton first**:
a thin path is made to work end-to-end early, then each capability is deepened. Each
slice below becomes its own feature branch, squash-merged into `main`.

This roadmap is a map, not a spec — it describes the shape of the work, not the
requirements/scenarios of what was actually built.

> Platform already in place: solution scaffold, Aspire orchestration, testing
> foundation, managed-identity auth, Terraform test environment, and per-service
> containers. This roadmap is the domain on top of that.

## Product flow

```
  scheduler (cron) ─▶ for each enabled repo ─▶ enqueue RemediationRunRequested
                                                   │ (Service Bus → remediation job, KEDA)
                                                   ▼
   RUN:
     1 READ      pull manifests from Azure DevOps (.csproj, Directory.Packages.props,
                 packages.lock.json)
     2 ANALYZE   match package patterns (Contoso.*) → matched packages;
                 resolve latest from the feed → outdated set
     3 APPLY     bump matched packages → restore → build (→ test);
                 bump OTHER deps only if the build demands it (collateral)
     4 green? ── yes ─▶ changes are safe
        └─ no ─▶ AI LOOP: build/test errors + diff → MAF agent → fix
                 (code and/or extra bumps) → rebuild/retest → repeat until
                 green or budget exhausted
     5 PR        branch + commit + open PR with a summary
     6 RECORD    status, history, logs, diffs, AI transcript
                                                   ▼
   UI: configure repos+patterns+policy · watch runs · read logs · retry
```

## Configuration model (decided)

**Package-first targeting over an explicit repo allowlist.** You list the repos and the
package patterns; the wildcard fans out across those repos.

```
  target-configuration:
    repos:    [ org/project/repo, ... ]         # explicit allowlist (no org crawl)
    patterns: [ "Contoso.*" ]  (+ excludes)    # package-ID globs — what to target
    feeds:    [ MyOrionServicesPackageFeed ]    # where "latest" is resolved
    policy:   strategy (patch|minor|major), ignore, targetBranch
```

Decisions:
- **Explicit repo list** — the scheduler enqueues per configured repo (as the scaffold
  already does); there is no repository-discovery/org-crawl capability.
- **Matched-first, collateral-if-needed** — update the `Contoso.*` packages first; touch
  other dependencies only when the build forces it (discovered at build time / by the AI loop).
- **"Latest" comes from the private feed** (Contoso feed); matching is on package ID as a glob.
- **Trigger is a scheduled timer** for now. Package-publish-triggered fan-out
  ("published Contoso.Core 2.0 → cascade to all consumers") is a later direction.
- **Build model: Option B (chosen at Slice 2)** — the tool does **not** build locally.
  It bumps versions and opens a PR; the targeted repo's own Azure DevOps CI validates
  the change on that PR. This keeps the remediation job lightweight (no SDK/git, no
  clone; writes via ADO REST) and aligns with running everything else locally. In-job
  building (Option A) remains a possible later change if local gating is wanted.
- **Config scope: global with optional per-repo override** — start global, no rework later.

## Capability map (walking-skeleton order)

| Slice | Capability | Owns | Replaces placeholder |
| --- | --- | --- | --- |
| 1 | **target-configuration** | repos[] + patterns + feeds + policy; persisted to Table Storage; API slices + Blazor page | `ManagedRepository` stub, List slice |
| 1 | **azure-devops-connectivity** | authenticated ADO client (read files/manifests; later create PR) + ADO/feed auth | `StubAzureDevOpsClient` |
| 1 | **dependency-analysis** | read manifests → match patterns → resolve latest from feed → outdated matched set | *(new)* |
| 2 | **update-execution** | bump matched outdated packages, edit manifest text (no clone/build); validation delegated to the repo's ADO CI on the PR | *(new)* |
| 2 | **pull-request-authoring** | branch/commit + PR with a change summary | *(part of the ADO client)* |
| 2 | **run-orchestration** | per-run state machine, status/history/artifacts persistence, idempotency (one open PR per repo/update-set), retries | remediation worker body |
| 3 | **update-policy** | target-version selection by strategy (patch/minor/major relative to current) + ignore list; needs no build signal | *(new; wires existing `UpdatePolicy` fields into version selection)* |
| 3b | **dependency-graph-alignment** *(collateral, split out)* | when a matched bump forces bumping its `Contoso.*` siblings — resolved from feed dependency metadata (local-first), not a local build | *(new; deliberate signal choice — see below)* |
| 5 | **ai-remediation-loop** | MAF + Foundry agent for **compile/API** breaks (source edits) — the class version math can't fix | `MafRemediationAgent` placeholder |
| 6 | **run-observability-ui** | run history, live status, drill into logs/diffs/AI transcript, retry | *(new)* |

Cross-cutting **secrets-and-identity** (ADO PAT/Entra + private-feed credentials) gates
connectivity; settle it inside the Slice-1 connectivity work (may become its own small capability).

## Dependency shape

```
        target-configuration
                │
                ▼
        azure-devops-connectivity ───────────────┐
                │              │                   │
                ▼              ▼                   ▼
        dependency-      pull-request-       secrets-and-identity
          analysis         authoring            (gates connectivity)
                │              ▲   ▲
                ▼              │   │
        update-execution ─────┘   │
                │                  │
                ▼                  │
        ai-remediation-loop ───────┘
                │
                ▼
        run-orchestration ──▶ run-observability-ui
```

## Slice sequence

```
  Slice 1  VISIBILITY  target-config + ADO read/auth + analysis
           ▶ "across your N repos, here's every Contoso.* pin and what's outdated"
           ▶ read-only dependency map in the UI. No writes. First shippable thing.

  Slice 2  FIRST PR    update-execution + PR authoring + run status
           ▶ bump matched Contoso.* to latest, open a PR; the repo's ADO CI
             validates it (no local build, no AI)

  Slice 3  POLICY      update strategy (patch/minor/major) + ignore list; no build
                       signal needed — pure version selection
  Slice 3b COLLATERAL  intra-family alignment: bumping one Contoso.* forces
                       compatible bumps of its siblings, resolved from feed
                       dependency metadata (local-first, no build)
  Slice 5  AI LOOP     the MAF agent for compile/API breaks (source edits)
  Slice 6  UI          full run history / logs / transcripts / retry
```

> **Note on the collateral split (decided during exploration):** "collateral bumps
> to pass the build" conflated two breakage classes. RESTORE-TIME conflicts (a bumped
> package's declared dependencies clash with other pins) are **version math** — for an
> internal family this is aligning the `Contoso.*` set, computable from feed metadata
> with no build (Slice 3b). COMPILE-TIME / API breaks need source edits — that is the
> AI loop (Slice 5). Slice 2's "no local build" removed the signal for both, but they
> need different (and cheaper) signals. The old Slice-4 "run tests / classify breakage"
> is folded away: local build/test (Option A) is only revisited if the metadata + CI
> path proves insufficient.

```
   COLLATERAL SIGNAL SPECTRUM (chosen when Slice 3b is proposed):
   metadata-only ───── restore-only ───── full build ───── CI-feedback
   (align family from  (dotnet restore;   (Option A;       (poll the PR's
    feed deps; local)   SDK⊂build)         SDK/git)         ADO CI, async)
   ↑ most local-first, fits the run-everything-locally goal
```

## Domain shape (locked in at Slice 1)

```
  TargetConfig (global-ish)          ManagedRepository (per repo)
    patterns[]   "Contoso.*"          org / project / name
    excludes[]                         enabled
    feeds[]                            targetBranch
    policy(strategy, ignore)           policy override? (optional)

  RemediationRun                      DependencyUpdate (child)
    id, repositoryId                    package, from → to
    status: Queued → Reading →          kind: matched | collateral
      Analyzing → Applying →            updateType: patch | minor | major
      Building → Remediating(AI) →
      CreatingPR →
      Completed | Failed | NoUpdates
    matchedPackages[], prUrl,
    summary, error, artifacts → blob
```

`RemediationRunRequested` (already in `Contracts`) is the trigger message; `RemediationRun`
is its persisted, evolving counterpart.

## Open questions (resolve inside the relevant slice)

- **secrets-and-identity (Slice 1):** ADO auth = PAT in Key Vault vs Entra app / workload
  identity federation; how private-feed credentials are supplied to restore/build.
- **AI loop bounds (Slice 5):** max iterations, token/cost budget, and the hard safety rule
  (never push to a protected branch — always a PR).
- **Idempotency (Slice 2):** keying an "update-set" so a re-run updates the existing PR
  rather than opening duplicates.
- **Build model revisit (later):** if in-job builds get heavy, move to Option B
  (delegate build/test to an ADO pipeline the worker triggers and polls).
