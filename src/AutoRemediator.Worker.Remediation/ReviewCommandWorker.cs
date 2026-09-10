using System.Text.Json;
using AutoRemediator.Contracts;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Infrastructure.Messaging;
using AutoRemediator.Infrastructure.Review;
using Microsoft.Extensions.DependencyInjection;

namespace AutoRemediator.Worker.Remediation;

/// <summary>
/// Consumes <see cref="ReviewCommandRequested"/> messages from Service Bus and executes the
/// corresponding review command. A second, independent consumer alongside
/// <see cref="RemediationWorker"/> — a malformed review command must not stall scheduled runs, and
/// vice versa (research.md Decision 4). Deployed as the same event-driven Container Apps Job, with
/// a second KEDA scale rule on the <c>review-commands</c> queue.
/// </summary>
public sealed class ReviewCommandWorker(
    IMessageConsumer consumer,
    IServiceScopeFactory scopeFactory,
    ILogger<ReviewCommandWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => consumer.RunAsync(RemediationQueues.ReviewCommands, HandleMessageAsync, stoppingToken);

    private async Task HandleMessageAsync(string body, CancellationToken cancellationToken)
    {
        ReviewCommandRequested? request;
        try
        {
            request = JsonSerializer.Deserialize<ReviewCommandRequested>(body);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request is null)
        {
            // An unparseable body is logged and completed, not retried — retrying would not parse
            // any better the second time (contracts/review-messages.md).
            logger.LogWarning("Received a message that could not be parsed as {Type}.", nameof(ReviewCommandRequested));
            return;
        }

        logger.LogInformation("Received review command {Command} for run {RunId}.", request.Command, request.RunId);

        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IReviewCommandHandler>();
        await handler.HandleAsync(request.RunId, request.Command, cancellationToken);

        logger.LogInformation("Finished review command {Command} for run {RunId}.", request.Command, request.RunId);
    }
}
