# 12 End-to-end verification in local mode

**Status:** todo
**Feature:** 004-per-repo-settings
**Depends on:** 11

## What to do
Run the whole feature against the local app once and record what actually happened. Do not fix product behaviour in this task: every divergence becomes a numbered finding in `docs/local-testing.md`, and Benny decides follow-ups.

1. **Cold start with the new catalogue.**
   - Run `aspire stop`, then `$env:LocalRepos__Enabled='true'; $env:Model__Enabled='false'; aspire start`.
   - Confirm that `local-scenarios` rebuilds (the fingerprint changed in task 10), and record the time.
   - Confirm that enrollment finishes 0 and the AppHost line has no profile.
   - Restart once with `$env:LocalRepos__Profile='major'` and confirm the deprecation line. Then unset it.
2. **One scheduler pass** (`aspire resource scheduler start`). Fill the per-repo matrix's Actual column in `docs/local-testing.md`: status, versions and flags read from `/api/runs/<id>` and the git repo, with run ids as 8-character prefixes.
3. **Inheritance checks:**
   - `minor-bump` (inherits) reaches 2.4.1 under global Minor.
   - `ignored-package`'s effective ignore list shows `Tailspin.Legacy.*` with source Repo.
   - `local-only` bumps `AdventureWorks.Text` and leaves `Contoso.Logging` at 1.2.0. Its effective view shows no global patterns, and the dependency map lists only AdventureWorks for it.
   - Add `Contoso.*` to the **global** patterns in the UI. Confirm that `local-only`'s effective view is unchanged and `minor-bump`'s shows it (source Both or Global). Then revert.
4. **Toggle checks:**
   - In the UI, turn inheritance off on `up-to-date` without adding patterns. Confirm the warning appears in the form and the effective view, and that Run now ends `NoUpdates`.
   - Restore the setting (or re-enroll).
5. **Validation checks:**
   - In the UI, try schedule `* * * * * *`, then `*/5 * * * *`, then `nonsense` on a repository, and the same values in the global default schedule.
   - Record each error message, and confirm it is announced. Check that the field has `aria-invalid` and is described by the error.
6. **Cron due/not-due:**
   - Start the scheduler again right after the first pass. Confirm it enqueues nothing ("not due" count in the logs). The built-in default is daily at 00:00 UTC, so avoid running this check across UTC midnight.
   - Confirm a repository without its own schedule shows `0 0 * * *` with source `Default` and its next run at the coming 00:00 UTC.
   - Restart with `$env:Scheduler__DevLoopEnabled='true'; $env:Scheduler__DevLoopIntervalSeconds='60'` (forwarded by the AppHost since task 10). The first tick runs every repo.
   - Then set one repo's schedule to a time a few minutes ahead (UTC, on a quarter hour, for example `45 14 * * *`).
   - Confirm that exactly that repo is enqueued once after the time passes, and not again on the following ticks. The repository page's "Last scheduled" and "Next runs" agree.
7. **Run now:**
   - Run now on `patch-bump` gives a `Manual` badge in Runs and Run detail, and a Completed or reused PR.
   - Run now on `disabled-repo` is offered, the UI says it is disabled and not scheduled, the run reaches Completed (Contoso.Logging -> 1.2.3) with the `Manual` badge, and the repository is still disabled afterwards. A following scheduler tick does not enqueue it.
   - Seed `held-review` with `held-run-seeder`, then Run now: confirm the 409 message links to the held run. Then start the scheduler while `held-review` is due (restart the AppHost and seed before the first pass), and confirm exactly one `SkippedHeld`.
8. **Automated checks against the running app:** set `WEB_BASE_URL`, plus the `ReviewGateTests` connection strings per `.work/agents/tester.md`. Run `dotnet test tests/AutoRemediator.Web.FunctionalTests` (axe, focus, `RunNowTests`, `RepositorySettingsTests` and the existing suites) and record the result in the doc's automated-checks section.
9. **Findings:** add a numbered finding for every divergence (run id, observed status, diagnostics). Add a short "Feature 004 check (actual)" subsection, like 003's "Self-provisioning check (actual)", with the timings and the results of steps 3 to 8.

## Files
- `docs/local-testing.md`: the Actual column, the 004 check subsection, and findings

## Out of scope
- Product fixes. Findings only.
- Deployed (Azure) verification. Terraform is not applied.

## Done when
- [ ] Every row of the per-repo matrix has an Actual entry with a run id (for `disabled-repo`: "no scheduled run", plus the run id of its Run now from step 7).
- [ ] Steps 3 to 7 are each recorded with their outcome. Any divergence is a numbered finding.
- [ ] The functional suite result against the running app is recorded, including the axe and focus results for `/configuration` and `/configuration/repositories/{id}`.
- [ ] `dotnet build AutoRemediator.sln` and `dotnet test AutoRemediator.sln` pass after `aspire stop`. Running them while the AppHost runs hits `bin`/`obj` locks.

## Notes
- Use `aspire start` / `aspire stop` from PowerShell. Never `dotnet run` the AppHost.
- The Web URL is the Vite dev server and is not shown by `aspire describe`; probe the ports (`.work/agents/tester.md`).
- Remember the 002 finding about re-runs creating empty commits. Repeated Run now on one repo can show it; record it against that finding rather than as a new one.
