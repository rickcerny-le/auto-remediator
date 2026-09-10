using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoRemediator.Contracts;
using AutoRemediator.Contracts.Dtos;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Messaging;
using AutoRemediator.Infrastructure.Review;
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

    private static readonly Guid RepairedRunId = Guid.NewGuid();

    private const string LogReference = "verification.log";
    private const string LogContent = "=== dotnet restore ===\nRestore succeeded.";
    private const string TranscriptReference = "remediation-transcript.md";
    private const string TranscriptContent = "## Attempt 1\n- applied `src/App/Program.cs`";

    private static readonly Guid AwaitingReviewRunId = Guid.NewGuid();
    private static readonly Guid AwaitingReviewRepositoryId = Guid.NewGuid();
    private const string ProposalReference = "proposal.json";

    private HttpClient CreateClient() => CreateClient(out _, out _, out _);

    private HttpClient CreateClient(out InMemoryRunStore store, out FakeProposalStore proposals, out RecordingPublisher publisher)
    {
        store = new InMemoryRunStore();
        var logs = new InMemoryLogStore
        {
            [LogReference] = LogContent,
            [TranscriptReference] = TranscriptContent,
        };

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

        var repaired = new RemediationRun(RepairedRunId, Guid.NewGuid(), "orion180/platform/gateway", DateTimeOffset.UtcNow);
        repaired.Verified(VerificationOutcome.Verified(LogReference));
        repaired.Remediated(attempts: 2, transcriptReference: TranscriptReference);
        repaired.Completed([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")], "https://dev.azure.com/pr/5", DateTimeOffset.UtcNow);

        var verifiedAt = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var awaitingReview = new RemediationRun(AwaitingReviewRunId, AwaitingReviewRepositoryId, "orion180/platform/checkout", DateTimeOffset.UtcNow.AddMinutes(-10));
        awaitingReview.RecordUpdates([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")]);
        awaitingReview.Advance(RunStatus.Remediating);
        awaitingReview.Remediated(attempts: 2, transcriptReference: TranscriptReference);
        awaitingReview.AwaitingReview(ProposalReference, verifiedAt);

        var proposal = new ChangeProposal(
            AwaitingReviewRunId, AwaitingReviewRepositoryId, "commit-abc", verifiedAt,
            [
                new ProposedFile("/Directory.Packages.props", "<Old/>", "<New/>", ProposedFileOrigin.Manifest),
                new ProposedFile("/src/App/Program.cs", "old source", "new source", ProposedFileOrigin.AgentEdit),
            ],
            [new VerificationDiagnostic("CS1061", "'Client' has no member 'SubmitAsync'", "src/App/Program.cs", 3, 42)],
            2, TranscriptReference);

        store.Seed(completed);
        store.Seed(failed);
        store.Seed(rejected);
        store.Seed(skipped);
        store.Seed(repaired);
        store.Seed(awaitingReview);

        proposals = new FakeProposalStore { [ProposalReference] = proposal };
        publisher = new RecordingPublisher();
        var capturedProposals = proposals;
        var capturedPublisher = publisher;
        var capturedStore = store;

        return factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRemediationRunStore>();
                services.RemoveAll<IVerificationLogStore>();
                services.RemoveAll<IChangeProposalStore>();
                services.RemoveAll<IMessagePublisher>();
                services.RemoveAll<IAzureDevOpsClient>();
                services.AddSingleton<IRemediationRunStore>(capturedStore);
                services.AddSingleton<IVerificationLogStore>(logs);
                services.AddSingleton<IChangeProposalStore>(capturedProposals);
                services.AddSingleton<IMessagePublisher>(capturedPublisher);
                services.AddSingleton<IAzureDevOpsClient>(new NeverCalledAdo());
            })).CreateClient();
    }

    [Fact]
    public async Task List_returns_all_runs()
    {
        var runs = await CreateClient().GetFromJsonAsync<List<RunSummaryDto>>("/api/runs", TestContext.Current.CancellationToken);

        Assert.NotNull(runs);
        Assert.Equal(6, runs.Count);
    }

    [Fact]
    public async Task Detail_reports_the_remediation_attempts_and_transcript()
    {
        var run = await CreateClient().GetFromJsonAsync<RunDetailDto>($"/api/runs/{RepairedRunId}", TestContext.Current.CancellationToken);

        Assert.NotNull(run!.Remediation);
        Assert.Equal(2, run.Remediation!.Attempts);
        Assert.Equal($"/api/runs/{RepairedRunId}/remediation-transcript", run.Remediation.TranscriptUrl);
    }

    [Fact]
    public async Task Detail_reports_no_remediation_when_the_loop_never_ran()
    {
        var run = await CreateClient().GetFromJsonAsync<RunDetailDto>($"/api/runs/{CompletedRunId}", TestContext.Current.CancellationToken);

        Assert.Null(run!.Remediation);
    }

    [Fact]
    public async Task Transcript_is_served_as_plain_text()
    {
        var response = await CreateClient().GetAsync(
            $"/api/runs/{RepairedRunId}/remediation-transcript", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TranscriptContent, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Transcript_is_404_when_the_run_stored_none()
    {
        var response = await CreateClient().GetAsync(
            $"/api/runs/{CompletedRunId}/remediation-transcript", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
    public async Task List_filters_by_AwaitingReview_returning_every_held_repository()
    {
        var runs = await CreateClient().GetFromJsonAsync<List<RunSummaryDto>>(
            "/api/runs?status=AwaitingReview", TestContext.Current.CancellationToken);

        var held = Assert.Single(runs!);
        Assert.Equal(AwaitingReviewRunId, held.Id);
        Assert.Equal("orion180/platform/checkout", held.RepositorySlug);
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

    // ---- GET /api/runs/{id}/proposal ---------------------------------------------------

    [Fact]
    public async Task Proposal_returns_before_and_after_content_diagnostics_attempts_base_commit_and_verification_time()
    {
        var proposal = await CreateClient().GetFromJsonAsync<ChangeProposalDto>(
            $"/api/runs/{AwaitingReviewRunId}/proposal", TestContext.Current.CancellationToken);

        Assert.NotNull(proposal);
        Assert.Equal(AwaitingReviewRunId, proposal!.RunId);
        Assert.Equal("commit-abc", proposal.BaseCommitId);
        Assert.Equal(2, proposal.Attempts);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero), proposal.VerifiedAtUtc);
        Assert.Equal($"/api/runs/{AwaitingReviewRunId}/remediation-transcript", proposal.TranscriptUrl);

        var agentEdit = proposal.Files.Single(f => f.Origin == "AgentEdit");
        Assert.Equal("old source", agentEdit.OriginalContent);
        Assert.Equal("new source", agentEdit.NewContent);

        var manifest = proposal.Files.Single(f => f.Origin == "Manifest");
        Assert.Equal("<Old/>", manifest.OriginalContent);
        Assert.Equal("<New/>", manifest.NewContent);

        var diagnostic = Assert.Single(proposal.ProvokingDiagnostics);
        Assert.Equal("CS1061", diagnostic.Code);
    }

    [Fact]
    public async Task Proposal_is_404_for_a_run_with_no_proposal()
    {
        var response = await CreateClient().GetAsync(
            $"/api/runs/{CompletedRunId}/proposal", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Proposal_is_404_for_an_unknown_run()
    {
        var response = await CreateClient().GetAsync(
            $"/api/runs/{Guid.NewGuid()}/proposal", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Detail_links_the_proposal_and_carries_the_review_note()
    {
        var run = await CreateClient().GetFromJsonAsync<RunDetailDto>(
            $"/api/runs/{AwaitingReviewRunId}", TestContext.Current.CancellationToken);

        Assert.Equal("AwaitingReview", run!.Status);
        Assert.Equal($"/api/runs/{AwaitingReviewRunId}/proposal", run.ProposalUrl);
    }

    // ---- POST /api/runs/{id}/review/{command} ------------------------------------------

    [Theory]
    [InlineData("approve", ReviewCommand.Approve)]
    [InlineData("rebuild", ReviewCommand.Rebuild)]
    [InlineData("retry", ReviewCommand.Retry)]
    [InlineData("discard", ReviewCommand.Discard)]
    public async Task Each_command_returns_202_and_publishes_exactly_one_message_for_a_held_run(string command, ReviewCommand expected)
    {
        var client = CreateClient(out _, out _, out var publisher);

        var response = await client.PostAsync($"/api/runs/{AwaitingReviewRunId}/review/{command}", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(AwaitingReviewRunId, body.GetProperty("runId").GetGuid());
        Assert.True(body.GetProperty("accepted").GetBoolean());

        var published = Assert.Single(publisher.Messages);
        Assert.Equal(RemediationQueues.ReviewCommands, published.Queue);
        var reviewCommand = Assert.IsType<ReviewCommandRequested>(published.Message);
        Assert.Equal(AwaitingReviewRunId, reviewCommand.RunId);
        Assert.Equal(expected, reviewCommand.Command);
    }

    [Fact]
    public async Task A_command_against_a_Completed_run_returns_409_and_publishes_nothing()
    {
        var client = CreateClient(out _, out _, out var publisher);

        var response = await client.PostAsync($"/api/runs/{CompletedRunId}/review/approve", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(publisher.Messages);
    }

    [Fact]
    public async Task A_command_against_an_unknown_run_returns_404_and_publishes_nothing()
    {
        var client = CreateClient(out _, out _, out var publisher);

        var response = await client.PostAsync($"/api/runs/{Guid.NewGuid()}/review/approve", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(publisher.Messages);
    }

    [Fact]
    public async Task An_unrecognized_command_name_returns_400_and_publishes_nothing()
    {
        var client = CreateClient(out _, out _, out var publisher);

        var response = await client.PostAsync($"/api/runs/{AwaitingReviewRunId}/review/frobnicate", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(publisher.Messages);
    }

    [Fact]
    public async Task No_review_endpoint_ever_calls_azure_devops()
    {
        // NeverCalledAdo throws if touched — reaching 202/404/409/400 without an exception proves it.
        var client = CreateClient(out _, out _, out _);

        await client.PostAsync($"/api/runs/{AwaitingReviewRunId}/review/approve", content: null, TestContext.Current.CancellationToken);
        await client.PostAsync($"/api/runs/{CompletedRunId}/review/approve", content: null, TestContext.Current.CancellationToken);
        await client.GetAsync($"/api/runs/{AwaitingReviewRunId}/proposal", TestContext.Current.CancellationToken);
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
        public Task<RemediationRun?> FindAwaitingReviewAsync(Guid repositoryId, CancellationToken ct = default)
            => Task.FromResult(_runs.Values.FirstOrDefault(r => r.RepositoryId == repositoryId && r.Status == RunStatus.AwaitingReview));
    }

    private sealed class FakeProposalStore : IChangeProposalStore
    {
        private readonly Dictionary<string, ChangeProposal> _stored = [];

        public ChangeProposal this[string reference]
        {
            set => _stored[reference] = value;
        }

        public Task<string> StoreAsync(ChangeProposal proposal, CancellationToken ct = default)
        {
            var reference = $"{proposal.RunId}/proposal.json";
            _stored[reference] = proposal;
            return Task.FromResult(reference);
        }

        public Task<ChangeProposal?> ReadAsync(string reference, CancellationToken ct = default)
            => Task.FromResult(_stored.GetValueOrDefault(reference));
    }

    private sealed record PublishedMessage(string Queue, object Message);

    private sealed class RecordingPublisher : IMessagePublisher
    {
        public List<PublishedMessage> Messages { get; } = [];

        public Task PublishAsync<T>(string queue, T message, CancellationToken ct = default)
        {
            Messages.Add(new PublishedMessage(queue, message!));
            return Task.CompletedTask;
        }
    }

    /// <summary>An Azure DevOps client double that fails the test if any member is invoked — the API must never call it.</summary>
    private sealed class NeverCalledAdo : IAzureDevOpsClient
    {
        private static InvalidOperationException NotExpected([System.Runtime.CompilerServices.CallerMemberName] string? member = null)
            => new($"The API endpoint must not call IAzureDevOpsClient.{member}.");

        public Task<bool> RepositoryExistsAsync(ManagedRepository r, CancellationToken ct = default) => throw NotExpected();
        public Task<IReadOnlyList<RepositoryFile>> GetManifestsAsync(ManagedRepository r, CancellationToken ct = default) => throw NotExpected();
        public Task<string?> GetBranchHeadAsync(ManagedRepository r, string branch, CancellationToken ct = default) => throw NotExpected();
        public Task<Stream> GetRepositoryArchiveAsync(ManagedRepository r, string commitId, CancellationToken ct = default) => throw NotExpected();
        public Task PushFilesAsync(ManagedRepository r, string branch, string baseCommitId, IReadOnlyList<FileChange> changes, string message, CancellationToken ct = default) => throw NotExpected();
        public Task<string> EnsurePullRequestAsync(ManagedRepository r, string s, string t, string title, string desc, CancellationToken ct = default) => throw NotExpected();
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
