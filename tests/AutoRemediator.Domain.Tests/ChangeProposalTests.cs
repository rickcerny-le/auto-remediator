using AutoRemediator.Domain;

namespace AutoRemediator.Domain.Tests;

public class ChangeProposalTests
{
    private static readonly DateTimeOffset VerifiedAt = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    private static ProposedFile ManifestFile() =>
        new("/src/Foo/Foo.csproj", "<Old/>", "<New/>", ProposedFileOrigin.Manifest);

    private static ProposedFile AgentFile() =>
        new("/src/Foo/Bar.cs", "old", "new", ProposedFileOrigin.AgentEdit);

    private static ChangeProposal NewProposal(IReadOnlyList<ProposedFile>? files = null, int attempts = 2) =>
        new(
            runId: Guid.NewGuid(),
            repositoryId: Guid.NewGuid(),
            baseCommitId: "abc123",
            verifiedAtUtc: VerifiedAt,
            files: files ?? [ManifestFile(), AgentFile()],
            provokingDiagnostics: [new VerificationDiagnostic("CS0117", "missing member")],
            attempts: attempts,
            transcriptReference: "runs/abc/transcript.log");

    [Fact]
    public void Valid_proposal_constructs()
    {
        var proposal = NewProposal();

        Assert.Equal("abc123", proposal.BaseCommitId);
        Assert.Equal(2, proposal.Files.Count);
    }

    [Fact]
    public void Files_must_be_non_empty()
    {
        Assert.Throws<ArgumentException>(() => NewProposal(files: []));
    }

    [Fact]
    public void At_least_one_file_must_be_an_agent_edit()
    {
        Assert.Throws<ArgumentException>(() => NewProposal(files: [ManifestFile()]));
    }

    [Fact]
    public void Attempts_must_be_greater_than_zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewProposal(attempts: 0));
    }

    [Fact]
    public void Base_commit_id_must_be_non_empty()
    {
        Assert.Throws<ArgumentException>(() => new ChangeProposal(
            Guid.NewGuid(), Guid.NewGuid(), "  ", VerifiedAt,
            [ManifestFile(), AgentFile()], [], 1, null));
    }

    [Fact]
    public void Paths_within_files_must_be_unique()
    {
        var duplicate = new ProposedFile("/src/Foo/Bar.cs", "old", "new-2", ProposedFileOrigin.AgentEdit);

        Assert.Throws<ArgumentException>(() => NewProposal(files: [AgentFile(), duplicate]));
    }

    [Fact]
    public void AgentEdits_and_ManifestEdits_are_derived_by_origin()
    {
        var lockFile = new ProposedFile("/packages.lock.json", "{}", "{\"v\":2}", ProposedFileOrigin.LockFile);
        var proposal = NewProposal(files: [ManifestFile(), AgentFile(), lockFile]);

        Assert.Single(proposal.AgentEdits);
        Assert.Equal("/src/Foo/Bar.cs", proposal.AgentEdits[0].Path);

        Assert.Single(proposal.ManifestEdits);
        Assert.Equal("/src/Foo/Foo.csproj", proposal.ManifestEdits[0].Path);
    }

    [Fact]
    public void TranscriptReference_may_be_null()
    {
        var proposal = new ChangeProposal(
            Guid.NewGuid(), Guid.NewGuid(), "abc123", VerifiedAt,
            [ManifestFile(), AgentFile()], [], 1, null);

        Assert.Null(proposal.TranscriptReference);
    }
}
