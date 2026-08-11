using System.Net;
using System.Net.Http.Json;
using AutoRemediator.Contracts.Dtos;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Verification;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoRemediator.Api.Tests;

public class RunsEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Guid CompletedRunId = Guid.NewGuid();
    private static readonly Guid RejectedRunId = Guid.NewGuid();
    private static readonly Guid SkippedRunId = Guid.NewGuid();

    private const string LogReference = "verification.log";
    private const string LogContent = "=== dotnet restore ===\nRestore succeeded.";

    private HttpClient CreateClient()
    {
        var store = new InMemoryRunStore();
        var logs = new InMemoryLogStore { [LogReference] = LogContent };

        var completed = new RemediationRun(CompletedRunId, Guid.NewGuid(), "orion180/platform/web-api", DateTimeOffset.UtcNow.AddMinutes(-5));
        completed.Verified(VerificationOutcome.Verified(LogReference));
        completed.Completed([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")], "https://dev.azure.com/pr/1", DateTimeOffset.UtcNow);

        var failed = new RemediationRun(Guid.NewGuid(), Guid.NewGuid(), "orion180/platform/worker", DateTimeOffset.UtcNow);
        failed.Failed("boom", DateTimeOffset.UtcNow);

        var rejected = new RemediationRun(RejectedRunId, Guid.NewGuid(), "orion180/platform/api", DateTimeOffset.UtcNow);
        rejected.VerificationFailed(
            [new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")],
            VerificationOutcome.DependencyFailure(
                [new VerificationDiagnostic("CS0117", "'Client' has no member 'SubmitAsync'", "src/Foo/Bar.cs", 42, 17)],
                LogReference),
            DateTimeOffset.UtcNow);

        var skipped = new RemediationRun(SkippedRunId, Guid.NewGuid(), "orion180/platform/jobs", DateTimeOffset.UtcNow);
        skipped.Verified(VerificationOutcome.Skipped("the configured feed was unreachable"));
        skipped.Completed([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")], "https://dev.azure.com/pr/2", DateTimeOffset.UtcNow);

        store.Seed(completed);
        store.Seed(failed);
        store.Seed(rejected);
        store.Seed(skipped);

        return factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRemediationRunStore>();
                services.RemoveAll<IVerificationLogStore>();
                services.AddSingleton<IRemediationRunStore>(store);
                services.AddSingleton<IVerificationLogStore>(logs);
            })).CreateClient();
    }

    [Fact]
    public async Task List_returns_all_runs()
    {
        var runs = await CreateClient().GetFromJsonAsync<List<RunSummaryDto>>("/api/runs", TestContext.Current.CancellationToken);

        Assert.NotNull(runs);
        Assert.Equal(4, runs.Count);
    }

    [Fact]
    public async Task List_filters_by_VerificationFailed_separately_from_Failed()
    {
        var client = CreateClient();

        var rejected = await client.GetFromJsonAsync<List<RunSummaryDto>>(
            "/api/runs?status=VerificationFailed", TestContext.Current.CancellationToken);
        var crashed = await client.GetFromJsonAsync<List<RunSummaryDto>>(
            "/api/runs?status=Failed", TestContext.Current.CancellationToken);

        Assert.Equal(RejectedRunId, Assert.Single(rejected!).Id);
        Assert.Equal("Failed", Assert.Single(crashed!).Status);
    }

    [Fact]
    public async Task List_filters_by_status()
    {
        var runs = await CreateClient().GetFromJsonAsync<List<RunSummaryDto>>("/api/runs?status=Completed", TestContext.Current.CancellationToken);

        Assert.NotEmpty(runs!);
        Assert.All(runs!, r => Assert.Equal("Completed", r.Status));
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

    [Fact]
    public async Task Detail_reports_a_verified_run()
    {
        var run = await CreateClient().GetFromJsonAsync<RunDetailDto>($"/api/runs/{CompletedRunId}", TestContext.Current.CancellationToken);

        Assert.NotNull(run!.Verification);
        Assert.Equal("Verified", run.Verification!.Classification);
        Assert.Null(run.Verification.SkipReason);
        Assert.Empty(run.Verification.Diagnostics);
        Assert.Equal($"/api/runs/{CompletedRunId}/verification-log", run.Verification.LogUrl);
    }

    [Fact]
    public async Task Detail_reports_diagnostics_for_a_rejected_run()
    {
        var run = await CreateClient().GetFromJsonAsync<RunDetailDto>($"/api/runs/{RejectedRunId}", TestContext.Current.CancellationToken);

        Assert.Equal("VerificationFailed", run!.Status);
        Assert.Null(run.PullRequestUrl);
        Assert.Equal("DependencyFailure", run.Verification!.Classification);

        var diagnostic = Assert.Single(run.Verification.Diagnostics);
        Assert.Equal("CS0117", diagnostic.Code);
        Assert.Equal("src/Foo/Bar.cs", diagnostic.Path);
        Assert.Equal(42, diagnostic.Line);
        Assert.Equal(17, diagnostic.Column);
    }

    [Fact]
    public async Task Detail_reports_the_skip_reason_on_a_completed_run()
    {
        var run = await CreateClient().GetFromJsonAsync<RunDetailDto>($"/api/runs/{SkippedRunId}", TestContext.Current.CancellationToken);

        Assert.Equal("Completed", run!.Status);
        Assert.Equal("https://dev.azure.com/pr/2", run.PullRequestUrl);
        Assert.Equal("Skipped", run.Verification!.Classification);
        Assert.Equal("the configured feed was unreachable", run.Verification.SkipReason);

        // No log was stored for this outcome, so there is nothing to link to.
        Assert.Null(run.Verification.LogUrl);
    }

    [Fact]
    public async Task Verification_log_is_served_as_plain_text()
    {
        var response = await CreateClient().GetAsync(
            $"/api/runs/{CompletedRunId}/verification-log", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(LogContent, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Verification_log_is_404_when_the_run_stored_none()
    {
        var response = await CreateClient().GetAsync(
            $"/api/runs/{SkippedRunId}/verification-log", TestContext.Current.CancellationToken);

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

    private sealed class InMemoryLogStore : IVerificationLogStore
    {
        private readonly Dictionary<string, string> _logs = [];

        public string this[string reference]
        {
            set => _logs[reference] = value;
        }

        public Task<string?> StoreAsync(Guid runId, string content, string name = "verification.log", CancellationToken ct = default)
        {
            var reference = $"{runId}/{name}";
            _logs[reference] = content;
            return Task.FromResult<string?>(reference);
        }

        public Task<string?> ReadAsync(string reference, CancellationToken ct = default)
            => Task.FromResult(_logs.GetValueOrDefault(reference));
    }
}
