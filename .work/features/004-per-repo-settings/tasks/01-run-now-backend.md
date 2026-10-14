# 01 Run now and run trigger (backend)

**Status:** todo
**Feature:** 004-per-repo-settings
**Depends on:** none

## What to do
Add an on-demand run for one repository (PRD §6.12), which the API triggers through the same enqueue path the scheduler uses, and record on every run whether it was scheduled or manual.

1. **Trigger on the message.** In `Contracts/Messages/RemediationRunRequested.cs`:
   - Add `RemediationTrigger { Scheduled, Manual }`, with `[JsonConverter(typeof(JsonStringEnumConverter<RemediationTrigger>))]` as on `ReviewCommand`.
   - Append `RemediationTrigger Trigger = RemediationTrigger.Scheduled` as the **last** positional parameter.
   - Check that a JSON body without `Trigger` deserializes as `Scheduled`, because messages already queued in Service Bus have no trigger.
2. **Trigger on the run.** In `Domain/RemediationRun.cs`:
   - Add `RunTrigger { Scheduled, Manual }`.
   - Add an optional constructor parameter `RunTrigger trigger = RunTrigger.Scheduled` and a `Trigger` property.
   - Add a `trigger` parameter (default `Scheduled`) at the end of `Restore`.
   - In `RemediationRunStore.cs`, add a nullable `Trigger` string column (null or unparseable reads as `Scheduled`).
   - `RemediationRunner.RunAsync` maps `request.Trigger` to the run's trigger when it creates the run.
3. **Shared requester.** Add `Infrastructure/Remediation/RemediationRunRequester.cs`:
   - Interface `IRemediationRunRequester`, with:
     - `Task<RunRequestResult> RequestAsync(ManagedRepository repo, RemediationTrigger trigger, CancellationToken ct)`
     - `RunRequestResult`: `Enqueued(Guid RunId)` | `Held(Guid HeldRunId)`
   - It does **not** check `Enabled`. Manual runs are allowed on disabled repositories (user decision), and the scheduler filters to enabled repositories before calling it. Say so in the XML doc.
   - It checks `IRemediationRunStore.FindAwaitingReviewAsync`, then builds the `RemediationRunRequested` (new `RunId`, org/project/name, `TimeProvider` now, trigger) and publishes it to `RemediationQueues.RemediationRuns`.
   - Register it as a singleton in `InfrastructureExtensions`.
   - It does **not** record `SkippedHeld`. That remains the scheduler's bookkeeping.
4. **Scheduler uses it.** `SchedulerWorker.RunOnceAsync` keeps its existing enabled-only filter and calls the requester with `Scheduled`. On `Held`, it records the `SkippedHeld` run exactly as today. Behaviour and logs are otherwise unchanged; the cron logic is task 08.
5. **API endpoint.** In `Api/Features/Repositories/RepositoriesEndpoints.cs`, add `POST /api/repositories/{id:guid}/run`:
   - 404 when the repository is unknown.
   - 409 `{ reason = "held", heldRunId, message }` when held.
   - 202 `{ runId, enabled }` on success, **including for a disabled repository**. `enabled` echoes the repository's flag so the UI can add "will not run on schedule" without a second request.
6. **DTOs.** Append `string Trigger = "Scheduled"` as the last parameter of `RunSummaryDto` and of `RunDetailDto` (after `PullRequestDiffUrl`). Map them in `RunsEndpoints.ToSummary` and `ToDetail`.

## Files
- `src/AutoRemediator.Contracts/Messages/RemediationRunRequested.cs`: `RemediationTrigger`, appended `Trigger`
- `src/AutoRemediator.Contracts/Dtos/RunDtos.cs`: appended `Trigger` on both run DTOs
- `src/AutoRemediator.Domain/RemediationRun.cs`: `RunTrigger`, ctor/`Restore`/property
- `src/AutoRemediator.Infrastructure/Configuration/RemediationRunStore.cs`: `Trigger` column
- `src/AutoRemediator.Infrastructure/Remediation/RemediationRunRequester.cs` (new), `RemediationRunner.cs`
- `src/AutoRemediator.Infrastructure/InfrastructureExtensions.cs`: registration
- `src/AutoRemediator.Worker.Scheduler/SchedulerWorker.cs`: uses the requester (new constructor dependency)
- `src/AutoRemediator.Api/Features/Repositories/RepositoriesEndpoints.cs`, `Features/Runs/RunsEndpoints.cs`
- Tests: `tests/AutoRemediator.Domain.Tests/RemediationRunTests.cs`, `tests/AutoRemediator.Infrastructure.Tests/RemediationRunStoreRoundTripTests.cs`, `RemediationRunnerTests.cs`, a new `RemediationRunRequesterTests.cs`, `tests/AutoRemediator.Workers.Tests/SchedulerWorkerTests.cs`, `tests/AutoRemediator.Api.Tests/RepositoriesEndpointTests.cs`, `RunsEndpointTests.cs`

## Out of scope
- UI (task 02).
- Due-based scheduling (task 08). The scheduler still enqueues every enabled, non-held repository.
- Recording the acting user (no authentication yet).
- A `Queued` status, and in-flight de-duplication.

## Done when
- [ ] `POST /api/repositories/{id}/run` returns 202 with a `runId` and publishes exactly one `RemediationRunRequested` with `Trigger = Manual` and that `RunId`. It returns 404 for an unknown id, and 409 with `reason` `held` (plus `heldRunId`), publishing nothing. Covered by API tests using a recording publisher.
- [ ] On a **disabled** repository, `POST /api/repositories/{id}/run` returns 202 with `enabled: false` and publishes one `Manual` message; the stored repository stays disabled (API test). A disabled repository that is also held still returns 409 `held`.
- [ ] `RemediationRunRequesterTests`: a disabled repository is enqueued (no `Enabled` check); a held repository returns `Held` and publishes nothing.
- [ ] `SchedulerWorkerTests` still show that a disabled repository is never enqueued by the scheduler.
- [ ] A `RemediationRunRequested` JSON without a `Trigger` property deserializes with `Trigger = Scheduled` (unit test).
- [ ] `RemediationRunner` creates the run with the message's trigger. The run store round-trips `Manual`, and a legacy entity with no `Trigger` column reads back as `Scheduled` (tests).
- [ ] `GET /api/runs` and `GET /api/runs/{id}` include `trigger`.
- [ ] All existing `SchedulerWorkerTests` pass unchanged in intent (adapted only for the new constructor dependency). Scheduled messages carry `Trigger = Scheduled`.
- [ ] `dotnet build AutoRemediator.sln` and `dotnet test AutoRemediator.sln` pass.

## Notes
- The worker creates the run row on consumption, so the trigger must travel on the message. Do not try to pre-create the run in the API.
- The held check in the API is advisory, like the review endpoint's (`RunsEndpoints`, "Decision 7"). The worker does not re-check held state for runs today. Keep that as it is and note it in the requester's XML doc.
- `RemediationRunner` loads the repository by id and does not check `Enabled`, so a manual run on a disabled repository runs end to end with no worker change. Do not add an `Enabled` check there.
- `HeldRunSeeder` constructs `RemediationRun` directly. The new constructor parameter is optional, so it compiles unchanged.
