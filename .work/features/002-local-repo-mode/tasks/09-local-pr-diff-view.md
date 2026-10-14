# 09 Read-only pull request diff on the run detail page (local mode)

**Status:** done
**Feature:** 002-local-repo-mode
**Depends on:** 01 (05 and 07 only for the live checks against a running app)

## What to do
In local mode a pull request is a JSON record on disk, and its `file://` link often does nothing in the browser. Let a person see what the pull request changes directly on the run detail page. The API computes the diff from the PR record's `baseCommit`/`headCommit` (stored by task 01) with the git binary, and the page renders it with the same file accordion the held-review proposal panel uses.

This view is **read-only**. It has no approve step, no buttons and no commands, and it changes nothing in the production flow. A run still ends at the PR (PRD non-goal). It exists only in local mode; on the Azure DevOps path the link is never emitted.

1. **Reader (Infrastructure).** Add a public `IPullRequestDiffReader` in `src/AutoRemediator.Infrastructure/AzureDevOps/`:
   - `bool IsAvailable { get; }`
   - `Task<PullRequestDiff?> GetDiffAsync(string pullRequestUrl, CancellationToken ct)`

   Add public result records `PullRequestDiff(SourceBranch, TargetBranch, BaseCommitId, HeadCommitId, IReadOnlyList<PullRequestDiffFile> Files)` and `PullRequestDiffFile(Path, ChangeKind, OriginalContent?, NewContent?, IsBinary, IsTruncated)`. `ChangeKind` is `Added`, `Modified` or `Deleted`. `Path` is rooted with `/`, as elsewhere.
   - `UnavailablePullRequestDiffReader` (internal): `IsAvailable = false`; `GetDiffAsync` returns null. Register it with `TryAddSingleton` on the default (Azure DevOps) path.
   - `LocalPullRequestDiffReader` (internal): registered instead when `AzureDevOps:Mode=Local`, inside task 01's mode branch. `IsAvailable = true`. `GetDiffAsync` does the following:
     - Return null unless the URL is an absolute `file` URI whose local path, after `Path.GetFullPath`, is inside `LocalPullRequestsRoot`. Reuse task 01's traversal guard. Return null if the file is missing or is not a valid `LocalPullRequestRecord` (the type from task 01).
     - Resolve the repo as `{LocalRoot}/{record.repository}`, applying the same name validation as `LocalRepositoryClient`.
     - Run `git diff --name-status -z --no-renames <baseCommit> <headCommit>` through task 01's `GitCli`. Map `A` → `Added`, `D` → `Deleted`, and everything else → `Modified`.
     - For each file, read the before side with `git show <base>:<path>` (null for `Added`) and the after side with `git show <head>:<path>` (null for `Deleted`).
     - Treat a file as binary if either side contains a NUL byte in its first 8 KB; then set `IsBinary` and leave both contents null. Decode text as UTF-8.
     - Cap each side at 256 KB and set `IsTruncated` when it is cut. Cap the list at 200 files, sorted by path (ordinal).
     - If either commit no longer exists (for example, the scenarios were regenerated), log a warning and return null. Never throw for a bad record or a missing commit.
2. **Contract and API.**
   - In `src/AutoRemediator.Contracts/Dtos/RunDtos.cs`, add `PullRequestDiffDto(SourceBranch, TargetBranch, BaseCommitId, HeadCommitId, IReadOnlyList<PullRequestDiffFileDto> Files)` and `PullRequestDiffFileDto(Path, ChangeKind, OriginalContent, NewContent, IsBinary, IsTruncated)`.
   - Append `string? PullRequestDiffUrl = null` as the **last** parameter of `RunDetailDto`, so existing constructions still compile.
   - In `RunsEndpoints`:
     - `ToDetail` sets `PullRequestDiffUrl` to `/api/runs/{id}/pull-request-diff` only when `reader.IsAvailable` and `run.PullRequestUrl` is not null. This follows the `ProposalUrl`/`TranscriptUrl` pattern: a relative `/api` link, or null when there is nothing to fetch. The detail endpoint gains an `IPullRequestDiffReader` parameter.
     - Add `GET /api/runs/{id:guid}/pull-request-diff`. Return 404 when the run is missing, has no `PullRequestUrl`, the reader is unavailable, or the reader returns null. Otherwise return 200 with the DTO.
     - The endpoint only reads: no store writes, no messages.
3. **Web** (`src/AutoRemediator.Web`):
   - `lib/types.ts`: add `pullRequestDiffUrl: string | null` to `RunDetail`, and add `PullRequestDiff`/`PullRequestDiffFile` types.
   - `lib/api.ts`: add `pullRequestDiff: (url: string) => json<PullRequestDiff>(url)`.
   - Extract the proposal panel's before/after accordion into a shared `src/components/file-comparison.tsx`. It takes the files (key, trigger label, before text, after text, and the placeholder for a missing side) and an `itemTestId`. The proposal panel uses it with `itemTestId="proposal-file"` and must render **identical markup and text** to today: same `defaultValue` (all open), `Before`/`After` labels, and "(file did not exist)".
   - `pages/run-detail.tsx`: when `run.pullRequestDiffUrl` is set, fetch the diff **separately** from the run, so a diff failure never breaks the page. Render a `Section` titled "Pull request changes", `testId="run-pr-diff"`, placed after the run header card and before "Held for review". It contains:
     - a `Pill` badge with the file count (`plural(n, 'file')`)
     - one sentence naming the source → target branches and the short base/head ids (`font-mono`), stating that this is a read-only view of the local pull request
     - the shared accordion with `itemTestId="pr-diff-file"`. The trigger label is `${changeKind}: ${path}`, so the change kind is conveyed in text, not colour. Placeholders are "(file did not exist)" for the missing side of an added or deleted file, "(binary file, not shown)" for binary files, and a visible "(truncated)" note when `isTruncated`.
     - "The pull request has no file changes." when the list is empty
     - "Loading pull request changes…" while it loads, and an inline muted message (not a page-level alert) if the request fails
   - No buttons in this section.
4. **Accessibility (WCAG 2.2 AA, feature 001 rules).** The section heading stays an `h2` (via `Section`). Accordion triggers keep the shared `focusRing` from `lib/utils.ts`. Do not use colour as the only signal for added/removed. Keep `whitespace-pre-wrap` on the `pre` blocks so they don't need horizontal scrolling. If any block can still scroll, it must be keyboard-focusable (`tabIndex={0}` with an accessible name) to satisfy axe `scrollable-region-focusable`. If you add a `DataTable` (for example a file summary), it must have a `caption`. Every existing `data-testid` stays.
5. **Tests:**
   - `tests/AutoRemediator.Infrastructure.Tests/LocalPullRequestDiffReaderTests.cs`: build temp git repos and records as in task 01; self-skip without git. Cover:
     - a modified file (before/after match `git show`)
     - an added file (before null) and a deleted file (after null), using commits made directly with the git CLI and a hand-written record
     - a binary file
     - truncation over 256 KB
     - an `https://` URL, a `file://` URL outside `LocalPullRequestsRoot`, a malformed record and an unknown commit, which all give null
     - the record refreshed by a second push (task 01), which shows the new head
   - `InfrastructureExtensionsTests`: with mode unset, `IPullRequestDiffReader.IsAvailable` is false; with `Mode=Local`, it is true.
   - `tests/AutoRemediator.Api.Tests/RunsEndpointTests.cs`: replace `IPullRequestDiffReader` with a fake, as the file already does for stores. Cover:
     - detail has `pullRequestDiffUrl` when available and a PR exists
     - it is null when unavailable (the default factory) or when there is no PR
     - the endpoint returns the fake's diff as the DTO
     - 404 for each not-found case
   - `tests/AutoRemediator.Web.FunctionalTests/PullRequestDiffTests.cs` (new; skipped unless `WEB_BASE_URL` is set and a run with a non-null `pullRequestDiffUrl` exists): the `run-pr-diff` section shows at least one `pr-diff-file` with "Before"/"After", and contains no button other than the accordion triggers.
   - `AccessibilityTests.cs`: add a `run-detail-pr-diff` case in both schemes that picks the first run whose detail has a `pullRequestDiffUrl` and waits until `run-pr-diff` has finished loading before running axe. Skip it with a clear message when no such run exists.

## Files
- `src/AutoRemediator.Infrastructure/AzureDevOps/IPullRequestDiffReader.cs` (new): interface and result records
- `src/AutoRemediator.Infrastructure/AzureDevOps/LocalPullRequestDiffReader.cs` (new)
- `src/AutoRemediator.Infrastructure/AzureDevOps/UnavailablePullRequestDiffReader.cs` (new)
- `src/AutoRemediator.Infrastructure/InfrastructureExtensions.cs`: register the reader per mode
- `src/AutoRemediator.Contracts/Dtos/RunDtos.cs`: diff DTOs, `RunDetailDto.PullRequestDiffUrl`
- `src/AutoRemediator.Api/Features/Runs/RunsEndpoints.cs`: link and endpoint
- `src/AutoRemediator.Web/src/lib/types.ts`, `src/AutoRemediator.Web/src/lib/api.ts`
- `src/AutoRemediator.Web/src/components/file-comparison.tsx` (new)
- `src/AutoRemediator.Web/src/pages/run-detail.tsx`
- `tests/AutoRemediator.Infrastructure.Tests/LocalPullRequestDiffReaderTests.cs` (new), `InfrastructureExtensionsTests.cs`
- `tests/AutoRemediator.Api.Tests/RunsEndpointTests.cs`
- `tests/AutoRemediator.Web.FunctionalTests/PullRequestDiffTests.cs` (new), `AccessibilityTests.cs`

## Out of scope
- Any approve, merge, comment or close action on a local PR, and any change to `RemediationRunner`, `ReviewCommandHandler`, the workers, the Domain, or the stored run.
- A diff view for Azure DevOps PRs (the link points there).
- Syntax highlighting, side-by-side line alignment, or a unified-diff renderer. Before/after full content per file is the chosen form, matching the proposal panel.
- Per-run snapshots of what each run pushed. The view shows the PR's current state (the record's latest `headCommit`).

## Done when
- [ ] With `AzureDevOps:Mode` unset, `GET /api/runs/{id}` never contains a non-null `pullRequestDiffUrl`, and `/pull-request-diff` returns 404 (API test).
- [ ] All tests in step 5 exist and pass:
  - `dotnet test tests/AutoRemediator.Infrastructure.Tests --filter "FullyQualifiedName~LocalPullRequestDiffReader|FullyQualifiedName~InfrastructureExtensions"`
  - `dotnet test tests/AutoRemediator.Api.Tests --filter "FullyQualifiedName~RunsEndpoint"`
- [ ] `git diff --stat` shows no change under `src/AutoRemediator.Domain`, `src/AutoRemediator.Worker.*`, `src/AutoRemediator.Infrastructure/Remediation` or `src/AutoRemediator.Infrastructure/Review`. The new endpoint is a `MapGet` that writes nothing.
- [ ] The proposal panel renders the same as before: `ReviewGateTests` pass against a running app (they seed their own held run, so the Azurite connection strings must be set), and `proposal-file` still exists.
- [ ] `npm run lint && npm run typecheck && npm run build` pass in `src/AutoRemediator.Web`.
- [ ] Against a running app in local mode (task 05) with a `Completed` scenario run (for example `patch-bump` from task 07):
  - the run detail page shows "Pull request changes" with `Modified: /Directory.Packages.props`, and the before/after match `git -C .local/repos/patch-bump show main:Directory.Packages.props` and the update branch
  - `PullRequestDiffTests` and the new `AccessibilityTests` case pass (not skip) in both themes, with zero axe violations
- [ ] Keyboard check, recorded in the review notes: the accordion triggers are reachable with Tab, show the focus ring, and toggle with Enter/Space.
- [ ] `dotnet build AutoRemediator.sln` has no new warnings, and `dotnet test AutoRemediator.sln` passes (the git-dependent tests self-skip without git).

## Notes
- Follow the existing link shape: `ProposalUrl` is built in `RunsEndpoints.ToDetail` as `$"/api/runs/{r.Id}/proposal"`, and the page fetches it with `api.proposal(run.proposalUrl)`.
- `AccessibilityTests.WaitForPageAsync` waits for the exact text "Loading…" to disappear. The diff's "Loading pull request changes…" is different text, so the new case must wait on the `run-pr-diff` section itself.
- `AutoRemediator.Api.Tests` is flaky on `main` (PRD Known defects: host-build timeouts). Re-run a timed-out test before treating it as a failure, and say so in the review notes.
- Frontend memory: the repo mixes CRLF and LF, so preserve each file's endings. There is no Python; use node `.cjs` scripts for scripted edits.
- `git show <commit>:<path>` takes the path without the leading `/`. Strip it before the call and add it back on the result.
