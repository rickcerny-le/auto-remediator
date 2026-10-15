# 09 Scheduler tick deployment and schedule visibility

**Status:** done
**Feature:** 004-per-repo-settings
**Depends on:** 07, 08

## What to do
Make the deployed scheduler a frequent tick, and show each repository's last and next scheduled runs in the API and the UI.

1. **Terraform.** The job's cron changes meaning: it was the cadence for every repository (`0 */6 * * *`), and becomes only the tick. The effective per-repository default cadence becomes **daily at 00:00 UTC** (`0 0 * * *`, the built-in default from task 03). Existing deployments therefore change from 6-hourly to daily runs. That is a deliberate consequence of the user's decision, not a regression. In `infra/terraform/environments/test`:
   - **Rename** the variable `scheduler_cron_expression` to `scheduler_tick_cron_expression` in `variables.tf`, `terraform.tfvars` and `main.tf` (`cron_expression = var.scheduler_tick_cron_expression`). Delete the old variable rather than keeping an alias, so a stale override shows up as Terraform's undeclared-variable warning or error instead of silently becoming a slow tick.
   - Default it to `*/15 * * * *` in both `variables.tf` and `terraform.tfvars`.
   - Description: "Scheduler tick (UTC). Each tick enqueues only repositories whose own schedule is due. Keep it at */15 * * * *, the minimum per-repository interval: a slower tick delays schedules. This is not the run cadence; set that with the global default schedule or per repository."
   - The module stays a `Schedule`-trigger, run-once job with `parallelism = 1`. The module's own `cron_expression` variable keeps its name (it is generic).
   - Update `infra/terraform/README.md`:
     - The jobs table row: "tick (`scheduler_tick_cron_expression`, every 15 minutes), enqueues only due repositories".
     - A short "Upgrading from the 6-hourly scheduler" subsection: (1) remove `scheduler_cron_expression` from any tfvars or `-var` usage; (2) if it was set to change how often repositories run, put that expression in the global default schedule (Configuration page or `PUT /api/settings`) or on individual repositories instead; (3) without that, repositories run once a day at 00:00 UTC, and every enabled repository runs once at the first tick after the deploy because none has been scheduled yet.
     - Any other cadence text.
   - Do not run `terraform apply`. Run `terraform fmt -check` and `terraform validate` in the environment folder if Terraform is installed, and otherwise say so in the review notes.
2. **API.** Append to `EffectiveSettingsDto` (last, optional):
   - `DateTimeOffset? LastScheduledAtUtc = null`
   - `IReadOnlyList<DateTimeOffset>? NextScheduledRunsUtc = null`: the next 3 occurrences after `max(now, last)`. When the repository has never been scheduled, the first entry is "now" (the next tick will run it); render that as "next tick".
   - `bool? DueAtNextTick = null`
   - Compute them in the effective-settings endpoint from `IScheduleStateStore`, `CronSchedule` and `DueEvaluator`. Disabled repositories return null for all three.
3. **UI** (`pages/repository.tsx`): in the effective settings card, under the Schedule row, show:
   - "Last scheduled" (UTC, plus local time in a `<time>` element with `dateTime`)
   - "Next runs" (the 3 entries, UTC and local)
   - "Disabled: not scheduled (Run now still works)" when disabled
   - `data-testid="schedule-next"`
   - Use `lib/format.ts` helpers for dates.
4. **README** (root): update the worker execution models table. The scheduler row should say it ticks every 15 minutes (Container Apps Job cron) and enqueues only due repositories (default cadence daily at 00:00 UTC), at most one run per repository per tick, with schedule state in the `schedulestate` table.

## Files
- `infra/terraform/environments/test/variables.tf`, `terraform.tfvars`, `main.tf` (variable rename), `infra/terraform/README.md`
- `src/AutoRemediator.Contracts/Dtos/ConfigurationDtos.cs`
- `src/AutoRemediator.Api/Features/Repositories/RepositoriesEndpoints.cs`
- `src/AutoRemediator.Web/src/lib/types.ts`, `pages/repository.tsx`
- `README.md`
- `tests/AutoRemediator.Api.Tests/RepositorySettingsEndpointTests.cs`

## Out of scope
- Any other Terraform change (deployment remains deferred per PRD §8).
- Showing the schedule on the Configuration table beyond the expression column from task 07.

## Done when
- [ ] `scheduler_cron_expression` no longer appears anywhere under `infra/` except the README's upgrade note. `scheduler_tick_cron_expression` defaults to `*/15 * * * *` in `variables.tf` and `terraform.tfvars` and feeds the scheduler job's `cron_expression` in `main.tf`. `terraform validate` passes, or its absence is noted.
- [ ] `infra/terraform/README.md` has the upgrade note: the rename, where cadence now lives, and that the default cadence changes from every 6 hours to daily at 00:00 UTC.
- [ ] API tests: a never-scheduled repository returns `lastScheduledAtUtc` null, `dueAtNextTick` true and 3 next runs. A repository scheduled 1 minute ago with `0 3 * * *` returns `dueAtNextTick` false and a first next run at the coming 03:00 UTC. A disabled repository returns null for all three.
- [ ] With a running app, the repository page shows the last and next runs, and `AccessibilityTests` and `FocusVisibleTests` for the repository route still pass.
- [ ] `npm run lint && npm run typecheck && npm run build`, `dotnet build AutoRemediator.sln` and `dotnet test AutoRemediator.sln` pass.

## Notes
- The API needs `IScheduleStateStore` registered in the API host too. `AddInfrastructure` is shared, so it already is once task 08 registers it.
- Use the request-time `TimeProvider` (registered by `AddInfrastructure`) so the tests can pin "now".
