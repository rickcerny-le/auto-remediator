using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Remediation;
using AutoRemediator.Infrastructure.Scheduling;

namespace AutoRemediator.Worker.Scheduler;

/// <summary>
/// A scheduler tick: enqueues a <see cref="RemediationRunRequested"/> for each enabled
/// repository whose effective cron schedule is due, at most one per repository per tick.
/// Deployed as a frequent (every 15 minutes) cron Azure Container Apps Job: it runs one tick and
/// stops the host. A dev-only loop (Scheduler:DevLoopEnabled) repeats the tick locally, with
/// Scheduler:DevLoopIntervalSeconds as the tick interval.
///
/// Due rule: a repository is due when it has never been scheduled, or when the next cron
/// occurrence after its <c>LastScheduledAtUtc</c> is at or before the tick time. The anchor
/// recorded on a claim is the tick time, not the cron occurrence, so after downtime a repository
/// that missed many occurrences runs once and is then not due again until its next occurrence
/// after now (no backfill storm). The claim is made before publishing, through an ETag-guarded
/// write, so overlapping ticks cannot both run a repository; a crash between claim and publish
/// loses that one occurrence, and a publish failure reverts the claim best-effort. Disabled
/// repositories are never evaluated; Run now does not read or write schedule state.
/// </summary>
public sealed class SchedulerWorker(
    IManagedRepositoryStore repositories,
    IRemediationRunStore runStore,
    IRemediationRunRequester requester,
    IEffectiveSettingsProvider settingsProvider,
    IScheduleStateStore scheduleState,
    IHostApplicationLifetime lifetime,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<SchedulerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var devLoop = configuration.GetValue("Scheduler:DevLoopEnabled", false);
        var interval = TimeSpan.FromSeconds(configuration.GetValue("Scheduler:DevLoopIntervalSeconds", 60));

        do
        {
            await RunOnceAsync(stoppingToken);

            if (!devLoop)
            {
                break;
            }

            try
            {
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        while (!stoppingToken.IsCancellationRequested);

        if (!devLoop)
        {
            // Production shape: the scheduled Job has done its work — exit.
            lifetime.StopApplication();
        }
    }

    /// <summary>Runs one tick and returns the number of runs enqueued. Testable in isolation.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        // One instant for the whole tick, so every repository is evaluated against the same time.
        var now = timeProvider.GetUtcNow();
        var enabled = (await repositories.ListAsync(cancellationToken)).Where(r => r.Enabled).ToList();
        var effective = await settingsProvider.GetAsync(enabled, cancellationToken);

        int enqueued = 0, skippedHeld = 0, notDue = 0, claimedElsewhere = 0;

        foreach (var repo in enabled)
        {
            var state = await scheduleState.GetAsync(repo.Id, cancellationToken);
            var schedule = effective[repo.Id].Schedule.Value;

            switch (DueEvaluator.Evaluate(schedule, state?.LastScheduledAtUtc, now))
            {
                case DueResult.InvalidCron:
                    logger.LogWarning(
                        "Not scheduling {Slug}: stored cron expression '{Cron}' is invalid.", repo.Slug, schedule);
                    notDue++;
                    continue;
                case DueResult.NotDue:
                    logger.LogDebug("{Slug} is not due (schedule '{Cron}').", repo.Slug, schedule);
                    notDue++;
                    continue;
            }

            if (!await scheduleState.TryClaimAsync(repo.Id, state, now, cancellationToken))
            {
                logger.LogInformation("{Slug} was claimed by another tick; skipping.", repo.Slug);
                claimedElsewhere++;
                continue;
            }

            RunRequestResult result;
            try
            {
                result = await requester.RequestAsync(repo, RemediationTrigger.Scheduled, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to enqueue a run for {Slug}; reverting its schedule state.", repo.Slug);
                await TryRevertAsync(repo, state?.LastScheduledAtUtc);
                continue;
            }

            switch (result)
            {
                case RunRequestResult.Held held:
                    // A repository with an open proposal receives no new updates until it is resolved
                    // (FR-024). The skip is recorded (only when due) so a held repository is visible as
                    // held rather than merely quiet (FR-025).
                    var skip = new RemediationRun(Guid.NewGuid(), repo.Id, repo.Slug, now);
                    skip.SkippedHeld(now);
                    await runStore.SaveAsync(skip, cancellationToken);
                    skippedHeld++;
                    logger.LogInformation(
                        "Skipped {Slug}: held for review by run {HeldRunId}.", repo.Slug, held.HeldRunId);
                    break;

                case RunRequestResult.Enqueued queued:
                    enqueued++;
                    logger.LogInformation("Enqueued remediation run {RunId} for {Slug}", queued.RunId, repo.Slug);
                    break;
            }
        }

        logger.LogInformation(
            "Scheduler tick complete: enqueued {Enqueued}, skipped held {SkippedHeld}, not due {NotDue}, claimed elsewhere {ClaimedElsewhere}.",
            enqueued, skippedHeld, notDue, claimedElsewhere);
        return enqueued;
    }

    private async Task TryRevertAsync(ManagedRepository repo, DateTimeOffset? previous)
    {
        try
        {
            await scheduleState.RevertAsync(repo.Id, previous, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not revert schedule state for {Slug}.", repo.Slug);
        }
    }
}
