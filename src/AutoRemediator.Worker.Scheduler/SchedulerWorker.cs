using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Remediation;

namespace AutoRemediator.Worker.Scheduler;

/// <summary>
/// Enumerates enrolled repositories and enqueues one <see cref="RemediationRunRequested"/>
/// per repository. Deployed as a scheduled (cron) Azure Container Apps Job: it runs its
/// work once and stops the host. A dev-only loop (Scheduler:DevLoopEnabled) can re-trigger
/// the run locally without changing the production run-once shape.
/// </summary>
public sealed class SchedulerWorker(
    IManagedRepositoryStore repositories,
    IRemediationRunStore runStore,
    IRemediationRunRequester requester,
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

    /// <summary>Publishes one remediation-run message per enabled repository. Testable in isolation.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var repos = await repositories.ListAsync(cancellationToken);
        var published = 0;

        foreach (var repo in repos.Where(r => r.Enabled))
        {
            // A repository with an open proposal receives no new updates until it is resolved
            // (FR-024). The skip is recorded rather than silently doing nothing, so a held
            // repository is visible as held rather than merely quiet (FR-025).
            var result = await requester.RequestAsync(repo, RemediationTrigger.Scheduled, cancellationToken);
            switch (result)
            {
                case RunRequestResult.Held held:
                    var skip = new RemediationRun(Guid.NewGuid(), repo.Id, repo.Slug, timeProvider.GetUtcNow());
                    skip.SkippedHeld(timeProvider.GetUtcNow());
                    await runStore.SaveAsync(skip, cancellationToken);
                    logger.LogInformation(
                        "Skipped {Slug}: held for review by run {HeldRunId}.", repo.Slug, held.HeldRunId);
                    break;

                case RunRequestResult.Enqueued enqueued:
                    published++;
                    logger.LogInformation("Enqueued remediation run {RunId} for {Slug}", enqueued.RunId, repo.Slug);
                    break;
            }
        }

        logger.LogInformation("Scheduler run complete — enqueued {Count} remediation run(s).", published);
        return published;
    }
}
