namespace AutoRemediator.Domain;

/// <summary>Status of a matched package relative to the latest resolved version.</summary>
public enum DependencyStatus
{
    /// <summary>Pinned version is behind the latest available.</summary>
    Outdated,

    /// <summary>Pinned version equals the latest available.</summary>
    UpToDate,

    /// <summary>Version could not be determined (unresolved manifest version or feed lookup failure).</summary>
    Unknown,
}

/// <summary>One row of the dependency map: a matched package in a repository.</summary>
public sealed record DependencyMapEntry(
    string RepositorySlug,
    string PackageId,
    string? CurrentVersion,
    string? LatestVersion,
    DependencyStatus Status);

/// <summary>The aggregate outdated-dependency view across configured repositories.</summary>
public sealed record DependencyMap(IReadOnlyList<DependencyMapEntry> Entries)
{
    public static DependencyMap Empty => new([]);
}
