# State

**Active feature:** none (004-per-repo-settings implemented and verified; committed on `rc/feat/per-repo-settings`)
**Active task:** none
**Queue:** empty
**Blockers:** none

## Done (branches are stacked: ui-redesign -> local-repo-mode -> self-provisioning-scenarios -> per-repo-settings)
- 001-ui-redesign, 002-local-repo-mode (incl. focus-ring fix), 003-self-provisioning-scenarios, 004-per-repo-settings (Run now, layered per-repo settings + do-not-inherit switch, cron scheduling with daily default, due-based scheduler, effective-settings view/UI, schedule visibility, per-repo local scenarios).
- 004 verified live: cold start auto-enrolls 16 repos; first pass enqueued 14 + 1 SkippedHeld; real cron tick ran `up-to-date` exactly once at 13:00 UTC; functional suite 46/46; unit suites 0 failures.

## Findings to triage (all in docs/local-testing.md and the PRD known defects)
- 14 cold rebuild took ~255 s (doc said ~85 s; cause not found).
- 15 settings form never shows the "No package patterns" warning (only the effective card).
- 16 at 390 px the repos table hides columns after Name and the effective table clips Source, with no scroll cue.
- 17 per-field errors have no role; focus lands on body after a failed save.
- 18 first "next run" for a never-scheduled repo is the current time.
- 19 `VerificationEndToEndTests.No_workspace_directory_survives_a_run` is racy (counts the shared %TEMP%\autoremediator directory); fix: count only the test's own directories.
- 20 header/sidebar look mid-page in full-page screenshots; "Settings saved." alert shifts the page (unchecked in a real scroll).
- 21 profile deprecation notice only in the AppHost log file.
- Earlier: empty commit on re-run (finding 1, reconfirmed), no-model "AI remediation" wording, Approve clears Updates, stale PR diff links, NU1605 triplicates, dependents not recovering after failed local-scenarios, -Reset/-Force mid-pass corrupts the pass, transient .git/logs/HEAD permission error, Pkcs transitive advisory, Azurite tests hardcode default port (skip under Aspire), token-usage test fails under Ollama, terraform validate not run, deployed model setup still Foundry.

## Next
- Triage the findings; likely next features: deployed model setup revamp (Foundry -> Ollama/infra), a findings-fix sweep (empty commit, workspace test race, Pkcs pin, a11y gaps 15-17).
