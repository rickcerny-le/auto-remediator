using AutoRemediator.Shared;

namespace AutoRemediator.Domain;

/// <summary>Lifecycle status of a remediation run.</summary>
public enum RunStatus
{
    Reading,
    Analyzing,
    Applying,
    CreatingPr,
    Completed,
    NoUpdates,
    Failed,
}

/// <summary>Why a package was bumped.</summary>
public enum UpdateKind
{
    /// <summary>A matched package bumped by policy.</summary>
    Matched,

    /// <summary>A declared sibling bumped by intra-family alignment.</summary>
    Collateral,
}

/// <summary>A single package version change applied by a run.</summary>
public sealed record DependencyUpdate(
    string PackageId,
    string FromVersion,
    string ToVersion,
    UpdateKind Kind = UpdateKind.Matched,
    bool BeyondPolicy = false);

/// <summary>
/// A remediation run for one repository: bumps matched outdated packages and opens a
/// pull request. Tracks status, the applied updates, the resulting PR, and any error.
/// </summary>
public sealed class RemediationRun : Entity<Guid>
{
    private readonly List<DependencyUpdate> _updates = [];

    public RemediationRun(Guid id, Guid repositoryId, string repositorySlug, DateTimeOffset startedAtUtc)
        : base(id)
    {
        RepositoryId = repositoryId;
        RepositorySlug = Guard.AgainstNullOrWhiteSpace(repositorySlug);
        StartedAtUtc = startedAtUtc;
        Status = RunStatus.Reading;
    }

    public Guid RepositoryId { get; }
    public string RepositorySlug { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset? FinishedAtUtc { get; private set; }
    public RunStatus Status { get; private set; }
    public IReadOnlyList<DependencyUpdate> Updates => _updates;
    public string? PullRequestUrl { get; private set; }
    public string? Error { get; private set; }

    public void Advance(RunStatus status) => Status = status;

    public void Completed(IEnumerable<DependencyUpdate> updates, string pullRequestUrl, DateTimeOffset finishedAtUtc)
    {
        _updates.Clear();
        _updates.AddRange(updates);
        PullRequestUrl = Guard.AgainstNullOrWhiteSpace(pullRequestUrl);
        Status = RunStatus.Completed;
        FinishedAtUtc = finishedAtUtc;
    }

    public void NoUpdates(DateTimeOffset finishedAtUtc)
    {
        Status = RunStatus.NoUpdates;
        FinishedAtUtc = finishedAtUtc;
    }

    public void Failed(string error, DateTimeOffset finishedAtUtc)
    {
        Error = error;
        Status = RunStatus.Failed;
        FinishedAtUtc = finishedAtUtc;
    }

    /// <summary>Rehydrates a run from persistence.</summary>
    public static RemediationRun Restore(
        Guid id,
        Guid repositoryId,
        string repositorySlug,
        RunStatus status,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? finishedAtUtc,
        IEnumerable<DependencyUpdate> updates,
        string? pullRequestUrl,
        string? error)
    {
        var run = new RemediationRun(id, repositoryId, repositorySlug, startedAtUtc)
        {
            Status = status,
            FinishedAtUtc = finishedAtUtc,
            PullRequestUrl = pullRequestUrl,
            Error = error,
        };
        run._updates.AddRange(updates);
        return run;
    }
}
