# 02 Fast reset mode

**Status:** todo
**Feature:** 003-self-provisioning-scenarios
**Depends on:** 01

## What to do
Add a reset that returns the demo repos to their generated branch state in a few seconds, with no rebuild. It is used between profiles, by the explicit-start Aspire resource (task 04), and on warm starts (`-ResetIfCurrent`).

1. In `LocalScenarios.psm1`, add and export `Reset-ScenarioRepos -Root -Catalogue`. For every catalogue repo that is not `NoFolder` and whose folder exists under `<Root>/repos`:
   1. `git worktree prune`.
   2. `git checkout -q -f main`.
   3. `git reset -q --hard main`.
   4. `git clean -xdfq`.
   5. Delete every local branch that matches `autoremediator/*`, using `git for-each-ref --format=%(refname:short) refs/heads/autoremediator/` and then `git branch -D <name>` for each.

   Then delete the contents of `<Root>/prs` (keep the folder). Print one line per repo that changed (for example `patch-bump: deleted autoremediator/dependency-updates`) and a summary with the elapsed time. Use the existing `Invoke-Git` helper; it already passes `-c commit.gpgsign=false`.
2. In `New-LocalScenarios.ps1`:
   - Add `[switch]$Reset` and `[switch]$ResetIfCurrent`. `-Reset` and `-Force` are mutually exclusive: use parameter sets or an explicit check that exits 1 with a message.
   - `-Reset` takes the same lock as task 01 and runs `Test-ScenarioOutputCurrent`. If the output is not current, it exits 1 with "Local scenarios are missing or stale (<Reason>); run New-LocalScenarios.ps1 (or -Force) instead of -Reset." Otherwise it calls `Reset-ScenarioRepos` and exits 0.
   - `-ResetIfCurrent` behaves like the default mode, but when the output is current it calls `Reset-ScenarioRepos` before exiting 0. After a rebuild it does nothing more, because a fresh build is already clean.
3. The reset must not touch:
   - `<Root>/feed` or `scenarios.fingerprint.json`
   - the commits on `main`
   - `scenario/agent-fix` or any branch not under `autoremediator/`
   - the NuGet global packages folder
   - Azurite
   - anything outside `<Root>`

   Add these as a comment block above `Reset-ScenarioRepos`.
4. Extend `Test-LocalScenarioFingerprint.ps1` with reset cases on its fake root:
   - Create an `autoremediator/dependency-updates` branch with a commit, a stray untracked file and a `prs/x/1.json`. After `Reset-ScenarioRepos`, the branch, the file and the record are gone, while `main`, `scenario/agent-fix`, the fake feed files and the fingerprint file are unchanged (compare the hashes before and after).
   - A user branch `my-branch` survives the reset.
   - A hand commit on `main` survives the reset.

## Files
- `tools/local-scenarios/LocalScenarios.psm1`: `Reset-ScenarioRepos`
- `tools/local-scenarios/New-LocalScenarios.ps1`: `-Reset`, `-ResetIfCurrent`
- `tools/local-scenarios/Test-LocalScenarioFingerprint.ps1`: reset cases

## Out of scope
- The Aspire resource for the reset (task 04).
- Clearing Azurite run history. Old runs keep their diff links, which then 404 (002 finding 4, documented in task 06).
- Undoing hand commits on `main`. That needs `-Force`.

## Done when
- [ ] `pwsh -NoProfile -File tools/local-scenarios/Test-LocalScenarioFingerprint.ps1` exits 0, including the new reset cases.
- [ ] On a real `.local` that has update branches and PR records from a scheduler pass, `New-LocalScenarios.ps1 -Reset` completes in under 10 s (record the time). Afterwards no repo has an `autoremediator/*` branch, `.local/prs` is empty, the `.local/feed` nupkgs and `scenarios.fingerprint.json` have the same SHA-256 as before, and every repo is on `main` with a clean working tree.
- [ ] After the reset, `Test-ScenarioOutputCurrent` reports current (so the next startup does not rebuild).
- [ ] `-Reset` against a missing or stale `.local` exits 1 with the "run without -Reset" message. `-Reset -Force` is rejected.
- [ ] `-ResetIfCurrent` on a current tree resets. On a stale tree it rebuilds and does not reset afterwards.

## Notes
- Use `RemediationRunner.UpdateBranch` (`autoremediator/dependency-updates`) as the reference. Matching the `autoremediator/` prefix keeps the reset safe if the name ever gains siblings.
- Worktrees from a killed `remediation` worker live under `%TEMP%\autoremediator-wt-*`. `git worktree prune` removes their registrations once the folder is gone. If the folder still exists, the registration remains, and `git branch -D` of a branch checked out in it would fail. The client always uses `--detach`, so this should not happen. If it does, report it rather than force-removing temp folders.
