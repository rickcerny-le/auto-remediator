# 01 LocalRepositoryClient and the Local mode switch

**Status:** done
**Feature:** 002-local-repo-mode
**Depends on:** none

## What to do
Add a second `IAzureDevOpsClient` implementation that works against plain git repositories on disk. Select it by configuration in `AddInfrastructure`. The Azure DevOps REST client stays the default, and its registration does not change.

1. **Options.** Extend `AzureDevOpsOptions` (section `AzureDevOps`) with:
   - `Mode`: `"AzureDevOps"` (default when absent) or `"Local"`, case-insensitive. Add a convenience `bool IsLocal`.
   - `LocalRoot`: folder that holds one git repo per managed repository `Name`.
   - `LocalPullRequestsRoot`: folder for PR records. When absent, it defaults to `Path.Combine(Path.GetDirectoryName(LocalRoot), "prs")`.
2. **DI.** In `AddInfrastructure`, read `AzureDevOps:Mode` from configuration. When it is `Local`, register `IAzureDevOpsClient` as a singleton `LocalRepositoryClient` and fail at startup (`ValidateOnStart` or an explicit throw) if `LocalRoot` is empty. Otherwise keep the existing `AddHttpClient<IAzureDevOpsClient, AzureDevOpsClient>` block byte-for-byte.
3. **`LocalRepositoryClient`** (`internal sealed`, `AzureDevOps/LocalRepositoryClient.cs`). It takes `IOptions<AzureDevOpsOptions>`, `TimeProvider` and `ILogger`. Map a repository to `{LocalRoot}/{repository.Name}`, ignoring organization and project. A name containing path separators, `..`, or invalid file-name characters is treated as a repo that does not exist. Mirror `AzureDevOpsClient` semantics:
   - `RepositoryExistsAsync`: true only when the folder is a git repo (`git rev-parse --git-dir` succeeds).
   - `GetManifestsAsync`: read from `refs/heads/{TargetBranch}` with `git ls-tree -r -z --name-only` and `git show <branch>:<path>`. Use the same `IsManifest` rule (`Directory.Packages.props`, `*.csproj`). Return paths rooted with `/`. Return an **empty list** for a missing repo or branch, as ADO does. Do not throw.
   - `GetBranchHeadAsync`: `git rev-parse --verify --quiet refs/heads/<branch>^{commit}`. Return null when the branch or repo is missing.
   - `GetRepositoryArchiveAsync`: `git archive --format=zip <commitId>` into a seekable `MemoryStream` at position 0. Throw `InvalidOperationException` on failure. Only committed content is included.
   - `PushFilesAsync`: never touch the source repo's working tree, index or checked-out branch. Create a temporary detached worktree at `baseCommitId` and write each change there. Reject a path that escapes the worktree. If the target file does not exist, throw `PushRejectedException("GitItemNotFoundException", ...)`. Write UTF-8 without a BOM. Commit with `-c user.name=AutoRemediator -c user.email=autoremediator@localhost -c commit.gpgsign=false commit --no-verify --allow-empty -m <message>`. Then publish with a compare-and-swap: `git update-ref refs/heads/<branch> <new> <old>`, where `<old>` is `baseCommitId` when the branch exists and the empty string (meaning "must not exist") when it does not. If the update fails, throw `PushRejectedException("GitRefUpdateStaleException", ...)`. Always remove the worktree in `finally` (`worktree remove --force`, then `worktree prune`).
   - `EnsurePullRequestAsync`: keep records under `{LocalPullRequestsRoot}/{repository.Name}/{id}.json`. Each record has these fields: `id`, `organization`, `project`, `repository`, `sourceBranch`, `targetBranch`, `baseCommit`, `headCommit`, `title`, `description`, `status: "active"`, `createdAtUtc`, `updatedAtUtc`, `url`.
     - `headCommit` is the current head of `refs/heads/<sourceBranch>`. `baseCommit` is `git merge-base refs/heads/<targetBranch> refs/heads/<sourceBranch>` (the commit the update branch was cut from, which stays correct if the target branch moves on later). Both are full commit ids. If either branch is missing, throw `InvalidOperationException`; the runner only calls this after a successful push.
     - If a record with `status == "active"` and the same `sourceBranch`/`targetBranch` exists, return its `url` unchanged. Refresh only its `baseCommit`, `headCommit` and `updatedAtUtc` (an ADO PR always shows the branch's latest push; task 09's diff view relies on this). Keep `id`, `url`, `createdAtUtc`, `title` and `description` as they were, and write no new file.
     - Otherwise write a new record with `id = max + 1`, starting at 1, and `updatedAtUtc = createdAtUtc`. The URL is `new Uri(fullPath).AbsoluteUri`, giving `file:///C:/...`.
     - Use `System.Text.Json` with indented web defaults (camelCase). Define the record shape as one `internal sealed record LocalPullRequestRecord` in its own file so task 09 reads the same type.
   - Git invocation helper: put it in its own `internal sealed class GitCli` (`AzureDevOps/GitCli.cs`) so task 09 can reuse it. Use `ProcessStartInfo("git")` with `ArgumentList` and no string concatenation. Read stdout as bytes and stderr concurrently. Set `GIT_TERMINAL_PROMPT=0`. Kill the process tree on cancellation. If git is missing (`Win32Exception`), raise `InvalidOperationException("git was not found on PATH; it is required when AzureDevOps:Mode is Local.")`.
4. **Tests** in `tests/AutoRemediator.Infrastructure.Tests/LocalRepositoryClientTests.cs`. Build temp repos with the git CLI, with a root-level file and `core.autocrlf=false`. Clean them up in `Dispose`. Self-skip with `Assert.Skip` when git is not on PATH. Cover:
   - exists true/false, including a bad name like `..\x`
   - manifests from the target branch, rooted paths, a non-manifest excluded, and an empty list for a missing branch and a missing repo
   - branch head, and null when missing
   - the archive contains committed files and not an uncommitted file
   - push creates the branch with new content, while `main`, the working tree file and `git status` are untouched
   - a second push based on the branch head advances it
   - a push with a stale base throws `PushRejectedException` with the stale type key
   - a push to a missing file throws with `GitItemNotFoundException`
   - no worktree is left behind (`git worktree list` shows one entry)
   - `EnsurePullRequest` writes `1.json` with the expected fields and a `file:///` URL; `baseCommit` equals `main`'s head at push time and `headCommit` equals the update branch head
   - after a second push advances the branch, a second call returns the same URL, writes no `2.json`, and `1.json`'s `headCommit` is the new branch head while `id`, `url` and `createdAtUtc` are unchanged
   - a different target branch gets `2.json`
5. Add a DI test to `InfrastructureExtensionsTests`: with `AzureDevOps:Mode=Local` and `LocalRoot` set, `IAzureDevOpsClient` resolves to `LocalRepositoryClient`. The existing tests (mode unset) still resolve `AzureDevOpsClient`.
6. Add the three `AzureDevOps:*` keys to the README Configuration table.

## Files
- `src/AutoRemediator.Infrastructure/AzureDevOps/AzureDevOpsOptions.cs`: `Mode`, `IsLocal`, `LocalRoot`, `LocalPullRequestsRoot`
- `src/AutoRemediator.Infrastructure/AzureDevOps/LocalRepositoryClient.cs` (new)
- `src/AutoRemediator.Infrastructure/AzureDevOps/GitCli.cs` (new): shared git invocation helper
- `src/AutoRemediator.Infrastructure/AzureDevOps/LocalPullRequestRecord.cs` (new): the PR record shape
- `src/AutoRemediator.Infrastructure/InfrastructureExtensions.cs`: mode switch
- `tests/AutoRemediator.Infrastructure.Tests/LocalRepositoryClientTests.cs` (new)
- `tests/AutoRemediator.Infrastructure.Tests/InfrastructureExtensionsTests.cs`: local-mode resolution test
- `README.md`: Configuration rows

## Out of scope
- Feed behaviour with local paths (task 02).
- AppHost wiring (task 05).
- Serving or rendering the PR diff (task 09). This task only stores `baseCommit`/`headCommit` on the record.
- Changing `AzureDevOpsClient`, `RemediationRunner` or the "missing branch reads as NoUpdates" behaviour.

## Done when
- [ ] With `AzureDevOps:Mode` unset, the registration code path for `AzureDevOpsClient` is unchanged (diff shows only the added branch), and the existing `InfrastructureExtensionsTests` pass.
- [ ] With `Mode=Local`, `IAzureDevOpsClient` resolves to `LocalRepositoryClient`; with `Mode=Local` and no `LocalRoot`, host startup fails with a message naming `AzureDevOps:LocalRoot`.
- [ ] Every case listed in step 4 has a test, and all of them pass: `dotnet test tests/AutoRemediator.Infrastructure.Tests --filter "FullyQualifiedName~LocalRepositoryClient|FullyQualifiedName~InfrastructureExtensions"`.
- [ ] The tests self-skip (not fail) when git is not on PATH. Checked by reading the guard.
- [ ] No git command is built by string concatenation, and no path from configuration or a repository name is used unvalidated.
- [ ] `dotnet build AutoRemediator.sln` has no new warnings, and `dotnet test AutoRemediator.sln` passes.

## Notes
- Port from `git show feat/local-remediation:src/AutoRemediator.Infrastructure/AzureDevOps/LocalGitClient.cs` and its `LocalGitClientTests.cs`. Required differences:
  - the class name and options (no `Git:` section)
  - PR records as JSON under `LocalPullRequestsRoot` rather than markdown in `.git`
  - idempotent PR lookup, with `baseCommit`/`headCommit` refreshed on the existing record
  - empty-string old value for branch creation (works for SHA-256 repos too)
  - `commit.gpgsign=false` and `--no-verify`
  - missing repo or branch returns empty or null instead of throwing
  - worktree prune
- The archive must have a root-level file, or `VerificationWorkspaceFactory.WrapperDirectory` may strip a top-level folder. `git archive` adds no prefix by default, so don't add one.
- `Path.GetFullPath(...).StartsWith(root + separator, OrdinalIgnoreCase)` is the traversal guard used elsewhere. Reuse that shape.
- Paths in `FileChange` arrive rooted (`/src/x.csproj`). Trim the leading `/` before combining.
