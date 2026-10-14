# 06 Documentation

**Status:** todo
**Feature:** 003-self-provisioning-scenarios
**Depends on:** 05

## What to do
Rewrite the local-mode docs around "set env vars, `aspire start`", keeping everything from 002 that is still true.

1. **`docs/local-testing.md`:**
   - **Prerequisites:** the same list, plus a note that `pwsh`, `git` and `dotnet` must be on the PATH that `aspire start` inherits, and what the resource logs show when one is missing.
   - **Quick start** (replaces sections 1, 2 and 4 as the main path):

     ```powershell
     $env:LocalRepos__Enabled='true'; $env:Model__Enabled='false'; aspire start
     ```

     Describe what happens, in order: `local-scenarios` (rebuild about 85 s when stale, otherwise skip plus reset), then `api`/`remediation`, then `local-scenarios-enroll` (profile), then the `scheduler` pass. Explain where to watch it (`aspire describe`, `aspire logs local-scenarios`, `aspire logs local-scenarios-enroll`).
   - **Configuration table:** `LocalRepos__Enabled`, `LocalRepos__Root`, `LocalRepos__Profile` (default `baseline`), `LocalRepos__ResetOnStart` (default `true`), `Model__Enabled`.
   - **When it rebuilds:** the fingerprint inputs (generator files, absolute root, .NET SDK version) and the structural check; that line endings do not count; that hand commits to `main` survive warm starts but not a rebuild; that a moved checkout rebuilds; and where the fingerprint file is.
   - **Forcing a rebuild:** `aspire stop`, then `pwsh -NoProfile -File tools/local-scenarios/New-LocalScenarios.ps1 -Force`, then `aspire start`. Explain why you stop first (the lock, in-flight runs). State that the default script mode now skips when current.
   - **Resetting between profiles:** `aspire resource local-scenarios-reset start`, or `New-LocalScenarios.ps1 -Reset`. Then either restart the AppHost with a new `LocalRepos__Profile`, or run `Set-LocalScenarioConfig.ps1 -ApiUrl <api url> -Profile <p>` and `aspire resource scheduler restart`. Include the "what reset never touches" list, and warn not to reset during a scheduler pass. Replace the old manual `git branch -D` / `Remove-Item` snippet.
   - Keep the manual `Set-LocalScenarioConfig.ps1` usage (for `-Only` and profile switches) as a secondary path.
   - Keep sections 3 (non-agentic), 6 (what to observe), 7 (the expected and actual matrices, unchanged), 8 (review gate; enrollment is now automatic, so drop its manual enroll step but keep `-Only held-review` as optional), 9, Troubleshooting and Findings. The matrices and findings are historical records of the 002 run; do not edit their cells.
   - **Troubleshooting additions:**
     - `local-scenarios` failed or the dependents are `FailedToStart`;
     - a missing tool;
     - the generator lock is held;
     - a locked folder during a rebuild (`dotnet build-server shutdown`);
     - enrollment failed while the app is up (restart `local-scenarios-enroll`, then start `scheduler`);
     - an invalid profile.
   - Leave a placeholder "Timings" subsection for task 07 to fill in.
2. **`tools/local-scenarios/README.md`:** document the `New-LocalScenarios.ps1` modes (default / `-Force` / `-Reset` / `-ResetIfCurrent`) and the lock; `Test-LocalScenarioFingerprint.ps1`; `LocalScenarioSetup` (modes, exit codes 0/1/2/3, that the AppHost runs it); and the updated `HeldRunSeeder` steps (no manual generation or enrollment).
3. **Root `README.md`, "Local repository mode":** the quick start, the profile and reset env vars, the auto-provisioning in one sentence, the reset resource, and a link to the walkthrough.

## Files
- `docs/local-testing.md`
- `tools/local-scenarios/README.md`
- `README.md`

## Out of scope
- Recording new actual results or timings (task 07).
- `docs/PRD.md` and the infra docs.

## Done when
- [ ] A reader can go from a fresh clone to a completed baseline pass using only the quick start, with no manual script.
- [ ] Every env var, resource name, script switch and exit code mentioned matches the code (check against `AppHost.cs`, the wrapper and the scripts).
- [ ] The expected-results matrices and the 002 findings are present and unchanged: compare the section diff with `git diff`.
- [ ] Markdown renders cleanly: tables have header rows, and anchors used in links exist.

## Notes
- Keep the walkthrough's existing tone and structure; this is an update, not a rewrite of the matrix sections.
- The anchor `#8-review-gate` and others are linked from inside the doc. If headings are renumbered, fix the links.
