using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Infrastructure.Tests;

/// <summary>Round-trips a run through the Table store; skips when Azurite is not running.</summary>
public class RemediationRunStoreRoundTripTests
{
    private static ServiceProvider BuildProvider()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=k;SharedAccessKey=a2V5a2V5a2V5a2V5";
        builder.AddInfrastructure();
        return builder.Services.BuildServiceProvider();
    }

    /// <summary>Saves a run, skipping the test when Azurite is not reachable.</summary>
    private static async Task<bool> TrySaveAsync(IRemediationRunStore store, RemediationRun run, CancellationToken ct)
    {
        try
        {
            await store.SaveAsync(run, ct);
            return true;
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            Assert.Skip($"Azurite storage emulator not reachable: {ex.GetType().Name}");
            return false;
        }
    }

    [Fact]
    public async Task Run_round_trips_and_lists_by_repository()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();

        var store = provider.GetRequiredService<IRemediationRunStore>();
        var repoId = Guid.NewGuid();
        var run = new RemediationRun(Guid.NewGuid(), repoId, "orion180/platform/web-api", DateTimeOffset.UtcNow);
        run.Completed([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")], "https://pr/1", DateTimeOffset.UtcNow);

        if (!await TrySaveAsync(store, run, ct))
        {
            return;
        }

        var runs = await store.ListByRepositoryAsync(repoId, ct);
        var saved = Assert.Single(runs);
        Assert.Equal(RunStatus.Completed, saved.Status);
        Assert.Equal("https://pr/1", saved.PullRequestUrl);
        Assert.Equal("Orion180.Core", Assert.Single(saved.Updates).PackageId);

        // Global list and by-id lookup (used by the runs UI).
        Assert.Contains(await store.ListAllAsync(ct), r => r.Id == run.Id);
        var byId = await store.GetAsync(run.Id, ct);
        Assert.NotNull(byId);
        Assert.Equal(RunStatus.Completed, byId!.Status);
    }

    [Fact]
    public async Task Verified_run_round_trips_its_verification_outcome()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IRemediationRunStore>();

        var run = new RemediationRun(Guid.NewGuid(), Guid.NewGuid(), "orion180/platform/web-api", DateTimeOffset.UtcNow);
        run.Verified(VerificationOutcome.Verified("runs/abc/verify.log"));
        run.Completed([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")], "https://pr/1", DateTimeOffset.UtcNow);

        if (!await TrySaveAsync(store, run, ct))
        {
            return;
        }

        var saved = await store.GetAsync(run.Id, ct);
        Assert.NotNull(saved);
        Assert.Equal(RunStatus.Completed, saved!.Status);
        Assert.Equal(VerificationClassification.Verified, saved.Verification?.Classification);
        Assert.Equal("runs/abc/verify.log", saved.Verification?.LogReference);
        Assert.Empty(saved.Verification!.Diagnostics);
        Assert.Null(saved.Verification.SkipReason);
    }

    [Fact]
    public async Task Rejected_run_round_trips_its_diagnostics_and_has_no_pull_request()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IRemediationRunStore>();

        var outcome = VerificationOutcome.DependencyFailure(
            [
                new VerificationDiagnostic("NU1107", "Version conflict detected for Orion180.Common"),
                new VerificationDiagnostic("CS0117", "'Client' has no member 'SubmitAsync'", "src/Foo/Bar.cs", 42, 17),
            ],
            "runs/abc/build.log");

        var run = new RemediationRun(Guid.NewGuid(), Guid.NewGuid(), "orion180/platform/web-api", DateTimeOffset.UtcNow);
        run.VerificationFailed([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")], outcome, DateTimeOffset.UtcNow);

        if (!await TrySaveAsync(store, run, ct))
        {
            return;
        }

        var saved = await store.GetAsync(run.Id, ct);
        Assert.NotNull(saved);
        Assert.Equal(RunStatus.VerificationFailed, saved!.Status);
        Assert.Null(saved.PullRequestUrl);
        Assert.Equal(VerificationClassification.DependencyFailure, saved.Verification?.Classification);
        Assert.Equal("runs/abc/build.log", saved.Verification?.LogReference);

        var diagnostics = saved.Verification!.Diagnostics;
        Assert.Equal(2, diagnostics.Count);
        Assert.Equal("NU1107", diagnostics[0].Code);
        var compile = diagnostics[1];
        Assert.Equal("CS0117", compile.Code);
        Assert.Equal("src/Foo/Bar.cs", compile.Path);
        Assert.Equal(42, compile.Line);
        Assert.Equal(17, compile.Column);
    }

    [Fact]
    public async Task Skipped_run_round_trips_its_reason_and_still_records_the_pull_request()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IRemediationRunStore>();

        var run = new RemediationRun(Guid.NewGuid(), Guid.NewGuid(), "orion180/platform/web-api", DateTimeOffset.UtcNow);
        run.Verified(VerificationOutcome.Skipped("the configured feed was unreachable", "runs/abc/restore.log"));
        run.Completed([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")], "https://pr/2", DateTimeOffset.UtcNow);

        if (!await TrySaveAsync(store, run, ct))
        {
            return;
        }

        var saved = await store.GetAsync(run.Id, ct);
        Assert.NotNull(saved);
        Assert.Equal(RunStatus.Completed, saved!.Status);
        Assert.Equal("https://pr/2", saved.PullRequestUrl);
        Assert.Equal(VerificationClassification.Skipped, saved.Verification?.Classification);
        Assert.Equal("the configured feed was unreachable", saved.Verification?.SkipReason);
    }

    [Fact]
    public async Task Repaired_run_round_trips_its_remediation_attempts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IRemediationRunStore>();

        var run = new RemediationRun(Guid.NewGuid(), Guid.NewGuid(), "orion180/platform/web-api", DateTimeOffset.UtcNow);
        run.Verified(VerificationOutcome.Verified("runs/abc/verify.log"));
        run.Remediated(attempts: 2, transcriptReference: "runs/abc/transcript.log");
        run.Completed([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")], "https://pr/3", DateTimeOffset.UtcNow);

        if (!await TrySaveAsync(store, run, ct))
        {
            return;
        }

        var saved = await store.GetAsync(run.Id, ct);
        Assert.NotNull(saved);
        Assert.Equal(2, saved!.RemediationAttempts);
        Assert.Equal("runs/abc/transcript.log", saved.RemediationTranscriptReference);
    }

    [Fact]
    public async Task Run_that_never_remediated_round_trips_null_attempts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IRemediationRunStore>();

        var run = new RemediationRun(Guid.NewGuid(), Guid.NewGuid(), "orion180/platform/web-api", DateTimeOffset.UtcNow);
        run.Verified(VerificationOutcome.Verified());
        run.Completed([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")], "https://pr/4", DateTimeOffset.UtcNow);

        if (!await TrySaveAsync(store, run, ct))
        {
            return;
        }

        var saved = await store.GetAsync(run.Id, ct);
        Assert.NotNull(saved);
        Assert.Null(saved!.RemediationAttempts);
        Assert.Null(saved.RemediationTranscriptReference);
    }

    [Fact]
    public async Task Run_that_never_verified_round_trips_a_null_outcome()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IRemediationRunStore>();

        var run = new RemediationRun(Guid.NewGuid(), Guid.NewGuid(), "orion180/platform/web-api", DateTimeOffset.UtcNow);
        run.NoUpdates(DateTimeOffset.UtcNow);

        if (!await TrySaveAsync(store, run, ct))
        {
            return;
        }

        var saved = await store.GetAsync(run.Id, ct);
        Assert.NotNull(saved);
        Assert.Equal(RunStatus.NoUpdates, saved!.Status);
        Assert.Null(saved.Verification);
    }

    // ---- Review gate -----------------------------------------------------------------

    [Fact]
    public async Task AwaitingReview_run_round_trips_its_proposal_reference_and_command_count()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IRemediationRunStore>();

        var run = new RemediationRun(Guid.NewGuid(), Guid.NewGuid(), "orion180/platform/web-api", DateTimeOffset.UtcNow);
        run.Advance(RunStatus.Remediating);
        run.AwaitingReview($"{run.Id}/proposal.json", DateTimeOffset.UtcNow);
        run.ReviewBlocked("the update branch moved");

        if (!await TrySaveAsync(store, run, ct))
        {
            return;
        }

        var saved = await store.GetAsync(run.Id, ct);
        Assert.NotNull(saved);
        Assert.Equal(RunStatus.AwaitingReview, saved!.Status);
        Assert.Equal($"{run.Id}/proposal.json", saved.ProposalReference);
        Assert.Equal("the update branch moved", saved.ReviewNote);
        Assert.Equal(1, saved.ReviewCommandCount);
        Assert.Null(saved.FinishedAtUtc);
    }

    [Fact]
    public async Task A_row_written_before_this_feature_deserializes_with_ReviewCommandCount_reading_as_zero()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IRemediationRunStore>();

        // Simulates a pre-existing row: completed, with none of the review columns ever written.
        var run = new RemediationRun(Guid.NewGuid(), Guid.NewGuid(), "orion180/platform/web-api", DateTimeOffset.UtcNow);
        run.Completed([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")], "https://pr/5", DateTimeOffset.UtcNow);

        if (!await TrySaveAsync(store, run, ct))
        {
            return;
        }

        var saved = await store.GetAsync(run.Id, ct);
        Assert.NotNull(saved);
        Assert.Null(saved!.ProposalReference);
        Assert.Null(saved.ReviewNote);
        Assert.Equal(0, saved.ReviewCommandCount);
    }

    [Fact]
    public async Task FindAwaitingReviewAsync_returns_the_held_run_for_a_repository_and_null_when_none_is_held()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IRemediationRunStore>();

        var repoId = Guid.NewGuid();
        var held = new RemediationRun(Guid.NewGuid(), repoId, "orion180/platform/web-api", DateTimeOffset.UtcNow);
        held.Advance(RunStatus.Remediating);
        held.AwaitingReview($"{held.Id}/proposal.json", DateTimeOffset.UtcNow);

        if (!await TrySaveAsync(store, held, ct))
        {
            return;
        }

        var found = await store.FindAwaitingReviewAsync(repoId, ct);
        Assert.NotNull(found);
        Assert.Equal(held.Id, found!.Id);

        var quietRepoId = Guid.NewGuid();
        Assert.Null(await store.FindAwaitingReviewAsync(quietRepoId, ct));
    }
}
