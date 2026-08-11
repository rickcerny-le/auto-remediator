using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Remediation;
using AutoRemediator.Infrastructure.Verification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Infrastructure.Tests;

public class RemediationRunnerTests
{
    private static readonly ManagedRepository Repo = new(Guid.NewGuid(), "orion180", "platform", "web-api");

    private static RemediationRunRequested Request => new(
        Guid.NewGuid(), Repo.Id, Repo.Organization, Repo.Project, Repo.Name, DateTimeOffset.UtcNow);

    private static RepositoryUpdatePlan SinglePlan => new(
        [new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")],
        [new ChangedManifest("/Directory.Packages.props", "<Project/>")]);

    [Fact]
    public async Task Completed_when_updates_are_applied_and_pr_opened()
    {
        var ado = new FakeAdo { PrUrl = "https://dev.azure.com/org/proj/_git/repo/pullrequest/42" };
        var store = new RecordingRunStore();

        var run = await RunAsync(SinglePlan, ado, store);

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("https://dev.azure.com/org/proj/_git/repo/pullrequest/42", run.PullRequestUrl);
        Assert.Equal(1, ado.Pushes);
        Assert.Equal(RunStatus.Completed, store.Last?.Status);
        Assert.Equal(VerificationClassification.Verified, run.Verification?.Classification);
    }

    // ---- Verification outcomes -----------------------------------------------------

    [Fact]
    public async Task Verified_change_is_pushed_and_the_pr_says_so()
    {
        var ado = new FakeAdo();

        var run = await RunAsync(SinglePlan, ado, new RecordingRunStore());

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(1, ado.Pushes);
        Assert.Contains("Verified locally", ado.LastDescription);
        Assert.DoesNotContain("Not verified", ado.LastDescription);
    }

    [Fact]
    public async Task Rejected_change_ends_in_VerificationFailed_with_no_push_and_no_pr()
    {
        var ado = new FakeAdo();
        var store = new RecordingRunStore();
        var outcome = VerificationOutcome.DependencyFailure(
            [new VerificationDiagnostic("CS0117", "'Client' has no member 'SubmitAsync'", "src/Foo/Bar.cs", 42, 17)],
            "runs/abc/verification.log");

        var run = await RunAsync(SinglePlan, ado, store, new FakeVerification(new VerificationResult(outcome, [])));

        Assert.Equal(RunStatus.VerificationFailed, run.Status);
        Assert.Equal(0, ado.Pushes);
        Assert.Null(ado.LastDescription);
        Assert.Null(run.PullRequestUrl);
        Assert.Null(run.Error);
        Assert.Equal(RunStatus.VerificationFailed, store.Last?.Status);

        // The attempted updates and diagnostics are kept for diagnosis.
        Assert.Equal("Orion180.Core", Assert.Single(run.Updates).PackageId);
        Assert.Equal("CS0117", run.Verification?.Diagnostics.Single().Code);
        Assert.Equal("runs/abc/verification.log", run.Verification?.LogReference);
    }

    [Fact]
    public async Task Skipped_verification_still_opens_a_pr_marked_not_verified()
    {
        var ado = new FakeAdo();
        var outcome = VerificationOutcome.Skipped("the configured feed was unreachable");

        var run = await RunAsync(SinglePlan, ado, new RecordingRunStore(), new FakeVerification(new VerificationResult(outcome, [])));

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(1, ado.Pushes);
        Assert.NotNull(run.PullRequestUrl);
        Assert.Contains("Not verified locally", ado.LastDescription);
        Assert.Contains("the configured feed was unreachable", ado.LastDescription);
        Assert.Equal(VerificationClassification.Skipped, run.Verification?.Classification);
    }

    [Fact]
    public async Task Regenerated_lock_file_joins_the_pushed_changes()
    {
        var ado = new FakeAdo();
        var result = new VerificationResult(
            VerificationOutcome.Verified(),
            [new FileChange("/src/Web/packages.lock.json", """{ "version": 1 }""")]);

        await RunAsync(SinglePlan, ado, new RecordingRunStore(), new FakeVerification(result));

        Assert.Equal(
            ["/Directory.Packages.props", "/src/Web/packages.lock.json"],
            ado.LastChanges!.Select(c => c.Path));
    }

    [Fact]
    public async Task Verification_runs_against_the_commit_the_push_is_based_on()
    {
        var verification = new FakeVerification(new VerificationResult(VerificationOutcome.Verified(), []));

        await RunAsync(SinglePlan, new FakeAdo(), new RecordingRunStore(), verification);

        Assert.Equal("commit-abc", verification.CommitId);
    }

    [Fact]
    public async Task Nothing_is_pushed_before_verification_runs()
    {
        var ado = new FakeAdo();
        var verification = new FakeVerification(new VerificationResult(VerificationOutcome.Verified(), []))
        {
            OnVerify = () => Assert.Equal(0, ado.Pushes),
        };

        await RunAsync(SinglePlan, ado, new RecordingRunStore(), verification);

        Assert.Equal(1, ado.Pushes);
    }

    [Fact]
    public async Task NoUpdates_never_downloads_a_tree_or_verifies()
    {
        var verification = new FakeVerification(new VerificationResult(VerificationOutcome.Verified(), []));

        var run = await RunAsync(RepositoryUpdatePlan.Empty, new FakeAdo(), new RecordingRunStore(), verification);

        Assert.Equal(RunStatus.NoUpdates, run.Status);
        Assert.False(verification.WasCalled);
        Assert.Null(run.Verification);
    }

    [Fact]
    public async Task Pr_description_always_names_the_repository_ci_as_the_authority()
    {
        var ado = new FakeAdo();

        await RunAsync(SinglePlan, ado, new RecordingRunStore());

        Assert.Contains("validated by this repository's own CI", ado.LastDescription);
    }

    [Fact]
    public async Task Pr_summary_labels_collateral_and_notes_escalation()
    {
        var plan = new RepositoryUpdatePlan(
            [
                new DependencyUpdate("Orion180.Core", "1.4.0", "1.5.0", UpdateKind.Matched),
                new DependencyUpdate("Orion180.Common", "1.4.0", "2.0.0", UpdateKind.Collateral, BeyondPolicy: true),
            ],
            [new ChangedManifest("/Directory.Packages.props", "<Project/>")]);
        var ado = new FakeAdo();

        await RunAsync(plan, ado, new RecordingRunStore());

        Assert.NotNull(ado.LastDescription);
        Assert.Contains("collateral", ado.LastDescription);
        Assert.Contains("beyond policy", ado.LastDescription);
    }

    [Fact]
    public async Task NoUpdates_when_plan_is_empty()
    {
        var ado = new FakeAdo();

        var run = await RunAsync(RepositoryUpdatePlan.Empty, ado, new RecordingRunStore());

        Assert.Equal(RunStatus.NoUpdates, run.Status);
        Assert.Equal(0, ado.Pushes);
        Assert.Null(run.PullRequestUrl);
    }

    [Fact]
    public async Task Failed_when_a_write_throws()
    {
        var plan = new RepositoryUpdatePlan(
            [new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")],
            [new ChangedManifest("/Directory.Packages.props", "<Project/>")]);
        var ado = new FakeAdo { ThrowOnPush = true };
        var store = new RecordingRunStore();

        var run = await RunAsync(plan, ado, store);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.NotNull(run.Error);
        Assert.Equal(RunStatus.Failed, store.Last?.Status);
    }

    private static async Task<RemediationRun> RunAsync(
        RepositoryUpdatePlan plan,
        FakeAdo ado,
        RecordingRunStore store,
        FakeVerification? verification = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=k;SharedAccessKey=a2V5a2V5a2V5a2V5";
        builder.AddInfrastructure();

        builder.Services.RemoveAll<IManagedRepositoryStore>();
        builder.Services.RemoveAll<ITargetingSettingsStore>();
        builder.Services.RemoveAll<IUpdatePlanner>();
        builder.Services.RemoveAll<IAzureDevOpsClient>();
        builder.Services.RemoveAll<IRemediationRunStore>();
        builder.Services.RemoveAll<IVerificationService>();
        builder.Services.AddSingleton<IManagedRepositoryStore>(new FakeRepoStore(Repo));
        builder.Services.AddSingleton<ITargetingSettingsStore>(new FakeSettingsStore());
        builder.Services.AddSingleton<IUpdatePlanner>(new FakePlanner(plan));
        builder.Services.AddSingleton<IAzureDevOpsClient>(ado);
        builder.Services.AddSingleton<IRemediationRunStore>(store);
        builder.Services.AddSingleton<IVerificationService>(
            verification ?? new FakeVerification(new VerificationResult(VerificationOutcome.Verified(), [])));

        await using var provider = builder.Services.BuildServiceProvider();
        return await provider.GetRequiredService<IRemediationRunner>().RunAsync(Request, TestContext.Current.CancellationToken);
    }

    private sealed class FakeRepoStore(ManagedRepository repo) : IManagedRepositoryStore
    {
        public Task<IReadOnlyList<ManagedRepository>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ManagedRepository>>([repo]);
        public Task<ManagedRepository?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ManagedRepository?>(id == repo.Id ? repo : null);
        public Task UpsertAsync(ManagedRepository r, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeSettingsStore : ITargetingSettingsStore
    {
        public Task<TargetingSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(new TargetingSettings(["Orion180.*"], feeds: ["https://feed"]));
        public Task SetAsync(TargetingSettings s, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakePlanner(RepositoryUpdatePlan plan) : IUpdatePlanner
    {
        public Task<RepositoryUpdatePlan> PlanAsync(ManagedRepository r, TargetingSettings s, CancellationToken ct = default) => Task.FromResult(plan);
    }

    private sealed class FakeVerification(VerificationResult verificationResult) : IVerificationService
    {
        private readonly VerificationResult result = verificationResult;

        public bool WasCalled { get; private set; }
        public string? CommitId { get; private set; }
        public Action? OnVerify { get; set; }
        public bool SessionDisposed { get; private set; }

        public Task<IVerificationSession> OpenAsync(
            Guid runId, ManagedRepository repository, string commitId, RepositoryUpdatePlan plan,
            TargetingSettings settings, CancellationToken ct = default)
        {
            CommitId = commitId;
            return Task.FromResult<IVerificationSession>(new FakeSession(this));
        }

        private sealed class FakeSession(FakeVerification owner) : IVerificationSession
        {
            public IVerificationWorkspace? Workspace => null;

            public Task<VerificationResult> VerifyAsync(CancellationToken ct = default)
            {
                owner.WasCalled = true;
                owner.OnVerify?.Invoke();
                return Task.FromResult(owner.result);
            }

            public void Dispose() => owner.SessionDisposed = true;
        }
    }

    private sealed class FakeAdo : IAzureDevOpsClient
    {
        public string PrUrl { get; set; } = "https://pr";
        public bool ThrowOnPush { get; set; }
        public int Pushes { get; private set; }
        public string? LastDescription { get; private set; }
        public IReadOnlyList<FileChange>? LastChanges { get; private set; }

        public Task<bool> RepositoryExistsAsync(ManagedRepository r, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<RepositoryFile>> GetManifestsAsync(ManagedRepository r, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RepositoryFile>>([]);
        public Task<string?> GetBranchHeadAsync(ManagedRepository r, string branch, CancellationToken ct = default) => Task.FromResult<string?>("commit-abc");
        public Task<Stream> GetRepositoryArchiveAsync(ManagedRepository r, string commitId, CancellationToken ct = default)
            => Task.FromResult<Stream>(TestArchive.Empty());
        public Task PushFilesAsync(ManagedRepository r, string branch, string baseCommitId, IReadOnlyList<FileChange> changes, string message, CancellationToken ct = default)
        {
            if (ThrowOnPush) throw new InvalidOperationException("push failed");
            Pushes++;
            LastChanges = changes;
            return Task.CompletedTask;
        }
        public Task<string> EnsurePullRequestAsync(ManagedRepository r, string s, string t, string title, string desc, CancellationToken ct = default)
        {
            LastDescription = desc;
            return Task.FromResult(PrUrl);
        }
    }

    private sealed class RecordingRunStore : IRemediationRunStore
    {
        public RemediationRun? Last { get; private set; }
        public Task SaveAsync(RemediationRun run, CancellationToken ct = default) { Last = run; return Task.CompletedTask; }
        public Task<IReadOnlyList<RemediationRun>> ListByRepositoryAsync(Guid repositoryId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RemediationRun>>([]);
        public Task<IReadOnlyList<RemediationRun>> ListAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RemediationRun>>(Last is null ? [] : [Last]);
        public Task<RemediationRun?> GetAsync(Guid runId, CancellationToken ct = default) => Task.FromResult(Last?.Id == runId ? Last : null);
    }
}
