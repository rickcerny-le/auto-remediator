using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoRemediator.Contracts;
using AutoRemediator.Contracts.Dtos;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Messaging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoRemediator.Api.Tests;

public class RepositoriesEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient CreateClient(InMemoryRepositoryStore store) =>
        factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IManagedRepositoryStore>();
                services.AddSingleton<IManagedRepositoryStore>(store);
            })).CreateClient();

    [Fact]
    public async Task Get_returns_configured_repositories()
    {
        var store = new InMemoryRepositoryStore();
        store.Seed(new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api"));
        var client = CreateClient(store);

        var repos = await client.GetFromJsonAsync<List<ManagedRepositoryDto>>("/api/repositories", TestContext.Current.CancellationToken);

        Assert.NotNull(repos);
        Assert.Single(repos);
        Assert.Equal("contoso/platform/web-api", $"{repos[0].Organization}/{repos[0].Project}/{repos[0].Name}");
    }

    [Fact]
    public async Task Post_creates_a_repository()
    {
        var store = new InMemoryRepositoryStore();
        var client = CreateClient(store);

        var response = await client.PostAsJsonAsync(
            "/api/repositories",
            new ManagedRepositoryInput("contoso", "platform", "worker-jobs"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single(await store.ListAsync(TestContext.Current.CancellationToken));
    }

    // ---- Run now (PRD 6.12) ----------------------------------------------------------

    private HttpClient CreateRunClient(InMemoryRepositoryStore store, out RecordingPublisher publisher, out InMemoryRunStore runs)
    {
        var capturedPublisher = publisher = new RecordingPublisher();
        var capturedRuns = runs = new InMemoryRunStore();
        return factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IManagedRepositoryStore>();
                services.RemoveAll<IRemediationRunStore>();
                services.RemoveAll<IMessagePublisher>();
                services.AddSingleton<IManagedRepositoryStore>(store);
                services.AddSingleton<IRemediationRunStore>(capturedRuns);
                services.AddSingleton<IMessagePublisher>(capturedPublisher);
            })).CreateClient();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

    [Fact]
    public async Task Run_enqueues_one_Manual_message_and_returns_202_with_the_run_id()
    {
        var store = new InMemoryRepositoryStore();
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api");
        store.Seed(repo);
        var client = CreateRunClient(store, out var publisher, out _);

        var response = await client.PostAsync($"/api/repositories/{repo.Id}/run", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var runId = body.GetProperty("runId").GetGuid();
        Assert.True(body.GetProperty("enabled").GetBoolean());

        var (queue, message) = Assert.Single(publisher.Messages);
        Assert.Equal(RemediationQueues.RemediationRuns, queue);
        var requested = Assert.IsType<RemediationRunRequested>(message);
        Assert.Equal(runId, requested.RunId);
        Assert.Equal(repo.Id, requested.RepositoryId);
        Assert.Equal(RemediationTrigger.Manual, requested.Trigger);
    }

    [Fact]
    public async Task Run_on_a_disabled_repository_is_accepted_and_leaves_it_disabled()
    {
        var store = new InMemoryRepositoryStore();
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "disabled-repo", enabled: false);
        store.Seed(repo);
        var client = CreateRunClient(store, out var publisher, out _);

        var response = await client.PostAsync($"/api/repositories/{repo.Id}/run", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False((await ReadJsonAsync(response)).GetProperty("enabled").GetBoolean());
        var requested = Assert.IsType<RemediationRunRequested>(Assert.Single(publisher.Messages).Message);
        Assert.Equal(RemediationTrigger.Manual, requested.Trigger);
        Assert.False((await store.GetAsync(repo.Id, TestContext.Current.CancellationToken))!.Enabled);
    }

    [Fact]
    public async Task Run_on_an_unknown_repository_is_404_and_publishes_nothing()
    {
        var client = CreateRunClient(new InMemoryRepositoryStore(), out var publisher, out _);

        var response = await client.PostAsync($"/api/repositories/{Guid.NewGuid()}/run", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(publisher.Messages);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Run_on_a_held_repository_is_409_with_the_held_run_id_and_publishes_nothing(bool enabled)
    {
        var store = new InMemoryRepositoryStore();
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api", enabled);
        store.Seed(repo);
        var client = CreateRunClient(store, out var publisher, out var runs);
        var held = new RemediationRun(Guid.NewGuid(), repo.Id, repo.Slug, DateTimeOffset.UtcNow);
        held.Advance(RunStatus.Remediating);
        held.AwaitingReview("proposal.json", DateTimeOffset.UtcNow);
        await runs.SaveAsync(held, TestContext.Current.CancellationToken);

        var response = await client.PostAsync($"/api/repositories/{repo.Id}/run", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("held", body.GetProperty("reason").GetString());
        Assert.Equal(held.Id, body.GetProperty("heldRunId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        Assert.Empty(publisher.Messages);
    }

    private sealed class RecordingPublisher : IMessagePublisher
    {
        public List<(string Queue, object Message)> Messages { get; } = [];

        public Task PublishAsync<T>(string queue, T message, CancellationToken ct = default)
        {
            Messages.Add((queue, message!));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryRunStore : IRemediationRunStore
    {
        private readonly Dictionary<Guid, RemediationRun> _runs = new();

        public Task SaveAsync(RemediationRun run, CancellationToken ct = default) { _runs[run.Id] = run; return Task.CompletedTask; }
        public Task<IReadOnlyList<RemediationRun>> ListByRepositoryAsync(Guid repositoryId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RemediationRun>>(_runs.Values.Where(r => r.RepositoryId == repositoryId).ToList());
        public Task<IReadOnlyList<RemediationRun>> ListAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RemediationRun>>(_runs.Values.ToList());
        public Task<RemediationRun?> GetAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(_runs.GetValueOrDefault(runId));
        public Task<RemediationRun?> FindAwaitingReviewAsync(Guid repositoryId, CancellationToken ct = default)
            => Task.FromResult(_runs.Values.FirstOrDefault(r => r.RepositoryId == repositoryId && r.Status == RunStatus.AwaitingReview));
    }

    private sealed class InMemoryRepositoryStore : IManagedRepositoryStore
    {
        private readonly Dictionary<Guid, ManagedRepository> _items = new();

        public void Seed(ManagedRepository r) => _items[r.Id] = r;

        public Task<IReadOnlyList<ManagedRepository>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ManagedRepository>>(_items.Values.ToList());

        public Task<ManagedRepository?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_items.GetValueOrDefault(id));

        public Task UpsertAsync(ManagedRepository r, CancellationToken ct = default)
        {
            _items[r.Id] = r;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            _items.Remove(id);
            return Task.CompletedTask;
        }
    }
}
