using System.Text.Json;
using AutoRemediator.Contracts;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Infrastructure.Messaging;
using AutoRemediator.Infrastructure.Remediation;
using Microsoft.Extensions.DependencyInjection;

namespace AutoRemediator.Worker.Remediation;

/// <summary>
/// Consumes <see cref="RemediationRunRequested"/> messages from Service Bus and runs each
/// through the <see cref="IRemediationRunner"/> (bump matched packages → open/refresh a PR).
/// Deployed as an event-driven Azure Container Apps Job scaled by queue depth (KEDA).
/// </summary>
public sealed class RemediationWorker(
    IMessageConsumer consumer,
    IServiceScopeFactory scopeFactory,
    ILogger<RemediationWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => consumer.RunAsync(RemediationQueues.RemediationRuns, HandleMessageAsync, stoppingToken);

    private async Task HandleMessageAsync(string body, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<RemediationRunRequested>(body);
        if (request is null)
        {
            logger.LogWarning("Received a message that could not be parsed as {Type}.", nameof(RemediationRunRequested));
            return;
        }

        logger.LogInformation("Received remediation run {RunId} for {Repository}.", request.RunId, request.RepositoryName);

        // Runner is scoped; create a scope per message.
        await using var scope = scopeFactory.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IRemediationRunner>();
        var run = await runner.RunAsync(request, cancellationToken);

        logger.LogInformation(
            "Remediation run {RunId} finished: {Status}{PullRequest}",
            run.Id,
            run.Status,
            run.PullRequestUrl is null ? string.Empty : $" ({run.PullRequestUrl})");
    }
}
