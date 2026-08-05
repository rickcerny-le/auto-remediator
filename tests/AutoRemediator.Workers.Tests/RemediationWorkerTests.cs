using System.Text.Json;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Remediation;
using AutoRemediator.Worker.Remediation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoRemediator.Workers.Tests;

public class RemediationWorkerTests
{
    [Fact]
    public async Task Worker_deserializes_message_and_invokes_runner()
    {
        var request = new RemediationRunRequested(
            RunId: Guid.NewGuid(),
            RepositoryId: Guid.NewGuid(),
            Organization: "orion180",
            Project: "platform",
            RepositoryName: "web-api",
            RequestedAtUtc: DateTimeOffset.UtcNow);

        var consumer = new SingleMessageConsumer(JsonSerializer.Serialize(request));
        var runner = new RecordingRunner();

        var services = new ServiceCollection();
        services.AddScoped<IRemediationRunner>(_ => runner);
        using var provider = services.BuildServiceProvider();

        var worker = new RemediationWorker(
            consumer,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RemediationWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await consumer.Completed;
        await worker.StopAsync(TestContext.Current.CancellationToken);

        var received = Assert.Single(runner.Received);
        Assert.Equal(request.RunId, received.RunId);
        Assert.Equal("web-api", received.RepositoryName);
    }

    private sealed class RecordingRunner : IRemediationRunner
    {
        public List<RemediationRunRequested> Received { get; } = [];

        public Task<RemediationRun> RunAsync(RemediationRunRequested request, CancellationToken cancellationToken = default)
        {
            Received.Add(request);
            var run = new RemediationRun(request.RunId, request.RepositoryId, "orion180/platform/web-api", DateTimeOffset.UtcNow);
            run.NoUpdates(DateTimeOffset.UtcNow);
            return Task.FromResult(run);
        }
    }
}
