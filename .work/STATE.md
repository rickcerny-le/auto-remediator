# State

**Active feature:** none (003-self-provisioning-scenarios implemented and verified; uncommitted on `rc/feat/self-provisioning-scenarios`)
**Active task:** none
**Queue:** empty
**Blockers:** none

## Done
- 001-ui-redesign: committed (2f20d57).
- 002-local-repo-mode: committed on `rc/feat/local-repo-mode` (020499a focus-ring fix, 510c118 feature).
- 003-self-provisioning-scenarios (tasks 01-07): implemented and verified for real, NOT yet committed. Plan committed as b7be283. Also uncommitted: web favicon.svg replacing the Blazor favicon.png.
  - Local mode `aspire start` now provisions `.local` (fingerprint skip, warm ~4 s, cold ~85 s), resets update branches/PR records (ResetOnStart default true), and enrolls 15 repos (14 enabled) with the baseline profile (`LocalRepos__Profile`). The scheduler is explicit-start unless `LocalRepos__RunOnStart=true`.
  - Functional suite 30/30 against the running app; Tools tests 37; fingerprint self-test 34/34.

## Findings to triage (docs/local-testing.md)
- 002 findings 1-7 (empty commit on re-run, no-model "AI remediation" wording, Approve clears Updates, stale diff links, NU1605 triplicates, cosmetic).
- 003 finding 8: transient `.git/logs/HEAD` permission error during warm reset (once in ~10 starts; unproven antivirus handle).
- 003 finding 9: dependents (`api`, `remediation`, `scheduler`) stay FailedToStart after a failed `local-scenarios` even if it later succeeds; must be started by hand.
- 003 finding 10: `-Reset`/`-Force` during a scheduler pass silently corrupts the pass.
- 003 finding 12: low-confidence log-tail gap for the enroll failure.
- Token-usage test fails under Ollama (budget may not be enforced).

## Next
- Commit 003; then pick fixes from the findings list.
- Next feature: revamp the deployed model setup (`infra/terraform` still provisions Foundry).
