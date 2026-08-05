using System.Net;
using System.Net.Http.Json;
using AutoRemediator.Contracts.Dtos;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoRemediator.Api.Tests;

public class RunsEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Guid CompletedRunId = Guid.NewGuid();

    private HttpClient CreateClient()
    {
        var store = new InMemoryRunStore();

        var completed = new RemediationRun(CompletedRunId, Guid.NewGuid(), "orion180/platform/web-api", DateTimeOffset.UtcNow.AddMinutes(-5));
        completed.Completed([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")], "https://dev.azure.com/pr/1", DateTimeOffset.UtcNow);
        var failed = new RemediationRun(Guid.NewGuid(), Guid.NewGuid(), "orion180/platform/worker", DateTimeOffset.UtcNow);
        failed.Failed("boom", DateTimeOffset.UtcNow);

        store.Seed(completed);
        store.Seed(failed);

        return factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRemediationRunStore>();
                services.AddSingleton<IRemediationRunStore>(store);
            })).CreateClient();
    }

    [Fact]
    public async Task List_returns_all_runs()
    {
        var runs = await CreateClient().GetFromJsonAsync<List<RunSummaryDto>>("/api/runs", TestContext.Current.CancellationToken);

        Assert.NotNull(runs);
        Assert.Equal(2, runs.Count);
    }

    [Fact]
    public async Task List_filters_by_status()
    {
        var runs = await CreateClient().GetFromJsonAsync<List<RunSummaryDto>>("/api/runs?status=Completed", TestContext.Current.CancellationToken);

        var run = Assert.Single(runs!);
        Assert.Equal("Completed", run.Status);
    }

    [Fact]
    public async Task Detail_returns_run_with_updates()
    {
        var run = await CreateClient().GetFromJsonAsync<RunDetailDto>($"/api/runs/{CompletedRunId}", TestContext.Current.CancellationToken);

        Assert.NotNull(run);
        Assert.Equal("https://dev.azure.com/pr/1", run.PullRequestUrl);
        Assert.Equal("Orion180.Core", Assert.Single(run.Updates).PackageId);
    }

    [Fact]
    public async Task Detail_returns_404_for_unknown_run()
    {
        var response = await CreateClient().GetAsync($"/api/runs/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class InMemoryRunStore : IRemediationRunStore
    {
        private readonly Dictionary<Guid, RemediationRun> _runs = new();

        public void Seed(RemediationRun run) => _runs[run.Id] = run;

        public Task SaveAsync(RemediationRun run, CancellationToken ct = default) { _runs[run.Id] = run; return Task.CompletedTask; }
        public Task<IReadOnlyList<RemediationRun>> ListByRepositoryAsync(Guid repositoryId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RemediationRun>>(_runs.Values.Where(r => r.RepositoryId == repositoryId).ToList());
        public Task<IReadOnlyList<RemediationRun>> ListAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RemediationRun>>(_runs.Values.ToList());
        public Task<RemediationRun?> GetAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(_runs.GetValueOrDefault(runId));
    }
}
