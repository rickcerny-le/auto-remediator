using System.Text.Json;
using AutoRemediator.Contracts;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Messaging;
using AutoRemediator.Infrastructure.Remediation;

namespace AutoRemediator.Infrastructure.Tests;

public class RemediationRunRequesterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private sealed class RecordingPublisher : IMessagePublisher
    {
        public List<(string Queue, object Message)> Messages { get; } = [];

        public Task PublishAsync<T>(string queue, T message, CancellationToken cancellationToken = default)
        {
            Messages.Add((queue, message!));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static (RemediationRunRequester Requester, RecordingPublisher Publisher, InMemoryRunStore Runs) Create()
    {
        var runs = new InMemoryRunStore();
        var publisher = new RecordingPublisher();
        return (new RemediationRunRequester(runs, publisher, new FixedTime(Now)), publisher, runs);
    }

    [Theory]
    [InlineData(RemediationTrigger.Scheduled)]
    [InlineData(RemediationTrigger.Manual)]
    public async Task An_unheld_repository_is_enqueued_with_the_trigger_and_a_fresh_run_id(RemediationTrigger trigger)
    {
        var (requester, publisher, _) = Create();
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api");

        var result = await requester.RequestAsync(repo, trigger, TestContext.Current.CancellationToken);

        var enqueued = Assert.IsType<RunRequestResult.Enqueued>(result);
        var (queue, message) = Assert.Single(publisher.Messages);
        Assert.Equal(RemediationQueues.RemediationRuns, queue);
        var requested = Assert.IsType<RemediationRunRequested>(message);
        Assert.Equal(enqueued.RunId, requested.RunId);
        Assert.NotEqual(Guid.Empty, requested.RunId);
        Assert.Equal(repo.Id, requested.RepositoryId);
        Assert.Equal("contoso", requested.Organization);
        Assert.Equal("platform", requested.Project);
        Assert.Equal("web-api", requested.RepositoryName);
        Assert.Equal(Now, requested.RequestedAtUtc);
        Assert.Equal(trigger, requested.Trigger);
    }

    [Fact]
    public async Task A_disabled_repository_is_still_enqueued_because_enabled_is_not_checked()
    {
        var (requester, publisher, _) = Create();
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "disabled-repo", enabled: false);

        var result = await requester.RequestAsync(repo, RemediationTrigger.Manual, TestContext.Current.CancellationToken);

        Assert.IsType<RunRequestResult.Enqueued>(result);
        Assert.Single(publisher.Messages);
    }

    [Fact]
    public async Task A_held_repository_returns_Held_and_publishes_nothing()
    {
        var (requester, publisher, runs) = Create();
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api");
        var held = new RemediationRun(Guid.NewGuid(), repo.Id, repo.Slug, Now);
        held.Advance(RunStatus.Remediating);
        held.AwaitingReview("proposal.json", Now);
        await runs.SaveAsync(held, TestContext.Current.CancellationToken);

        var result = await requester.RequestAsync(repo, RemediationTrigger.Manual, TestContext.Current.CancellationToken);

        Assert.Equal(held.Id, Assert.IsType<RunRequestResult.Held>(result).HeldRunId);
        Assert.Empty(publisher.Messages);
    }

    [Fact]
    public async Task A_held_repository_is_not_recorded_as_skipped_by_the_requester()
    {
        var (requester, _, runs) = Create();
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api");
        var held = new RemediationRun(Guid.NewGuid(), repo.Id, repo.Slug, Now);
        held.Advance(RunStatus.Remediating);
        held.AwaitingReview("proposal.json", Now);
        await runs.SaveAsync(held, TestContext.Current.CancellationToken);

        await requester.RequestAsync(repo, RemediationTrigger.Scheduled, TestContext.Current.CancellationToken);

        Assert.Single(await runs.ListAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_message_without_a_Trigger_property_deserializes_as_Scheduled()
    {
        var json = $$"""
            {"RunId":"{{Guid.NewGuid()}}","RepositoryId":"{{Guid.NewGuid()}}","Organization":"contoso",
             "Project":"platform","RepositoryName":"web-api","RequestedAtUtc":"2026-10-14T09:00:00+00:00"}
            """;

        var message = JsonSerializer.Deserialize<RemediationRunRequested>(json);

        Assert.NotNull(message);
        Assert.Equal(RemediationTrigger.Scheduled, message!.Trigger);
    }

    [Fact]
    public void The_trigger_serializes_as_a_string_and_round_trips()
    {
        var original = new RemediationRunRequested(
            Guid.NewGuid(), Guid.NewGuid(), "contoso", "platform", "web-api", Now, RemediationTrigger.Manual);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<RemediationRunRequested>(json);

        Assert.Contains("\"Trigger\":\"Manual\"", json);
        Assert.Equal(original, restored);
    }
}
