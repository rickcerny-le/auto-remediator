# 07 Cold/warm start verification with timings

**Status:** done
**Feature:** 003-self-provisioning-scenarios
**Depends on:** 06

## What to do
Run the new flow for real, end to end, and record what happened in `docs/local-testing.md` (the Timings subsection plus a short "Self-provisioning check (actual)" section). Use PowerShell, `aspire start` and `aspire stop`; never `dotnet run` on the AppHost.

1. **Automated checks first:**
   - `aspire stop`
   - `dotnet build AutoRemediator.sln`
   - `dotnet test AutoRemediator.sln`
   - `pwsh -NoProfile -File tools/local-scenarios/Test-LocalScenarioFingerprint.ps1`

   Record the pass counts.
2. **Cold start:**
   1. `aspire stop`, then delete `.local`.
   2. `$env:LocalRepos__Enabled='true'; $env:Model__Enabled='false'; aspire start`.
   3. Record, from the timestamps in `aspire logs` and `aspire describe`:
      - the `local-scenarios` duration;
      - when `api` became healthy;
      - the `local-scenarios-enroll` duration;
      - when the scheduler pass started;
      - when the last run finished.
   4. Confirm that the baseline matrix holds, either every row or a spot check of at least `patch-bump`, `minor-bump`, `family-alignment`, `restore-conflict`, `lock-file` and `disabled-repo`, reading `/api/runs/<id>` and the git state as in 002.
3. **Warm start:**
   1. `aspire stop`, then `aspire start` with nothing changed.
   2. Record the `local-scenarios` duration and confirm that it logged "up to date" and the reset.
   3. Confirm that the update branches from the cold pass were deleted before the new pass. Check the `git log` timestamps on `autoremediator/dependency-updates`: the branch has exactly one commit after the new pass.
   4. Confirm that `.local/prs/<repo>/1.json` was recreated, not refreshed: its `createdAtUtc` is from this session.
4. **Warm start without reset:** `$env:LocalRepos__ResetOnStart='false'`. Record that the branches persist and that finding 1 (an empty commit) reappears or not.
5. **Invalidation:** add a trailing blank line to `scenarios.psd1` (do not commit it), then `aspire start`. Confirm a rebuild with a reason that names the fingerprint. Revert the change and confirm the next start rebuilds again (the fingerprint changed back), then skips on the start after that.
6. **Profile:** `$env:LocalRepos__Profile='major'`, then `aspire start`. Confirm that `major-compiles` is `Completed` with 2.0.0 and `major-breaking` is `VerificationFailed` with 0 attempts. Then set `bogus` and record the states of `local-scenarios-enroll`, `scheduler`, `api` and `web`.
7. **Reset resource:** after a pass, `aspire resource local-scenarios-reset start`. Record its duration and that the feed and fingerprint hashes are unchanged.
8. **Missing tool:** start `aspire` from a shell whose PATH lacks the pwsh folder. Record the `local-scenarios` log line, its exit code and the dependents' states. Restore PATH.
9. **Held review:** `aspire resource held-run-seeder start` works with no manual enrollment.
10. **Concurrency:** while the AppHost's `local-scenarios` is rebuilding, run `New-LocalScenarios.ps1` by hand and confirm exit 3 with the lock message.

Write a timings table with these rows, each with seconds measured and the machine noted:
- cold `local-scenarios`
- warm `local-scenarios`
- warm without reset
- reset resource
- enrollment
- `aspire start` to the first run finished, cold and warm

Record any divergence as a new numbered finding under Findings. Do not fix it.

## Files
- `docs/local-testing.md`: Timings and "Self-provisioning check (actual)"

## Out of scope
- Fixing anything found. Report it to Benny as a finding.
- A full four-profile matrix re-run (002 did that). Step 6 is a spot check.

## Done when
- [ ] Every numbered step above has a recorded result (actual numbers, statuses and run ids) in `docs/local-testing.md`.
- [ ] Warm `local-scenarios` is under 15 s and cold is within about 30 s of the 85 s baseline. If either is not, it is recorded as a finding with the measured value.
- [ ] `aspire stop` is run at the end, and `.local` is left in a current state, so the next `aspire start` is warm.

## Notes
- `aspire describe --format Json` writes UTF-16 when redirected to a file.
- The Web URL is the Vite dev server; it is not always shown in `aspire describe`, so probe the ports.
- Azurite data is not persisted, so each start's run history is fresh. That is expected.
