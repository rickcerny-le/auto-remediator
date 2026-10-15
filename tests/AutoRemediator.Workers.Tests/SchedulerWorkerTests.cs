using AutoRemediator.Contracts;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Messaging;
using AutoRemediator.Infrastructure.Remediation;
using AutoRemediator.Worker.Scheduler;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace AutoRemediator.Workers.Tests;

public class SchedulerWorkerTests
{
    private static readonly DateTimeOffset Day = new(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ManagedRepository Repo(string name, bool enabled = true, string? schedule = null) =>
        new(Guid.NewGuid(), "contoso", "platform", name, enabled, "main",
            schedule is null ? null : new RepositorySettings(schedule: schedule));

    private static SchedulerWorker Worker(
        IReadOnlyList<ManagedRepository> repos,
        IRemediationRunStore runStore,
        IMessagePublisher publisher,
        InMemoryScheduleStateStore state,
        TimeProvider time,
        TargetingSettings? global = null,
        IHostApplicationLifetime? lifetime = null) =>
        new(new FakeManagedRepositoryStore(repos),
            runStore,
            new RemediationRunRequester(runStore, publisher, time),
            new FakeEffectiveSettingsProvider(global),
            state,
            lifetime ?? new FakeHostApplicationLifetime(),
            new ConfigurationManager(),
            time,
            NullLogger<SchedulerWorker>.Instance);

    [Fact]
    public async Task RunOnce_publishes_one_message_per_enabled_never_scheduled_repository()
    {
        var repositories = new List<ManagedRepository>
        {
            Repo("web-api"),
            Repo("disabled-repo", enabled: false),
            Repo("worker-jobs"),
        };

        var publisher = new RecordingPublisher();
        var worker = Worker(repositories, new FakeRunStore(), publisher, new InMemoryScheduleStateStore(), new FakeTimeProvider(Day));

        var published = await worker.RunOnceAsync(Ct);

        Assert.Equal(2, published);
        Assert.Equal(2, publisher.Messages.Count);
        Assert.All(publisher.Messages, m =>
        {
            Assert.Equal(RemediationQueues.RemediationRuns, m.Queue);
            var requested = Assert.IsType<RemediationRunRequested>(m.Message);
            Assert.Equal(RemediationTrigger.Scheduled, requested.Trigger);
        });
        Assert.DoesNotContain(publisher.Messages, m => ((RemediationRunRequested)m.Message).RepositoryName == "disabled-repo");
    }

    [Fact]
    public async Task ExecuteAsync_runs_once_and_requests_stop_when_dev_loop_disabled()
    {
        var lifetime = new FakeHostApplicationLifetime();
        var worker = Worker([Repo("web-api")], new FakeRunStore(), new RecordingPublisher(),
            new InMemoryScheduleStateStore(), TimeProvider.System, lifetime: lifetime);

        await worker.StartAsync(Ct);
        await worker.ExecuteTask!; // completes because the dev loop is off

        Assert.True(lifetime.StopRequested);
    }

    // ---- Due-based scheduling ------------------------------------------------------------

    [Fact]
    public async Task A_due_repository_is_enqueued_and_its_state_is_set_to_now()
    {
        var repo = Repo("web-api");
        var state = new InMemoryScheduleStateStore();
        state.Seed(repo.Id, Day.AddDays(-1).AddSeconds(5));
        var publisher = new RecordingPublisher();
        var time = new FakeTimeProvider(Day.AddSeconds(30));

        var published = await Worker([repo], new FakeRunStore(), publisher, state, time).RunOnceAsync(Ct);

        Assert.Equal(1, published);
        Assert.Single(publisher.Messages);
        Assert.Equal(time.GetUtcNow(), state.LastFor(repo.Id));
    }

    [Fact]
    public async Task A_not_due_repository_is_not_enqueued_and_its_state_is_unchanged()
    {
        var repo = Repo("web-api");
        var state = new InMemoryScheduleStateStore();
        var last = Day.AddDays(-1).AddSeconds(5);
        state.Seed(repo.Id, last);
        var publisher = new RecordingPublisher();

        var published = await Worker([repo], new FakeRunStore(), publisher, state,
            new FakeTimeProvider(Day.AddHours(-1))).RunOnceAsync(Ct);

        Assert.Equal(0, published);
        Assert.Empty(publisher.Messages);
        Assert.Equal(last, state.LastFor(repo.Id));
        Assert.Equal(0, state.ClaimCount);
    }

    [Fact]
    public async Task A_never_scheduled_repository_is_enqueued_at_the_first_tick_whatever_its_schedule()
    {
        var repo = Repo("web-api", schedule: "0 3 * * 1"); // Mondays 03:00; 2026-03-10 is a Tuesday
        var publisher = new RecordingPublisher();
        var time = new FakeTimeProvider(Day.AddHours(10).AddMinutes(15));
        var state = new InMemoryScheduleStateStore();

        var published = await Worker([repo], new FakeRunStore(), publisher, state, time).RunOnceAsync(Ct);

        Assert.Equal(1, published);
        Assert.Equal(time.GetUtcNow(), state.LastFor(repo.Id));
    }

    [Fact]
    public async Task Disabled_repositories_are_never_evaluated_and_their_state_is_untouched()
    {
        var repo = Repo("disabled-repo", enabled: false);
        var state = new InMemoryScheduleStateStore();
        var last = Day.AddDays(-5);
        state.Seed(repo.Id, last);
        var publisher = new RecordingPublisher();

        var published = await Worker([repo], new FakeRunStore(), publisher, state, new FakeTimeProvider(Day)).RunOnceAsync(Ct);

        Assert.Equal(0, published);
        Assert.Empty(publisher.Messages);
        Assert.Empty(state.Reads);
        Assert.Equal(0, state.ClaimCount);
        Assert.Equal(last, state.LastFor(repo.Id));
    }

    [Fact]
    public async Task A_manual_run_on_a_disabled_repository_leaves_it_unscheduled()
    {
        var repo = Repo("disabled-repo", enabled: false);
        var runStore = new FakeRunStore();
        var publisher = new RecordingPublisher();
        var state = new InMemoryScheduleStateStore();
        var time = new FakeTimeProvider(Day);
        var requester = new RemediationRunRequester(runStore, publisher, time);

        var manual = await requester.RequestAsync(repo, RemediationTrigger.Manual, Ct);
        Assert.IsType<RunRequestResult.Enqueued>(manual);

        var published = await Worker([repo], runStore, publisher, state, time).RunOnceAsync(Ct);

        Assert.Equal(0, published);
        Assert.False(state.HasRow(repo.Id));
        var only = Assert.Single(publisher.Messages);
        Assert.Equal(RemediationTrigger.Manual, ((RemediationRunRequested)only.Message).Trigger);
    }

    [Fact]
    public async Task A_due_held_repository_records_one_SkippedHeld_and_advances_its_state()
    {
        var repo = Repo("web-api");
        var runStore = new FakeRunStore();
        runStore.SeedHeld(repo.Id);
        var publisher = new RecordingPublisher();
        var state = new InMemoryScheduleStateStore();
        var time = new FakeTimeProvider(Day);
        var worker = Worker([repo], runStore, publisher, state, time);

        var first = await worker.RunOnceAsync(Ct);
        var second = await worker.RunOnceAsync(Ct); // same instant: no longer due

        Assert.Equal(0, first);
        Assert.Equal(0, second);
        Assert.Empty(publisher.Messages);
        var recorded = Assert.Single(runStore.Saved, r => r.Status == RunStatus.SkippedHeld);
        Assert.Equal(repo.Slug, recorded.RepositorySlug);
        Assert.NotNull(recorded.FinishedAtUtc);
        Assert.Equal(Day, state.LastFor(repo.Id));
    }

    [Fact]
    public async Task A_held_repository_that_is_not_due_records_nothing()
    {
        var repo = Repo("web-api");
        var runStore = new FakeRunStore();
        runStore.SeedHeld(repo.Id);
        var state = new InMemoryScheduleStateStore();
        state.Seed(repo.Id, Day.AddSeconds(5));

        await Worker([repo], runStore, new RecordingPublisher(), state,
            new FakeTimeProvider(Day.AddHours(12))).RunOnceAsync(Ct);

        Assert.Empty(runStore.Saved);
    }

    [Fact]
    public async Task A_repository_whose_proposal_was_resolved_is_enqueued_normally()
    {
        var publisher = new RecordingPublisher();

        var published = await Worker([Repo("web-api")], new FakeRunStore(), publisher,
            new InMemoryScheduleStateStore(), new FakeTimeProvider(Day)).RunOnceAsync(Ct);

        Assert.Equal(1, published);
        Assert.Single(publisher.Messages);
    }

    [Fact]
    public async Task Schedules_fire_at_their_own_times_across_a_simulated_day()
    {
        var daily3 = Repo("daily-3am", schedule: "0 3 * * *");
        var sixHourly = Repo("six-hourly", schedule: "0 */6 * * *");
        var unscheduled = Repo("no-schedule");
        var state = new InMemoryScheduleStateStore();
        foreach (var r in new[] { daily3, sixHourly, unscheduled })
        {
            state.Seed(r.Id, Day);
        }

        var publisher = new RecordingPublisher();
        var time = new FakeTimeProvider(Day);
        var worker = Worker([daily3, sixHourly, unscheduled], new FakeRunStore(), publisher, state, time);

        var firedAt = new List<(string Name, DateTimeOffset At)>();
        for (var tick = 1; tick <= 96; tick++)
        {
            time.Advance(TimeSpan.FromMinutes(15));
            var before = publisher.Messages.Count;
            await worker.RunOnceAsync(Ct);
            firedAt.AddRange(publisher.Messages.Skip(before)
                .Select(m => (((RemediationRunRequested)m.Message).RepositoryName, time.GetUtcNow())));
        }

        Assert.Equal(Day.AddDays(1), time.GetUtcNow());
        Assert.Equal([Day.AddHours(3)], firedAt.Where(f => f.Name == "daily-3am").Select(f => f.At));
        Assert.Equal(
            [Day.AddHours(6), Day.AddHours(12), Day.AddHours(18), Day.AddDays(1)],
            firedAt.Where(f => f.Name == "six-hourly").Select(f => f.At));
        Assert.Equal([Day.AddDays(1)], firedAt.Where(f => f.Name == "no-schedule").Select(f => f.At));
    }

    [Fact]
    public async Task Many_missed_occurrences_collapse_into_one_run()
    {
        var repo = Repo("web-api", schedule: "0 */6 * * *");
        var state = new InMemoryScheduleStateStore();
        state.Seed(repo.Id, Day.AddDays(-3));
        var publisher = new RecordingPublisher();
        var time = new FakeTimeProvider(Day.AddMinutes(1));
        var worker = Worker([repo], new FakeRunStore(), publisher, state, time);

        await worker.RunOnceAsync(Ct);
        time.Advance(TimeSpan.FromMinutes(15));
        await worker.RunOnceAsync(Ct);

        Assert.Single(publisher.Messages);
    }

    [Fact]
    public async Task A_repository_with_no_schedule_uses_the_built_in_daily_midnight_default()
    {
        var repo = Repo("web-api");
        var state = new InMemoryScheduleStateStore();
        state.Seed(repo.Id, Day.AddSeconds(5)); // yesterday-relative: the previous midnight tick
        var publisher = new RecordingPublisher();
        var time = new FakeTimeProvider(Day.AddHours(23).AddMinutes(45));
        var worker = Worker([repo], new FakeRunStore(), publisher, state, time);

        Assert.Equal(0, await worker.RunOnceAsync(Ct));

        time.Advance(TimeSpan.FromMinutes(15)); // 00:00 tick
        Assert.Equal(1, await worker.RunOnceAsync(Ct));
    }

    [Fact]
    public async Task The_global_default_schedule_applies_when_the_repository_has_none()
    {
        var repo = Repo("web-api");
        var state = new InMemoryScheduleStateStore();
        state.Seed(repo.Id, Day.AddSeconds(5));
        var global = new TargetingSettings(["Contoso.*"], defaultSchedule: "0 */6 * * *");
        var publisher = new RecordingPublisher();
        var time = new FakeTimeProvider(Day.AddHours(6));

        var published = await Worker([repo], new FakeRunStore(), publisher, state, time, global).RunOnceAsync(Ct);

        Assert.Equal(1, published);
    }

    [Fact]
    public async Task A_lost_claim_publishes_nothing()
    {
        var repo = Repo("web-api");
        var state = new InMemoryScheduleStateStore { RejectClaims = true };
        var publisher = new RecordingPublisher();

        var published = await Worker([repo], new FakeRunStore(), publisher, state, new FakeTimeProvider(Day)).RunOnceAsync(Ct);

        Assert.Equal(0, published);
        Assert.Empty(publisher.Messages);
        Assert.Equal(1, state.ClaimCount);
    }

    [Fact]
    public async Task Overlapping_ticks_cannot_both_run_a_repository()
    {
        var repo = Repo("web-api");
        var state = new InMemoryScheduleStateStore { ReadBarrier = new Barrier(2) }; // both read before either claims
        var publisher = new FailingPublisher();
        var runStore = new FakeRunStore();
        var time = new FakeTimeProvider(Day);
        var a = Worker([repo], runStore, publisher, state, time);
        var b = Worker([repo], runStore, publisher, state, time);

        var results = await Task.WhenAll(Task.Run(() => a.RunOnceAsync(Ct), Ct), Task.Run(() => b.RunOnceAsync(Ct), Ct));

        Assert.Equal(1, results.Sum());
        Assert.Single(publisher.Messages);
        Assert.Equal(2, state.ClaimCount);
    }

    [Fact]
    public async Task A_publish_exception_reverts_the_state_and_the_tick_continues()
    {
        var broken = Repo("broken");
        var healthy = Repo("healthy");
        var state = new InMemoryScheduleStateStore();
        var previous = Day.AddDays(-1).AddSeconds(5);
        state.Seed(broken.Id, previous);
        var publisher = new FailingPublisher("broken");

        var published = await Worker([broken, healthy], new FakeRunStore(), publisher, state,
            new FakeTimeProvider(Day)).RunOnceAsync(Ct);

        Assert.Equal(1, published);
        Assert.Equal("healthy", ((RemediationRunRequested)Assert.Single(publisher.Messages).Message).RepositoryName);
        Assert.Equal(previous, state.LastFor(broken.Id));
        Assert.Equal(Day, state.LastFor(healthy.Id));
    }

    [Fact]
    public async Task An_invalid_stored_cron_is_not_due()
    {
        var repo = Repo("web-api", schedule: "not a cron");
        var publisher = new RecordingPublisher();

        var published = await Worker([repo], new FakeRunStore(), publisher, new InMemoryScheduleStateStore(),
            new FakeTimeProvider(Day)).RunOnceAsync(Ct);

        Assert.Equal(0, published);
        Assert.Empty(publisher.Messages);
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

        public Task SaveAsync(RemediationRun run, CancellationToken ct = default) { lock (Saved) { Saved.Add(run); } return Task.CompletedTask; }
        public Task<IReadOnlyList<RemediationRun>> ListByRepositoryAsync(Guid repositoryId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RemediationRun>>([]);
        public Task<IReadOnlyList<RemediationRun>> ListAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RemediationRun>>([]);
        public Task<RemediationRun?> GetAsync(Guid runId, CancellationToken ct = default) => Task.FromResult<RemediationRun?>(null);
        public Task<RemediationRun?> FindAwaitingReviewAsync(Guid repositoryId, CancellationToken ct = default)
            => Task.FromResult(_held.GetValueOrDefault(repositoryId));
    }
}
