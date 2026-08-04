using System.Text.Json;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Worker.Remediation;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoRemediator.Workers.Tests;

public class RemediationWorkerTests
{
    [Fact]
    public async Task Worker_deserializes_message_and_delegates_to_agent()
    {
        var run = new RemediationRunRequested(
            RunId: Guid.NewGuid(),
            RepositoryId: Guid.NewGuid(),
            Organization: "contoso",
            Project: "platform",
            RepositoryName: "web-api",
            RequestedAtUtc: DateTimeOffset.UtcNow);

        var consumer = new SingleMessageConsumer(JsonSerializer.Serialize(run));
        var agent = new RecordingAgent();
        var worker = new RemediationWorker(consumer, agent, NullLogger<RemediationWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await consumer.Completed;
        await worker.StopAsync(TestContext.Current.CancellationToken);

        var received = Assert.Single(agent.Received);
        Assert.Equal(run.RunId, received.RunId);
        Assert.Equal("web-api", received.RepositoryName);
    }
}
