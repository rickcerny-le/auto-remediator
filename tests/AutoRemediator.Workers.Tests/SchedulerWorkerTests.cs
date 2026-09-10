using AutoRemediator.Contracts;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
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
            new FakeRunStore(),
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
            new FakeRunStore(),
            new RecordingPublisher(),
            lifetime,
            new ConfigurationManager(),
            TimeProvider.System,
            NullLogger<SchedulerWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await worker.ExecuteTask!; // completes because the dev loop is off

        Assert.True(lifetime.StopRequested);
    }

    // ---- Held repository visibility (US5) ----------------------------------------------

    [Fact]
    public async Task A_repository_with_an_open_proposal_is_not_enqueued()
    {
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api");
        var runStore = new FakeRunStore();
        runStore.SeedHeld(repo.Id);
        var publisher = new RecordingPublisher();

        var worker = new SchedulerWorker(
            new FakeManagedRepositoryStore([repo]), runStore, publisher,
            new FakeHostApplicationLifetime(), new ConfigurationManager(), TimeProvider.System,
            NullLogger<SchedulerWorker>.Instance);

        var published = await worker.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, published);
        Assert.Empty(publisher.Messages);
    }

    [Fact]
    public async Task The_skip_is_recorded_as_a_SkippedHeld_run_naming_the_repository()
    {
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api");
        var runStore = new FakeRunStore();
        runStore.SeedHeld(repo.Id);

        var worker = new SchedulerWorker(
            new FakeManagedRepositoryStore([repo]), runStore, new RecordingPublisher(),
            new FakeHostApplicationLifetime(), new ConfigurationManager(), TimeProvider.System,
            NullLogger<SchedulerWorker>.Instance);

        await worker.RunOnceAsync(TestContext.Current.CancellationToken);

        var recorded = Assert.Single(runStore.Saved, r => r.Status == RunStatus.SkippedHeld);
        Assert.Equal(repo.Slug, recorded.RepositorySlug);
        Assert.NotNull(recorded.FinishedAtUtc);
    }

    [Fact]
    public async Task A_repository_whose_proposal_was_resolved_is_enqueued_normally()
    {
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api");
        var runStore = new FakeRunStore(); // no held run seeded — the proposal was approved or discarded
        var publisher = new RecordingPublisher();

        var worker = new SchedulerWorker(
            new FakeManagedRepositoryStore([repo]), runStore, publisher,
            new FakeHostApplicationLifetime(), new ConfigurationManager(), TimeProvider.System,
            NullLogger<SchedulerWorker>.Instance);

        var published = await worker.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, published);
        Assert.Single(publisher.Messages);
    }

    [Fact]
    public async Task A_repository_held_across_two_passes_is_not_enqueued_twice()
    {
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api");
        var runStore = new FakeRunStore();
        runStore.SeedHeld(repo.Id);
        var publisher = new RecordingPublisher();

        var worker = new SchedulerWorker(
            new FakeManagedRepositoryStore([repo]), runStore, publisher,
            new FakeHostApplicationLifetime(), new ConfigurationManager(), TimeProvider.System,
            NullLogger<SchedulerWorker>.Instance);

        await worker.RunOnceAsync(TestContext.Current.CancellationToken);
        await worker.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Empty(publisher.Messages);
        Assert.Equal(2, runStore.Saved.Count(r => r.Status == RunStatus.SkippedHeld));
    }

    private sealed class FakeRunStore : IRemediationRunStore
    {
        private readonly Dictionary<Guid, RemediationRun> _held = [];
        public List<RemediationRun> Saved { get; } = [];

        public void SeedHeld(Guid repositoryId)
        {
            var run = new RemediationRun(Guid.NewGuid(), repositoryId, "contoso/platform/web-api", DateTimeOffset.UtcNow);
            run.Advance(RunStatus.Remediating);
            run.AwaitingReview("proposal.json", DateTimeOffset.UtcNow);
            _held[repositoryId] = run;
        }

        public Task SaveAsync(RemediationRun run, CancellationToken ct = default) { Saved.Add(run); return Task.CompletedTask; }
        public Task<IReadOnlyList<RemediationRun>> ListByRepositoryAsync(Guid repositoryId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RemediationRun>>([]);
        public Task<IReadOnlyList<RemediationRun>> ListAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RemediationRun>>([]);
        public Task<RemediationRun?> GetAsync(Guid runId, CancellationToken ct = default) => Task.FromResult<RemediationRun?>(null);
        public Task<RemediationRun?> FindAwaitingReviewAsync(Guid repositoryId, CancellationToken ct = default)
            => Task.FromResult(_held.GetValueOrDefault(repositoryId));
    }
}
