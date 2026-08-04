## Context

Slice 2 of the roadmap — the first automated PR. The remediation worker (an event-driven ACA Job) consumes `RemediationRunRequested` and, for the repo, bumps matched outdated `Orion180.*` packages and opens a pull request. Slice 1 already provides config stores, the read-only ADO client, manifest parsing/matching, and feed version resolution. This slice adds writes.

Confirmed decisions: **no local build** — validation is delegated to the repo's own ADO CI on the PR (so no clone, no SDK/git in the image); **one PR per repo** on a deterministic branch (`autoremediator/dependency-updates`), refreshed on re-run; **matched-only** bumps (no collateral, no AI); run errors/no-updates open no PR.

## Goals / Non-Goals

**Goals:**
- Compute `{package, from, to}` updates for matched outdated packages (reuse Slice-1 analysis pieces).
- Edit manifest XML in place to the target versions (text-level, no clone).
- Push the edits to a per-repo branch and open/refresh one PR via ADO REST.
- Drive a run state machine and persist run history to Table Storage.
- Extend the ADO client with the needed write operations.

**Non-Goals:**
- Local build/test (delegated to the repo CI), collateral/transitive bumps, the AI loop (Slice 3/5).
- Non-`Orion180.*`/third-party packages.
- API/UI for run history (Slice 6) — persistence only here.
- Container image changes (stays runtime; no SDK/git).

## Decisions

**1. Reuse analysis; add an update computation seam.**
Factor the Slice-1 per-repo analysis (read manifests → parse → match → resolve latest → status) so both the dependency map and the update computation use it. `IUpdatePlanner` produces `RepositoryUpdatePlan { repo, updates: [{packageId, from, to}], changedManifests: [{path, newContent}] }` from a repo, using the ADO client + feed resolver + a new `ManifestEditor`. Rationale: one source of truth for "what's outdated"; the planner is unit-testable with mocked ADO/feed.

**2. `ManifestEditor` edits text, not a DOM re-serialization.**
Setting a version does a targeted replacement of the `Version` attribute/element for the specific `PackageVersion`/`PackageReference` id, preserving all other bytes/formatting (regex anchored to the element for that id). Rationale: avoids XDocument reformatting the whole file (noisy diffs, lost comments). Only manifests containing an updated package are returned as changed. *Alternative:* parse+rewrite via XDocument — simpler but reformats; rejected for diff hygiene.

**3. ADO writes via REST pushes + pull requests — no clone.**
Extend `IAzureDevOpsClient`:
- `GetBranchHeadAsync(repo, branch)` → latest commit id (or null).
- `PushFilesAsync(repo, branch, baseCommitId, changes, message)` → `POST /_apis/git/repositories/{repo}/pushes` with a `refUpdates` entry (oldObjectId = existing branch head, or the target head when creating the branch) and one commit whose `changes` are `edit` operations carrying the new content.
- `EnsurePullRequestAsync(repo, sourceBranch, targetBranch, title, description)` → `GET pullrequests?searchCriteria.sourceRefName&status=active`; if found return it, else `POST pullrequests`.
Rationale: no working tree needed because we don't build; keeps the job lightweight. *Trade-off:* we can only `edit` existing files (adequate — manifests already exist); creating new files would need `add`, not needed now.

**4. Idempotent branch/PR by construction.**
Deterministic branch name `autoremediator/dependency-updates`. Push resets/updates that branch; the single active PR from it is reused. Re-runs refresh rather than duplicate. Commits may accumulate on the branch across runs — acceptable for the test tool; a later slice can squash/reset. Rationale: satisfies the one-PR-per-repo decision with minimal state.

**5. Run orchestration in a `RemediationRunner`; worker calls it.**
`Worker.Remediation` deserializes `RemediationRunRequested` and calls `IRemediationRunner.RunAsync(request)`. The runner walks Reading → Analyzing → Applying → CreatingPR, sets a terminal status (`Completed` | `NoUpdates` | `Failed`), and persists a `RemediationRun` via `IRemediationRunStore` (Table `runs`). The `IRemediationAgent` placeholder is left untouched for Slice 5. Rationale: keeps the worker thin and the pipeline testable; matches the run-orchestration capability.

**6. Persistence model.**
`RemediationRun` (Domain): id, repositoryId, repositorySlug, status, startedAtUtc, finishedAtUtc, updates (`[{packageId, from, to}]` serialized JSON), pullRequestUrl, error. Table `runs`, `PartitionKey = repositoryId`, `RowKey = runId` (so history is queryable per repo). Rationale: mirrors the config-store approach; per-repo partition supports Slice-6 history views.

**7. PAT scope grows.**
Writing branches/PRs needs Code (Read & Write) + Pull Request contribute on the PAT. This is a doc change to the Key Vault/PAT note — no code impact (same `AzureDevOps:Pat`).

## Risks / Trade-offs

- **[Delegated validation, not gated]** → We open a PR even if the bump would break the build; the repo CI shows red. Mitigation: intended for this slice; the AI loop (Slice 5) will gate/fix. The PR description states it is an automated dependency bump.
- **[Text edit misses exotic manifests]** → `$(Var)` versions, imported props, or `Update=` entries may not be editable by a simple targeted replace. Mitigation: only edit packages whose concrete version was parsed (Slice-1 marks variables as unknown → skipped); unmatched cases are left untouched and logged.
- **[Accumulating commits on the branch]** → Repeated runs append commits. Mitigation: acceptable now; note a future reset/squash. The PR still reflects the latest state.
- **[Push race / stale oldObjectId]** → If the branch head moved between read and push, the push is rejected. Mitigation: read head immediately before push; on conflict, mark the run `Failed` (retried on the next schedule).
- **[Idempotency across redelivery]** → Service Bus at-least-once could reprocess. Mitigation: the deterministic branch + active-PR reuse makes reprocessing converge to the same PR.

## Migration Plan

Additive. The remediation worker's placeholder body is replaced by the runner call. New Domain/Infrastructure types and the `runs` table (created on first use). No image or API change. Rollback = revert; local dev unaffected. In Azure, the PAT must gain write scopes before runs succeed.

## Open Questions

- Whether to reset the working branch to the target head each run (clean single commit) vs. appending (simpler) — starting with append; revisit if diffs get noisy.
- Commit author identity for the pushes (a bot name/email) — default to a configured `AutoRemediator` identity; confirm at apply.
- Exact ADO PR description format/labels — start with a concise markdown table of updates.
