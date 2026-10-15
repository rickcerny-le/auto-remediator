# 08 Due-based scheduler with per-repository schedule state

**Status:** done
**Feature:** 004-per-repo-settings
**Depends on:** 01, 05

## What to do
Turn the scheduler from "enqueue every enabled repository on each fire" into a tick that enqueues only the repositories whose effective cron schedule is due. It enqueues at most one run per repository per tick and never runs a backfill storm.

1. **Schedule state store.** Add `Infrastructure/Scheduling/ScheduleStateStore.cs` (new):
   - Table `schedulestate`, partition `schedule`, row = repository id. Columns: `LastScheduledAtUtc` (`DateTimeOffset?`) and the standard ETag.
   - Interface `IScheduleStateStore`, with:
     - `Task<ScheduleState?> GetAsync(Guid repositoryId, ct)`, where `ScheduleState(Guid RepositoryId, DateTimeOffset? LastScheduledAtUtc, string? ETag)`
     - `Task<bool> TryClaimAsync(ScheduleState? current, DateTimeOffset now, ct)`:
       - when `current` is null, it does `AddEntityAsync` and returns false on 409
       - otherwise it does `UpdateEntityAsync` with `If-Match` = ETag and returns false on 412
     - `Task RevertAsync(Guid repositoryId, DateTimeOffset? previous, ct)`: best effort, unconditional
   - Register it as a singleton. It is a separate table on purpose, so the API's full-replace repository upsert can never clobber scheduler bookkeeping.
2. **Due rule.** Add `Infrastructure/Scheduling/DueEvaluator.cs`, a pure static `IsDue(string cron, DateTimeOffset? lastScheduledAtUtc, DateTimeOffset now)`:
   - It is due when `last` is null, or when `CronSchedule.NextAfter(last) <= now`.
   - An unparseable stored cron is treated as **not due**, logged by the caller as a warning naming the repository. The API prevents it, but stored data can predate validation.
3. **`SchedulerWorker.RunOnceAsync`.** For each **enabled** repository (resolve effective settings in one batch through `IEffectiveSettingsProvider`):
   1. Load its state and evaluate `IsDue(effective.Schedule.Value, state?.LastScheduledAtUtc, now)`. If it is not due, log at Debug and continue.
   2. If `TryClaimAsync` returns false, log at Information ("claimed by another tick") and continue.
   3. Call `requester.RequestAsync(repo, Scheduled)`.
      - `Enqueued`: count it.
      - `Held`: record `SkippedHeld` as today.
      - An exception from publishing: call `RevertAsync(previous)`, log at Error, and continue with the next repository.
   - `now` is read once per tick from `TimeProvider`, so every repository is evaluated against the same instant.
   - The final log line reports the counts: enqueued, skipped held, not due, and claimed elsewhere.
4. Keep the `Scheduler:DevLoopEnabled` loop. Each loop iteration is a tick, so locally `DevLoopIntervalSeconds` acts as the tick interval.
5. **Tests** (`tests/AutoRemediator.Workers.Tests/SchedulerWorkerTests.cs` and a new `tests/AutoRemediator.Infrastructure.Tests/DueEvaluatorTests.cs`). Use `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`; add it if it is not already referenced) and in-memory fakes for the state store and the provider.

## Files
- `src/AutoRemediator.Infrastructure/Scheduling/ScheduleStateStore.cs` (new), `DueEvaluator.cs` (new)
- `src/AutoRemediator.Infrastructure/InfrastructureExtensions.cs`
- `src/AutoRemediator.Worker.Scheduler/SchedulerWorker.cs`
- `tests/AutoRemediator.Workers.Tests/SchedulerWorkerTests.cs`, `Fakes.cs`
- `tests/AutoRemediator.Infrastructure.Tests/DueEvaluatorTests.cs` (new), a new `ScheduleStateStoreTests.cs` (Azurite-backed, self-skipping)

## Out of scope
- Terraform, README and showing schedule times in the API/UI (task 09).
- Changing Run now. It does not read or write schedule state.
- Per-repository time zones.

## Done when
- [ ] `DueEvaluatorTests`:
  - Never scheduled is due.
  - `0 3 * * *` with last = yesterday 03:00:10 is due at 03:00:30 today and not due at 02:59 today.
  - The built-in default `0 0 * * *` with last = yesterday 00:00:10 is not due at 23:59 and is due at 00:00:30 today.
  - The same expression with last = 3 days ago is due, and the run then sets last = now, so the next tick is not due (one run, no backfill).
  - An invalid cron is not due.
- [ ] `SchedulerWorkerTests` (adapted plus new):
  - A due repository is enqueued and its state is set to now.
  - A not-due repository is not enqueued.
  - Disabled repositories are never evaluated and their state is untouched.
  - A due held repository records exactly one `SkippedHeld` and advances its state; a second tick at the same time records nothing more.
  - Two repositories with different schedules are enqueued at their own times across a simulated day. Both start with state `LastScheduledAtUtc` = day 00:00:00 UTC, and the ticks run at 00:15, 00:30, ... up to the next day's 00:00 (96 ticks). A `0 3 * * *` repository gets exactly 1 message, and a `0 */6 * * *` repository gets exactly 4. (A third repository with no schedule gets exactly 1, at the next day's 00:00 tick.)
  - A lost claim (the fake returns false) publishes nothing.
  - A publish exception reverts the state and the tick continues with the next repository.
  - A repository with no schedule uses the global default, and with none, the built-in `0 0 * * *` (daily at 00:00 UTC): with last = yesterday 00:00:05 it is not due at 23:45 and is due at the 00:00 tick.
  - A never-scheduled repository is enqueued at the very first tick, whatever its schedule.
  - A disabled repository that had a manual run (Run now) still has no schedule state and is not enqueued while disabled.
- [ ] `ScheduleStateStoreTests` (Azurite): the first claim inserts; a second claim with a stale ETag returns false; a claim with a fresh ETag succeeds.
- [ ] `dotnet build AutoRemediator.sln` and `dotnet test AutoRemediator.sln` pass.

## Notes
- The anchor is the **tick time**, not the cron occurrence. That is what guarantees one run per repository per tick after downtime. Explain it in the worker's XML doc, which currently says it enqueues "one per repository".
- The requester does not check `Enabled` (manual runs are allowed on disabled repositories), so the enabled-only filter in `RunOnceAsync` is what keeps disabled repositories off the schedule. Do not drop it.
- Order matters: claim, then act. A crash after the claim and before publishing loses that occurrence (the next one runs normally), which is preferable to double runs. A publish failure reverts on a best-effort basis.
- Azure Tables: `AddEntityAsync` throws `RequestFailedException` with Status 409 when the entity exists. `UpdateEntityAsync(entity, ifMatch: etag, TableUpdateMode.Replace)` throws 412 on an ETag mismatch.
