# 11 Documentation and PRD

**Status:** done
**Feature:** 004-per-repo-settings
**Depends on:** 09, 10

## What to do
Bring the docs in line with per-repo settings, cron scheduling and Run now. Remove every reference to settings profiles, and keep the 002/003 findings.

1. **`docs/local-testing.md`:**
   - **Quick start:** `LocalRepos__Enabled=true; Model__Enabled=false; aspire start`, then start the scheduler for the first pass, or use Run now on any repository. Remove `LocalRepos__Profile` from the configuration table. Add a line saying it is obsolete, ignored, and announced by the AppHost.
   - **New section "Per-repository settings":**
     - the global local baseline
     - how each scenario repo overrides it (link to `scenarios.psd1`)
     - the inheritance rule (lists union, scalars override)
     - the inheritance switch, demonstrated by `local-only`
     - where to see the effective settings (the repository page)
   - **New section "Scheduling and Run now in local mode":**
     - The first scheduler pass after `aspire start` runs every enabled repo, because Azurite is not persisted, so every repo is never-scheduled.
     - Later scheduler starts enqueue only due repos. With the built-in daily default `0 0 * * *`, none are due until after the next 00:00 UTC.
     - Run now re-runs one repo. It is refused (409) while a repo is held for review, and it works on disabled repos (`disabled-repo` demonstrates this; the repo stays unscheduled).
     - The scheduler's dev loop (`Scheduler__DevLoopEnabled`) acts as a tick.
   - **Replace the four profile matrices** (section 7) with one per-repo matrix: Repo | Settings (and source) | Expected | Actual. Leave Actual for task 12 to fill. Keep the "Non-agentic check" and "PR diff panel check" subsections. They now run under `major-breaking`'s own settings, so reword "under the `major` profile".
   - **Delete "Profiles and resetting between them".** Keep the reset instructions as "Resetting between runs" (the reset is still useful after many Run now pushes). Fix every anchor that pointed to the old headings.
   - **Review gate section:** run `held-run-seeder` **before** the first scheduler pass to see `SkippedHeld`. Otherwise Run now's 409 shows the hold.
   - **Troubleshooting:** replace the invalid-profile entry with the deprecation message. Add "a schedule rejected with 400" and "Run now says held".
   - **Findings:** keep every 002/003 finding verbatim. Mark finding 12 (the invalid-profile cosmetic) as obsolete with a one-line reason; do not delete it.
2. **`tools/local-scenarios/README.md`:** remove profiles, document the `Settings`/`Expected`/`GlobalSettings` keys, the `{feed}` token, `Set-LocalScenarioConfig.ps1` without `-Profile`, and the new `local-only` repo.
3. **`README.md`:** in the local-mode section, remove `LocalRepos__Profile`, mention per-repo scenario settings and Run now, and change "reset the repos between profiles" to "between runs". The worker table was already updated in task 09; check that it is consistent.
4. **`docs/PRD.md`:**
   - **§5 diagram:** "scheduler (cron tick) ─▶ for each enabled, non-held repo **that is due by its schedule**". Operator Run now is unchanged.
   - **§6.1:** describe the global baseline plus optional per-repository settings: list union, scalar override, one "do not inherit" switch (schedule excluded), the effective-settings view with sources, and that the whole pipeline (analysis, planning, verification, review commands, dependency map) uses the effective settings. Mention that repositories are editable and can be enabled or disabled in the UI.
   - **§6.4:** "Per-scope strategy" becomes "Strategy per the effective settings (global, overridable per repository)".
   - **§6.11:** due-based scheduling: 5-field UTC cron per repository (minimum 15 minutes), global default, built-in `0 0 * * *` (daily at 00:00 UTC); a 15-minute run-once tick; at most one run per repository per tick; no backfill; never-scheduled = due at the next tick; `SkippedHeld` only when due; ETag-claimed state in `schedulestate`. State that this replaces the old every-6-hours pass, so the default cadence is now daily.
   - **§6.12:** **Status:** *built*. Add the trigger badge, the 409 refusal while held, that Run now works on disabled repositories (to try a repository before enabling it; disabled means "not scheduled"), and that the acting user is recorded once §6.17 lands.
   - **§6.15:** the scheduler's deployed shape is the 15-minute tick (`scheduler_tick_cron_expression`).
   - **§7:** add "Effective settings" and "Due (schedule)".
   - **§8 decisions:** add a resolved item "Per-repository settings and schedules" summarising the layering, the switch (schedule excluded), Cronos 5-field UTC, the built-in daily default and the resulting cadence change from every 6 hours, the minimum interval (noted as an assumption open to revisiting), the anchor rule, never-scheduled = due, the Terraform variable rename, and Run now's rules (held refused, disabled allowed).
   - **Known defects:** change the configuration-UI entry to keep only "per-repository `Organization` is stored but ignored; one `AzureDevOps:OrganizationUrl` (and PAT) is used for everything", with the edit/enable half struck through as fixed by this feature. Add to the dependency-map entry that it now uses each repository's effective settings, but is still live.
   - **§3 / §9:** check there is nothing to change. The package-publish trigger stays a future direction, and a trigger enum now exists for it.

## Files
- `docs/local-testing.md`
- `tools/local-scenarios/README.md`
- `README.md`
- `docs/PRD.md`

## Out of scope
- Filling the "Actual" column (task 12).
- `.work/decisions.md` (Benny's file).

## Done when
- [ ] No mention of `LocalRepos__Profile`, `-Profile` or the `baseline`/`patch`/`major`/`prerelease` profiles remains in `docs/`, `README.md` or `tools/local-scenarios/README.md`, apart from the deprecation note and the historical findings. Check with a grep on `Profile` and `profile`.
- [ ] Every internal anchor link in `docs/local-testing.md` resolves to an existing heading. Check by hand or with a quick script.
- [ ] The PRD sections listed above are updated; §6.12 says *built*; the known-defects list matches the "Out" scope of FEATURE.md.
- [ ] The per-repo matrix lists all 16 scenario repos, including `local-only` and `disabled-repo`.

## Notes
- Keep the tone and table style of the existing docs (short cells, run ids as 8-character prefixes).
- The 002 finding about empty commits on re-runs matters more now that Run now makes re-runs easy. Add a sentence to that finding, without changing its status.
