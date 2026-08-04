# AutoRemediator — Application Roadmap

North star for the application (product) capabilities, built **walking-skeleton first**:
a thin path is made to work end-to-end early, then each capability is deepened. Each
slice below becomes one OpenSpec change (propose → apply → archive); capabilities are
`ADDED` on first touch and `MODIFIED` as later slices deepen them.

This roadmap is a map, not a spec. Detailed requirements/scenarios are authored
**just-in-time** inside each slice's change, so they reflect what was actually built.

> Platform already in place (archived changes): solution scaffold, Aspire orchestration,
> testing foundation, managed-identity auth, Terraform test environment, per-service
> containers, and trunk-based CI/CD. This roadmap is the domain on top of that.

## Product flow

```
  scheduler (cron) ─▶ for each enabled repo ─▶ enqueue RemediationRunRequested
                                                   │ (Service Bus → remediation job, KEDA)
                                                   ▼
   RUN:
     1 READ      pull manifests from Azure DevOps (.csproj, Directory.Packages.props,
                 packages.lock.json)
     2 ANALYZE   match package patterns (Orion180.*) → matched packages;
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
    patterns: [ "Orion180.*" ]  (+ excludes)    # package-ID globs — what to target
    feeds:    [ MyOrionServicesPackageFeed ]    # where "latest" is resolved
    policy:   strategy (patch|minor|major), ignore, targetBranch
```

Decisions:
- **Explicit repo list** — the scheduler enqueues per configured repo (as the scaffold
  already does); there is no repository-discovery/org-crawl capability.
- **Matched-first, collateral-if-needed** — update the `Orion180.*` packages first; touch
  other dependencies only when the build forces it (discovered at build time / by the AI loop).
- **"Latest" comes from the private feed** (Orion180 feed); matching is on package ID as a glob.
- **Trigger is a scheduled timer** for now. Package-publish-triggered fan-out
  ("published Orion180.Core 2.0 → cascade to all consumers") is a later direction.
- **Build model: Option A** — the remediation ACA job clones + builds in-process
  (self-contained; job image carries the SDK). Delegating build to an ADO pipeline
  (Option B) is a later scaling option.
- **Config scope: global with optional per-repo override** — start global, no rework later.

## Capability map (walking-skeleton order)

| Slice | Capability | Owns | Replaces placeholder |
| --- | --- | --- | --- |
| 1 | **target-configuration** | repos[] + patterns + feeds + policy; persisted to Table Storage; API slices + Blazor page | `ManagedRepository` stub, List slice |
| 1 | **azure-devops-connectivity** | authenticated ADO client (read files/manifests; later create PR) + ADO/feed auth | `StubAzureDevOpsClient` |
| 1 | **dependency-analysis** | read manifests → match patterns → resolve latest from feed → outdated matched set | *(new)* |
| 2 | **update-execution** | apply matched updates in a workspace → restore/build(/test) → success or breakage; collateral bumps when the build demands | *(new)* |
| 2 | **pull-request-authoring** | branch/commit + PR with a change summary | *(part of the ADO client)* |
| 2 | **run-orchestration** | per-run state machine, status/history/artifacts persistence, idempotency (one open PR per repo/update-set), retries | remediation worker body |
| 5 | **ai-remediation-loop** | MAF + Foundry agent: build/test errors + diff → fixes (code and/or extra bumps) → re-run, budget-bounded | `MafRemediationAgent` placeholder |
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
           ▶ "across your N repos, here's every Orion180.* pin and what's outdated"
           ▶ read-only dependency map in the UI. No writes. First shippable thing.

  Slice 2  FIRST PR    update-execution + PR authoring + run status
           ▶ bump matched Orion180.* to latest, build, open a PR if green (no AI)

  Slice 3  DEPTH       policy (patch/minor/major, ignore) + collateral bumps to pass the build
  Slice 4  TESTS       run tests; classify build-break vs test-break
  Slice 5  AI LOOP     the MAF agent — now fed by real build failures
  Slice 6  UI          full run history / logs / transcripts / retry
```

## Domain shape (locked in at Slice 1)

```
  TargetConfig (global-ish)          ManagedRepository (per repo)
    patterns[]   "Orion180.*"          org / project / name
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
