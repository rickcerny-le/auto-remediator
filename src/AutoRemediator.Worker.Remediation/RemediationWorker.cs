using System.Text.Json;
using AutoRemediator.Agents;
using AutoRemediator.Contracts;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Infrastructure.Messaging;

namespace AutoRemediator.Worker.Remediation;

/// <summary>
/// Consumes <see cref="RemediationRunRequested"/> messages from Service Bus and delegates
/// each to the remediation agent. Deployed as an event-driven Azure Container Apps Job
/// scaled by queue depth (KEDA). Placeholder — no real remediation is performed yet.
/// </summary>
public sealed class RemediationWorker(
    IMessageConsumer consumer,
    IRemediationAgent agent,
    ILogger<RemediationWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => consumer.RunAsync(RemediationQueues.RemediationRuns, HandleMessageAsync, stoppingToken);

    private async Task HandleMessageAsync(string body, CancellationToken cancellationToken)
    {
        var run = JsonSerializer.Deserialize<RemediationRunRequested>(body);
        if (run is null)
        {
            logger.LogWarning("Received a message that could not be parsed as {Type}.", nameof(RemediationRunRequested));
            return;
        }

        logger.LogInformation("Received remediation run {RunId} for {Repository}.", run.RunId, run.RepositoryName);

        var outcome = await agent.RemediateAsync(run, cancellationToken);

        logger.LogInformation("Remediation run {RunId} finished: {Summary}", run.RunId, outcome.Summary);
    }
}
