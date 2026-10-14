# State

**Active feature:** none (002-local-repo-mode implemented, awaiting commit)
**Active task:** none
**Queue:** empty
**Blockers:** none

## Last completed
- 001-ui-redesign: committed (2f20d57) on `rc/feat/ui-redesign`.
- 002-local-repo-mode (tasks 01-10 done) on branch `rc/feat/local-repo-mode`. Also includes a focus-ring fix (shared `focusRing` drew no outline under Tailwind v4) with a real-browser test. Walkthrough executed: all scenario outcomes matched the matrix; functional suite 30/30 against the running local app. Findings are in `docs/local-testing.md`.

## Next
- Commit 002; decide which walkthrough findings become fixes (empty commit on re-run, no-model "AI remediation" wording, Approve clearing the Updates table, stale diff links, NU1605 triplicates).
- Next feature: revamp the deployed model setup (`infra/terraform` still provisions Foundry).
