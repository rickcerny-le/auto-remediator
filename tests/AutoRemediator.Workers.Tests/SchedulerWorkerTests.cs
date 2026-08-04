using AutoRemediator.Contracts;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Worker.Scheduler;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoRemediator.Workers.Tests;

public class SchedulerWorkerTests
{
    [Fact]
    public async Task RunOnce_publishes_one_message_per_enabled_repository()
    {
        var repositories = new List<ManagedRepository>
        {
            new(Guid.NewGuid(), "contoso", "platform", "web-api"),
            new(Guid.NewGuid(), "contoso", "platform", "disabled-repo", enabled: false),
            new(Guid.NewGuid(), "contoso", "platform", "worker-jobs"),
        };

        var publisher = new RecordingPublisher();
        var worker = new SchedulerWorker(
            new FakeManagedRepositoryStore(repositories),
            publisher,
            new FakeHostApplicationLifetime(),
            new ConfigurationManager(),
            TimeProvider.System,
            NullLogger<SchedulerWorker>.Instance);

        var published = await worker.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, published);
        Assert.Equal(2, publisher.Messages.Count);
        Assert.All(publisher.Messages, m =>
        {
            Assert.Equal(RemediationQueues.RemediationRuns, m.Queue);
            Assert.IsType<RemediationRunRequested>(m.Message);
        });
    }

    [Fact]
    public async Task ExecuteAsync_runs_once_and_requests_stop_when_dev_loop_disabled()
    {
        var repositories = new List<ManagedRepository>
        {
            new(Guid.NewGuid(), "contoso", "platform", "web-api"),
        };

        var lifetime = new FakeHostApplicationLifetime();
        var worker = new SchedulerWorker(
            new FakeManagedRepositoryStore(repositories),
            new RecordingPublisher(),
            lifetime,
            new ConfigurationManager(),
            TimeProvider.System,
            NullLogger<SchedulerWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await worker.ExecuteTask!; // completes because the dev loop is off

        Assert.True(lifetime.StopRequested);
    }
}
