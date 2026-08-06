using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Remediation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Infrastructure.Tests;

public class RemediationRunnerTests
{
    private static readonly ManagedRepository Repo = new(Guid.NewGuid(), "orion180", "platform", "web-api");

    private static RemediationRunRequested Request => new(
        Guid.NewGuid(), Repo.Id, Repo.Organization, Repo.Project, Repo.Name, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Completed_when_updates_are_applied_and_pr_opened()
    {
        var plan = new RepositoryUpdatePlan(
            [new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")],
            [new ChangedManifest("/Directory.Packages.props", "<Project/>")]);
        var ado = new FakeAdo { PrUrl = "https://dev.azure.com/org/proj/_git/repo/pullrequest/42" };
        var store = new RecordingRunStore();

        var run = await RunAsync(plan, ado, store);

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("https://dev.azure.com/org/proj/_git/repo/pullrequest/42", run.PullRequestUrl);
        Assert.Equal(1, ado.Pushes);
        Assert.Equal(RunStatus.Completed, store.Last?.Status);
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

    private static async Task<RemediationRun> RunAsync(RepositoryUpdatePlan plan, FakeAdo ado, RecordingRunStore store)
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
        builder.Services.AddSingleton<IManagedRepositoryStore>(new FakeRepoStore(Repo));
        builder.Services.AddSingleton<ITargetingSettingsStore>(new FakeSettingsStore());
        builder.Services.AddSingleton<IUpdatePlanner>(new FakePlanner(plan));
        builder.Services.AddSingleton<IAzureDevOpsClient>(ado);
        builder.Services.AddSingleton<IRemediationRunStore>(store);

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

    private sealed class FakeAdo : IAzureDevOpsClient
    {
        public string PrUrl { get; set; } = "https://pr";
        public bool ThrowOnPush { get; set; }
        public int Pushes { get; private set; }
        public string? LastDescription { get; private set; }

        public Task<bool> RepositoryExistsAsync(ManagedRepository r, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<RepositoryFile>> GetManifestsAsync(ManagedRepository r, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RepositoryFile>>([]);
        public Task<string?> GetBranchHeadAsync(ManagedRepository r, string branch, CancellationToken ct = default) => Task.FromResult<string?>("commit-abc");
        public Task<Stream> GetRepositoryArchiveAsync(ManagedRepository r, string commitId, CancellationToken ct = default)
            => Task.FromResult<Stream>(TestArchive.Empty());
        public Task PushFilesAsync(ManagedRepository r, string branch, string baseCommitId, IReadOnlyList<FileChange> changes, string message, CancellationToken ct = default)
        {
            if (ThrowOnPush) throw new InvalidOperationException("push failed");
            Pushes++;
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
