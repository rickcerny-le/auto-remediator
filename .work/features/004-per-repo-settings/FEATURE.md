# Per-repository settings, cron scheduling and Run now

**Status:** todo
**Created:** 2026-10-14

## Goal
Let each managed repository carry its own targeting settings and schedule on top of one global baseline, let an operator run a single repository on demand, and make every part of the pipeline use the settings that actually apply to that repository. Today one global `TargetingSettings` drives every repository, the scheduler enqueues every enabled repository on each fire, there is no manual trigger (PRD §6.12), and repositories cannot be edited or enabled/disabled in the UI. Afterwards an operator can see, per repository, which settings apply and where each value comes from. The local scenarios become self-describing: each demo repo carries its own settings, so the global profiles and the reset-between-profiles routine go away.

## Scope
**In:**
- **Run now** (PRD §6.12): `POST /api/repositories/{id}/run`, a shared run requester used by both the API and the scheduler, a run trigger (`Scheduled` | `Manual`) on the message, the run, the run DTOs and a UI badge. Refused while the repository is held for review. Allowed on disabled repositories (so an operator can try a repository before enabling its schedule).
- **Layered settings:** a per-repository `RepositorySettings` (lists, scalar overrides, schedule, one inheritance switch), one pure resolver that produces the **effective settings** with a source for every value, storage columns, and use of the effective settings by the runner, the review handler (Rebuild/Retry), the analyzer/planner (through their callers) and the dependency map.
- **API:** `GET /api/repositories/{id}`, `GET/PUT /api/repositories/{id}/settings`, `GET /api/repositories/{id}/effective-settings`, validation (400 `ValidationProblem` with field keys), a `defaultSchedule` on the global settings, and new optional DTO fields appended last.
- **UI:** an enable/disable switch and a Run now button on each repository row, a repository detail page (edit the repository, edit its settings, see the effective settings with sources, Run now), a default-schedule field on the global settings, and a `Manual` badge on runs. Everything follows the 001 accessibility rules.
- **Cron scheduling:** a per-repository cron schedule (5-field, UTC), a global default schedule, and a built-in default of daily at UTC midnight (`0 0 * * *`) when neither is set. The scheduler becomes a frequent tick (every 15 minutes in Terraform, still a run-once cron Container Apps Job) that enqueues only due repositories, at most one run per repository per tick, with per-repository schedule state and optimistic concurrency. The Terraform variable `scheduler_cron_expression` is renamed `scheduler_tick_cron_expression`, because its meaning changes from "cadence" to "tick".
- **Local scenarios:** per-repo settings in `scenarios.psd1`, enrollment sending them, a global local baseline (patterns plus the local feed only), a new `local-only` scenario for the inheritance switch, removal of the four profiles, and deprecation of `LocalRepos__Profile`.
- **Docs:** `docs/local-testing.md` (a per-repo expected-results matrix; the 002/003 findings stay), `tools/local-scenarios/README.md`, `README.md`, `infra/terraform/README.md`, and the PRD (§5, §6.1, §6.11, §6.12, §8 decisions and known defects).
- **Known defect fixed:** "repositories can't be edited or enabled/disabled in the UI".

**Out:**
- **Per-repository `Organization` routing** (the second half of that known defect). Repositories still all go through the single `AzureDevOps:OrganizationUrl` and one PAT. Fixing it properly needs a credential per organization (a PAT is normally organization-scoped), a client or `HttpClient` per organization, and Key Vault naming. That is a feature of its own, not a minimal fix. This feature keeps the field editable, and the UI labels it as informational. The PRD known defect is narrowed to this half.
- Named presets (Contoso/Fabrikam). There is one global baseline only.
- Per-repository time zones. Schedules are UTC only.
- Removing a single global list entry for one repository. That needs the inheritance switch, which drops all of them.
- Dependency-map caching (the "computed live" known defect stays). The map only switches to effective settings.
- Authentication, and recording the acting user on manual runs (§6.17 is not built; the trigger field leaves room for a `RequestedBy` later).
- A `Queued` run status. A manual run appears in the feed once the worker picks it up, as scheduled runs do today.
- The daily token cap and the feed mapping decision (both still not built).
- Porting the scenario generator to C#.

## Acceptance criteria
- [ ] `POST /api/repositories/{id}/run` returns 202 with `{ runId, enabled }` and enqueues one `RemediationRunRequested` with trigger `Manual`. It returns 404 for an unknown repository and 409 with the held run id while the repository is `AwaitingReview`. On a disabled repository it returns 202 and the run proceeds normally (the repository stays disabled and is still never scheduled). The resulting run shows `Manual` in the runs feed and on the run detail page; scheduled runs show no badge.
- [ ] Repositories and settings stored before this feature load unchanged: no per-repo settings, inheritance on, no schedule (so the global default applies), and runs without a trigger read as `Scheduled`. A round-trip test writes a legacy-shaped entity and reads it back.
- [ ] The resolver unit tests cover: union of lists with the global entries first and case-insensitive de-duplication; scalar repo-wins and inherited-when-null; the inheritance switch off (only the repo's lists, unset scalars fall back to built-in defaults, the schedule still falls back to the global default); empty patterns with inheritance off (no patterns, plus a warning); and the source of every value.
- [ ] The runner, the review handler (Rebuild and Retry), and the dependency map all obtain settings through the effective-settings provider, and nothing outside the provider and the global settings endpoint reads `ITargetingSettingsStore`. A test shows a repo-only pattern being bumped by a run and appearing in the dependency map for that repository only.
- [ ] `GET /api/repositories/{id}/effective-settings` returns every value with its source (`Global`, `Repository`, `Both`, `Default`), the warnings, and (after task 09) the last and next scheduled times. The repository detail page shows this, and its table has a caption.
- [ ] An invalid cron expression, an expression that fires more often than every 15 minutes, a 6-field (seconds) expression, or an unknown strategy is rejected with 400 and a field-keyed error. The UI shows the error next to the field (`aria-invalid` plus `aria-describedby`).
- [ ] The scheduler enqueues a repository only when it is due. A repository is due when it has never been scheduled, or when the next cron occurrence after its last scheduled time is at or before now. It enqueues at most one run per repository per tick however many occurrences were missed, still records `SkippedHeld` for a due held repository (only when due, so not on every tick), never evaluates disabled repositories, and two overlapping ticks cannot both claim the same repository.
- [ ] A repository with no schedule and no global default schedule resolves to the built-in `0 0 * * *` (daily, UTC midnight) with source `Default`.
- [ ] Terraform's `scheduler_cron_expression` is replaced by `scheduler_tick_cron_expression`, defaulting to `*/15 * * * *`; the deployed shape is still a run-once Schedule-triggered job; `infra/terraform/README.md` explains the rename and that the default per-repository cadence changes from every 6 hours to daily.
- [ ] Repository rows have an accessible enable switch and Run now button. Run now is offered on disabled repositories too, and the UI makes clear that a disabled repository will not run on schedule. The repository detail page and the changed Configuration page pass `AccessibilityTests` (light and dark) and `FocusVisibleTests`; the new route is added to both. Interactive elements use `focusRing`, and new elements have `data-testid` hooks.
- [ ] Local mode: `LocalRepos__Enabled=true; Model__Enabled=false; aspire start`, then starting the scheduler, reproduces the new per-repo matrix in `docs/local-testing.md` in one pass without any profile switch or reset. Run now on any single scenario repo (including `disabled-repo`) runs just that repo. Setting `LocalRepos__Profile` prints a deprecation warning and is otherwise ignored.
- [ ] `dotnet build AutoRemediator.sln`, `dotnet test AutoRemediator.sln` (with the known flaky API host timeouts noted if they occur), `npm run lint && npm run typecheck && npm run build` in `src/AutoRemediator.Web`, and `pwsh tools/local-scenarios/Test-LocalScenarioFingerprint.ps1` pass.
- [ ] The PRD marks §6.12 as built, describes layered settings in §6.1 and due-based scheduling in §6.11 and §5, records the decisions in §8, and strikes the enable/disable half of the configuration-UI known defect.

## Context
**Current state (read before starting):**
- `Domain/TargetingSettings.cs`: `TargetingSettings(patterns, excludes, feeds, policy)` and `UpdatePolicy(Strategy, Ignore, AllowPrerelease)`, with default policy Minor, no ignore and no pre-release. `Domain/ManagedRepository.cs` holds org/project/name/enabled/targetBranch only. `Domain` and `Contracts` reference only `Shared` and have **no NuGet packages**. Keep it that way, so the cron library lives in Infrastructure.
- Storage (`Infrastructure/Configuration`): the `repositories` table (partition `repo`, row = id) and the `config` table (`settings`/`global`, with JSON columns for the lists). Both are written with `TableUpdateMode.Replace`. The `runs` table is partitioned by repository id. New columns must be nullable so that old rows read as "inherit" or "unset".
- Settings consumers: `RemediationRunner` (line ~57), `ReviewCommandHandler` (Rebuild ~121, Retry ~169), and `DependencyMapService`. All of them call `ITargetingSettingsStore.GetAsync` and pass a `TargetingSettings` down to `IUpdatePlanner.PlanAsync`, `IRepositoryAnalyzer.AnalyzeAsync`, `IDependencyAligner`, `IVerificationService.OpenAsync` and `VerificationWorkspace.WriteNuGetConfigAsync`. Those downstream signatures stay as they are; callers pass `effective.Targeting`.
- `Worker.Scheduler/SchedulerWorker.cs`: `RunOnceAsync` enqueues every enabled repository and records `SkippedHeld` for held ones. It runs once and exits, unless `Scheduler:DevLoopEnabled` is set. The run record is created by `RemediationRunner` when the worker consumes the message (same `RunId`), so "manual" must travel **on the message**.
- Idempotency today means one `RunId` gives one run row (upsert), and the runner reuses the `autoremediator/dependency-updates` branch and the single active PR. There is no scheduler-side de-duplication.
- API: minimal API slices under `Api/Features/*` implementing `IFeatureEndpoint`. The repositories `PUT` rebuilds the entity from the input (a full replace). `SettingsEndpoints.FromDto` silently maps an unknown strategy to Minor.
- Web: `pages/configuration.tsx` (global settings form plus repo table plus add form), `lib/types.ts` (hand-mirrored DTOs), `lib/api.ts`, and routes in `main.tsx`. `DataTable` needs a `caption`, and `rowHref`/`primaryColumnId` make the primary cell a link. `focusRing` lives in `lib/utils.ts`. shadcn primitives in `components/ui` include `switch`, `select`, `sheet` and `alert`. Home has a read-only "Targeting" card showing the global settings.
- Functional tests (`tests/AutoRemediator.Web.FunctionalTests`): `AccessibilityTests.Routes()` (with a `{run}` placeholder resolved through `/api/runs`) and `FocusVisibleTests.Cases()`. Both are skipped unless `WEB_BASE_URL` is set.
- Terraform: `infra/terraform/environments/test` `scheduler_cron_expression` currently defaults to `0 */6 * * *` (also in `terraform.tfvars`) and is passed as `cron_expression` to the scheduler job in `main.tf`. The module sets `parallelism = 1` and `replica_completion_count = 1`, but overlapping executions are still possible if one runs longer than the interval.
- Local scenarios: `scenarios.psd1` (catalogue with per-profile `Outcomes`, written into each repo README), `Set-LocalScenarioConfig.ps1 -Profile` (upserts repos by name and PUTs `/api/settings`), the `LocalScenarioSetup` wrapper (`enroll --profile`, `Enrollment.ValidateProfile`), `AppHost.cs` (`LocalRepos:Profile`), and `tests/AutoRemediator.Tools.Tests/SetupTests.cs`. `scenarios.psd1` is part of the 003 fingerprint, so any edit to it triggers one ~85 s rebuild on the next start. That is intended.

**Design (confirmed by the user on 2026-10-14 unless marked otherwise; Benny to log):**
- **Layering model.**
  - Global = the existing `TargetingSettings` plus a new nullable `DefaultSchedule`.
  - Per repository = `RepositorySettings`:
    - `InheritGlobal` (bool, default true)
    - the lists `Patterns`, `Excludes`, `Feeds`, `Ignore` (empty = adds nothing)
    - `Strategy` (`UpdateStrategy?`), `AllowPrerelease` (`bool?`), `Schedule` (`string?`, cron)
- **Inheritance on (default).**
  - Each effective list is the global list followed by the repo's additions, with case-insensitive de-duplication that keeps the first spelling.
  - Each scalar is the repo value when it is non-null, otherwise the global value.
  - A repo cannot remove a global exclude or ignore entry without turning inheritance off.
- **Inheritance off ("local-only override").** This is **one switch for all targeting fields** (the simplest reading of "this repo does not inherit the global settings").
  - Lists are the repo's lists only.
  - An unset strategy or pre-release flag falls back to the built-in defaults (`UpdatePolicy.Default`: Minor, no pre-release), not to the global values.
  - **The schedule is not covered by the switch** (confirmed by the user). It is operational, not targeting, so an unset repo schedule still uses the global default schedule.
  - With no patterns, nothing matches and every run ends `NoUpdates`. The resolver emits a warning ("No package patterns: nothing will be updated"). The API accepts the configuration, and the UI shows the warning in the settings form and the effective view.
  - With patterns but no feeds, versions cannot be resolved (`Unknown`). That also produces a warning ("No feeds: versions cannot be resolved").
- **Sources.**
  - Lists, per item: `Global`, `Repository`, or `Both` (the same item in both).
  - Scalars: `Global`, `Repository`, or `Default` (the built-in value).
- **One resolver.** `EffectiveSettingsResolver.Resolve(TargetingSettings global, string? globalDefaultSchedule, RepositorySettings repo)` lives in Domain. It is pure and unit tested, and returns `EffectiveSettings { TargetingSettings Targeting; string Schedule; per-field sources; Warnings }`. `IEffectiveSettingsProvider.GetAsync(ManagedRepository)` in Infrastructure loads the global settings and calls it. It is the only reader of `ITargetingSettingsStore` besides the global settings endpoint.
- **Built-in default schedule: `0 0 * * *`** (daily at 00:00 UTC; user decision). It applies when neither the repository nor the global settings set a schedule.
  - **Deliberate cadence change for existing deployments.** Today the deployed job fires every 6 hours (`scheduler_cron_expression = "0 */6 * * *"`) and runs every enabled repository each time. After this feature the job ticks every 15 minutes, but a repository with no schedule of its own and no global default runs **once a day** at UTC midnight. Operators who want the old cadence set the global default schedule to `0 */6 * * *` (Configuration page or `PUT /api/settings`).
- **Terraform variable: rename, because its meaning changes.** `scheduler_cron_expression` meant "how often every repository runs"; after this feature the job's cron is only the tick, and cadence lives in the stored settings. Keeping the name would silently turn an operator's old cadence override (for example `0 */6 * * *`) into a 6-hourly tick, which makes per-repository schedules late by up to 6 hours. So:
  - Rename it to `scheduler_tick_cron_expression` (default `*/15 * * * *`) in `variables.tf`, `terraform.tfvars` and `main.tf`, and delete the old variable. Anyone still setting the old name gets Terraform's "undeclared variable" warning (tfvars) or error (`-var`), which surfaces the change instead of hiding it.
  - The description says it is the tick and should stay at `*/15 * * * *`, the minimum per-repository interval. A slower tick delays schedules; a faster one only adds empty ticks.
  - `infra/terraform/README.md` gets a short "Upgrading from the 6-hourly scheduler" note: drop `scheduler_cron_expression` from any tfvars; if it was set to change cadence, put that expression in the global default schedule (or per repository) instead; the default cadence is now daily at 00:00 UTC.
  - Terraform does not write the global default schedule. It is application data in the `config` table, like the rest of the targeting settings.
- **Cron: Cronos** (HangfireIO/Cronos, MIT, no dependencies, widely used by Hangfire). It handles DST-correct UTC/time-zone math, supports `L`, `W`, `#` and `@daily`-style macros, and has a strict parser.
  - The dialect is **standard 5-field** (`CronFormat.Standard`). Seconds are rejected because the tick is 15 minutes.
  - **Time zone is UTC**, the same as Container Apps Job cron and every stored timestamp, with no DST ambiguity. The UI labels "UTC" and also shows the next occurrence in the browser's local time.
  - Validation also rejects expressions that can fire more often than every 15 minutes (`CronSchedule.MinimumInterval`), checked over the next 100 occurrences. Finer schedules would silently behave as "every tick". **Assumed, revisit if wanted:** this was the planner's recommendation and was kept, but the user did not answer it explicitly. The alternative is to accept such expressions and document that they behave as "every tick".
  - The package goes into Infrastructure only, behind `CronSchedule` (parse, validate, next occurrence).
- **Due rule.**
  - Per repository, the scheduler keeps `LastScheduledAtUtc` in a **separate** `schedulestate` table (partition `schedule`, row = repository id). It is separate so the API's full-replace repository upsert never clobbers it, and so ETags can arbitrate.
  - A repository is due when `LastScheduledAtUtc` is null (never scheduled) or `cron.GetNextOccurrence(LastScheduledAtUtc) <= now`.
  - **Claim, then act:** insert the state (if absent) or update it with `If-Match`, setting `LastScheduledAtUtc = now`. A conflict (409/412) means another tick claimed the repository, so skip it.
  - Then publish, or record `SkippedHeld` if the repository is held. If publishing throws, make a best-effort revert to the previous value and log.
  - Setting the anchor to the tick time gives **at most one run per repository per tick** with no backfill storm after downtime. Disabled repositories are not evaluated, so on re-enable they run at the next tick if an occurrence passed while disabled, or immediately if never scheduled (one catch-up run).
  - Missed occurrences while held collapse into one `SkippedHeld` per due occurrence, not one per tick.
- **Never-scheduled = due now** (confirmed by the user). A newly enrolled repository, and every repository right after this feature is deployed, runs at the very next tick. This matches today's "every enabled repo runs on each fire". From then on it follows its effective schedule (daily at 00:00 UTC by default). In local mode it means the first scheduler pass after a start covers every scenario repo (Azurite is not persisted).
- **Run now does not touch schedule state.** The cron cadence is wall-clock anchored and independent of manual runs. A Run now on a disabled repository therefore leaves it never-scheduled (or at its old anchor), and enabling it later still triggers a run at the next tick.
- **Run now rules** (user decision on disabled repositories).
  - Responses: 404 unknown repository; 409 `{ reason: "held", heldRunId, message }` while held; otherwise 202 `{ runId, enabled }` (`enabled` lets the UI warn about a disabled repository without a second request). **Disabled repositories are allowed**: "disabled" means "not scheduled", not "off", so an operator can try a repository before enabling it. The worker never checked `Enabled`, so nothing downstream changes.
  - The held check uses `FindAwaitingReviewAsync`, the same as the scheduler. It is advisory, like review commands. A manual run does **not** record `SkippedHeld`; the refusal is the signal.
  - There is no in-flight de-duplication, because the scheduler has none: a second click gives a second run that reuses the branch and PR. The UI disables the button while a request is pending.
- **Trigger.**
  - `Contracts.Messages.RemediationTrigger { Scheduled, Manual }` (with a string enum converter, like `ReviewCommand`) is appended to `RemediationRunRequested` with default `Scheduled`, so older messages deserialize as scheduled.
  - `Domain.RunTrigger { Scheduled, Manual }` is a constructor parameter (default `Scheduled`) and a property on `RemediationRun`, stored in a nullable `Trigger` column (null = `Scheduled`).
  - `Trigger` is appended last to `RunSummaryDto` and `RunDetailDto` as a string.
  - `SkippedHeld` bookkeeping runs are `Scheduled`.
- **Shared requester.** `IRemediationRunRequester` (Infrastructure/Remediation) builds and publishes the message and does the held check. It does not check `Enabled`; the scheduler filters to enabled repositories itself. Both the scheduler and the API use it, so the message shape cannot drift.
- **API contract (append-only, new optional fields last):**
  - `ManagedRepositoryDto` gains `RepositorySettingsDto? Settings = null`.
  - `ManagedRepositoryInput` gains `RepositorySettingsDto? Settings = null`. On `PUT`, null keeps the stored settings (so existing callers such as older enrollment scripts do not wipe them). On `POST`, null means inherit everything.
  - `TargetingSettingsDto` gains `string? DefaultSchedule = null`.
  - New records: `RepositorySettingsDto(bool InheritGlobal, IReadOnlyList<string> Patterns, Excludes, Feeds, Ignore, string? Strategy, bool? AllowPrerelease, string? Schedule)`, `EffectiveSettingsDto`, `EffectiveListItemDto(string Value, string Source)` and `EffectiveValueDto(string Value, string Source)`.
  - Global `PUT /api/settings` now validates the strategy and the default schedule. It returns 400 where it used to coerce silently to Minor (an intended contract tightening).
- **Storage additions (all nullable).**
  - `repositories` gains `InheritGlobal bool?` (null = true), `PatternsJson`, `ExcludesJson`, `FeedsJson`, `IgnoreJson` (`string?`, null = empty), `Strategy string?`, `AllowPrerelease bool?` and `Schedule string?`.
  - `config/settings/global` gains `DefaultSchedule string?`.
  - `runs` gains `Trigger string?`.
  - The new `schedulestate` table holds `LastScheduledAtUtc DateTimeOffset?`.
  - No data migration is needed.
- **UI shape.**
  - Configuration keeps the global settings card (renamed "Global settings", plus a default-schedule field) and the repository table. The table gets columns for name (link to the detail page), branch, an Enabled switch, a Settings badge (`Inherits` / `Overrides` / `Local only`), Schedule (the value, or "default"), Run now, and Delete.
  - The new route is `/configuration/repositories/:id`. It has an edit form for the repository fields (Organization labelled "informational"), a settings form, an effective settings table (caption "Effective settings", columns Setting / Value / Source), and Run now.
  - Tri-state scalars use a `Select` with an "Inherit (global: X)" option.
  - Run now results show in the page's `role="status"` alert. The 409 for a held repository links to the held run.
  - Run now is offered on disabled repositories. Next to it (table row and detail page) the UI shows visible text "Disabled: not scheduled", and the queued message for a disabled repository adds "This repository is disabled and will not run on schedule."
- **Local scenarios.**
  - The global baseline is patterns `Contoso.*`, `Fabrikam.*`, `Northwind.*`, `Tailspin.*` and the local feed, with no excludes, no ignore, Minor, no pre-release, and no default schedule.
  - `disabled-repo` gets no scheduled run, but Run now runs it (Contoso.Logging 1.2.0 → 1.2.3 under the global Minor, `Manual`).
  - Each repo's `Settings` in `scenarios.psd1`:
    - `patch-bump`: Patch, schedule `0 3 * * *`
    - `major-compiles`: Major
    - `major-breaking`: Major
    - `prerelease`: AllowPrerelease
    - `ignored-package`: Ignore `Tailspin.Legacy.*`
    - new `local-only`: inheritance off, patterns `AdventureWorks.*`, feed = the local feed (the token `{feed}` is substituted at enrollment), with `AdventureWorks.Text` 1.0.0 → 1.5.0
    - every other repo inherits
  - `Outcomes` becomes a single `Expected` string.
  - The AppHost forwards `Scheduler__DevLoopEnabled` and `Scheduler__DevLoopIntervalSeconds` to the scheduler when they are set, so the local scheduler can act as a real tick for due/not-due checks.
  - `LocalRepos__Profile` is deprecated: the AppHost prints `LocalRepos__Profile is obsolete and ignored: each scenario repo carries its own settings (see docs/local-testing.md).` and no longer passes `--profile`. `Set-LocalScenarioConfig.ps1 -Profile` is removed. `-Only` stays.

**Relevant decisions (decisions.md):**
- 001: every table has a `caption`, every interactive element uses `focusRing`, axe runs through `Deque.AxeCore.Playwright`, and focus is checked by `FocusVisibleTests`.
- 002: "Scenarios run under global settings profiles; per-repo settings are out of scope." **This feature supersedes that.** Missing branch / unknown repo ends `NoUpdates` (known defect, unchanged).
- 003: the fingerprint covers `scenarios.psd1`; the scheduler is opt-in in local mode (`LocalRepos__RunOnStart`); enrollment runs as `local-scenarios-enroll`; and the default profile was `baseline` (**superseded**: profiles are removed).

## Tasks
1. [todo] 01 Run now and run trigger (backend)
2. [todo] 02 Run now, enable switch and Manual badge (UI)
3. [todo] 03 Repository settings model, effective-settings resolver and cron helper
4. [todo] 04 Settings storage and effective-settings provider
5. [todo] 05 Pipeline uses effective settings (runner, review, dependency map)
6. [todo] 06 API for repository settings, effective settings and default schedule
7. [todo] 07 Repository detail page and settings UI
8. [todo] 08 Due-based scheduler with per-repository schedule state
9. [todo] 09 Scheduler tick deployment and schedule visibility
10. [todo] 10 Local scenarios with per-repo settings
11. [todo] 11 Documentation and PRD
12. [todo] 12 End-to-end verification in local mode

## Risks and open questions
No open questions. The user resolved the earlier ones on 2026-10-14:
- **Built-in default schedule:** `0 0 * * *` (daily, UTC midnight), not every 6 hours. See the cadence-change risk below.
- **Run now on a disabled repository:** allowed (202). Still 409 while held.
- **Never-scheduled = due now:** confirmed. A newly enrolled repository runs at the very next tick.
- **Inheritance switch scope:** confirmed. The switch leaves the schedule alone; an unset schedule uses the global default.
- **Minimum schedule interval (assumed, revisit if wanted):** schedules firing more often than every 15 minutes stay rejected. This is the planner's recommendation, not an explicit user answer. Changing it later only means dropping the interval check in `CronSchedule.TryParse` and documenting "every tick" behaviour.

Risks:
- **Cadence change for existing deployments (deliberate).** Deployed repositories without their own schedule go from every 6 hours to once a day at 00:00 UTC. Right after the deploy every enabled repository runs at the first tick (never-scheduled), then daily. Operators who want the old cadence set the global default schedule to `0 */6 * * *`. Task 09 (Terraform README upgrade note, root README worker table) and task 11 (PRD §6.11 and §8) state this.
- **Terraform variable rename.** `scheduler_cron_expression` becomes `scheduler_tick_cron_expression`. Any environment that set the old name in tfvars sees an undeclared-variable warning until it is removed; anyone passing it with `-var` gets an error. This is intended, so the meaning change is not silent.
- **Run now on disabled repositories.** An operator could mistake a successful manual run for "the repository is live". The UI shows "Disabled: not scheduled" next to the button and in the queued message to prevent that.
- **Time zones.** Schedules are UTC only. `0 3 * * *` is 03:00 UTC, which is 04:00 or 05:00 in central Europe depending on DST. The UI shows the next run in both UTC and browser time to make this visible. Per-repo time zones are deferred (Cronos supports them, so it is a cheap later addition).
- **Azure DevOps load.** Per-repo cron can concentrate runs. For example, ten repos on `0 9 * * 1` all fire at the same tick. Runs still process sequentially per worker, and 429/`Retry-After` is honoured (PRD §8), so this is a latency risk, not a correctness one. The 15-minute floor caps any one repo at 96 runs a day.
- **Review gate interaction.** A held repository is skipped only when due, so `SkippedHeld` rows appear at the repository's cadence, not every 15 minutes. Run now refuses while held (but not while disabled). Rebuild/Retry use the **current** effective settings, not the ones in force when the run was planned. If the settings changed in between (for example a feed removed), a retry can plan differently. This is accepted and documented.
- **Daily token cap.** Not built. More frequent schedules can raise AI usage, but the per-run budget still bounds each run. No change here.
- **Concurrency.** Overlapping ticks (a slow tick plus the next fire) are arbitrated by ETag claims on `schedulestate`. Publish-then-crash after a claim loses that occurrence until the next one; claim-then-publish-failure is reverted on a best-effort basis. Both are logged.
- **Contract tightening.** `PUT /api/settings` with an unknown strategy now returns 400 instead of silently becoming Minor. The only known caller is `Set-LocalScenarioConfig.ps1`, which sends valid values.
- **Global settings UI migration.** The global form keeps its fields and gains a default schedule. Existing stored settings load with no default schedule, so the built-in `0 0 * * *` applies, and the field shows that value as a placeholder.
- **Local mode cadence change.** Restarting the `scheduler` resource no longer re-runs every repository: only the first pass after `aspire start` does (everything is never-scheduled because Azurite is not persisted), and later passes enqueue only due repositories (with the daily default, usually none until after 00:00 UTC). Re-running a single repo is Run now. The docs must say so. The `held-review` `SkippedHeld` demo therefore needs the seeder to run **before** the first scheduler pass. Tasks 10 and 11 document seeding first, and Run now's 409 is the always-available signal.
- **Rebuild on first start.** Editing `scenarios.psd1` changes the 003 fingerprint, so the first start after task 10 rebuilds `.local` (~85 s). This is intended.
- **Flaky API tests.** `AutoRemediator.Api.Tests` host-build timeouts are a known defect. New API tests may hit them, so reviewers should rerun before treating a timeout as a failure.
