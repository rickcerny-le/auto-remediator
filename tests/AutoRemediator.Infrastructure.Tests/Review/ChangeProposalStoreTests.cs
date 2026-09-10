using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Review;
using AutoRemediator.Infrastructure.Storage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoRemediator.Infrastructure.Tests.Review;

/// <summary>Round-trips a change proposal through blob storage; skips when Azurite is not running.</summary>
public class ChangeProposalStoreTests
{
    private static readonly DateTimeOffset VerifiedAt = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    private static ServiceProvider BuildProvider()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=k;SharedAccessKey=a2V5a2V5a2V5a2V5";
        builder.AddInfrastructure();
        return builder.Services.BuildServiceProvider();
    }

    private static ChangeProposal NewProposal(Guid? runId = null, Guid? repositoryId = null) => new(
        runId ?? Guid.NewGuid(),
        repositoryId ?? Guid.NewGuid(),
        baseCommitId: "abc123",
        verifiedAtUtc: VerifiedAt,
        files:
        [
            new ProposedFile("/src/Foo/Foo.csproj", "<Old/>", "<New/>", ProposedFileOrigin.Manifest),
            new ProposedFile("/src/Foo/Bar.cs", "old", "new", ProposedFileOrigin.AgentEdit),
        ],
        provokingDiagnostics: [new VerificationDiagnostic("CS0117", "missing member", "src/Foo/Bar.cs", 42, 17)],
        attempts: 2,
        transcriptReference: "runs/abc/remediation-transcript.md");

    [Fact]
    public async Task Proposal_round_trips_through_blob_storage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IChangeProposalStore>();

        var runId = Guid.NewGuid();
        var proposal = NewProposal(runId);

        string reference;
        try
        {
            reference = await store.StoreAsync(proposal, ct);
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            Assert.Skip($"Azurite storage emulator not reachable: {ex.GetType().Name}");
            return;
        }

        Assert.Contains(runId.ToString(), reference);

        var read = await store.ReadAsync(reference, ct);

        Assert.NotNull(read);
        Assert.Equal(proposal.RunId, read!.RunId);
        Assert.Equal(proposal.RepositoryId, read.RepositoryId);
        Assert.Equal(proposal.BaseCommitId, read.BaseCommitId);
        Assert.Equal(proposal.VerifiedAtUtc, read.VerifiedAtUtc);
        Assert.Equal(proposal.Attempts, read.Attempts);
        Assert.Equal(proposal.TranscriptReference, read.TranscriptReference);
        Assert.Equal(proposal.Files.Count, read.Files.Count);
        Assert.Equal(proposal.Files[0].Path, read.Files[0].Path);
        Assert.Equal(proposal.Files[0].OriginalContent, read.Files[0].OriginalContent);
        Assert.Equal(proposal.Files[1].Origin, read.Files[1].Origin);
        Assert.Single(read.ProvokingDiagnostics);
        Assert.Equal("CS0117", read.ProvokingDiagnostics[0].Code);
    }

    [Fact]
    public async Task An_unknown_reference_reads_as_null()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IChangeProposalStore>();

        try
        {
            // Force the container to exist so a missing blob is distinguishable from a missing emulator.
            await store.StoreAsync(NewProposal(), ct);
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            Assert.Skip($"Azurite storage emulator not reachable: {ex.GetType().Name}");
            return;
        }

        Assert.Null(await store.ReadAsync($"{Guid.NewGuid()}/proposal.json", ct));
    }

    [Fact]
    public async Task A_failing_write_propagates_rather_than_returning_null()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new ChangeProposalStore(new ThrowingBlobStore(), NullLogger<ChangeProposalStore>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StoreAsync(NewProposal(), ct));
    }

    private sealed class ThrowingBlobStore : IBlobStore
    {
        public Task<BlobContainerClient> GetContainerAsync(string containerName, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("storage is unavailable");
    }
}
