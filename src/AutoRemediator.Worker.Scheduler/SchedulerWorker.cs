using AutoRemediator.Contracts;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Messaging;

namespace AutoRemediator.Worker.Scheduler;

/// <summary>
/// Enumerates enrolled repositories and enqueues one <see cref="RemediationRunRequested"/>
/// per repository. Deployed as a scheduled (cron) Azure Container Apps Job: it runs its
/// work once and stops the host. A dev-only loop (Scheduler:DevLoopEnabled) can re-trigger
/// the run locally without changing the production run-once shape.
/// </summary>
public sealed class SchedulerWorker(
    IManagedRepositoryStore repositories,
    IMessagePublisher publisher,
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
            var message = new RemediationRunRequested(
                RunId: Guid.NewGuid(),
                RepositoryId: repo.Id,
                Organization: repo.Organization,
                Project: repo.Project,
                RepositoryName: repo.Name,
                RequestedAtUtc: timeProvider.GetUtcNow());

            await publisher.PublishAsync(RemediationQueues.RemediationRuns, message, cancellationToken);
            published++;

            logger.LogInformation("Enqueued remediation run {RunId} for {Slug}", message.RunId, repo.Slug);
        }

        logger.LogInformation("Scheduler run complete — enqueued {Count} remediation run(s).", published);
        return published;
    }
}
