namespace AutoRemediator.Domain;

/// <summary>How aggressively updates may move a version.</summary>
public enum UpdateStrategy
{
    Patch,
    Minor,
    Major,
}

/// <summary>Update policy applied across targeted packages.</summary>
/// <param name="Strategy">Maximum version jump allowed.</param>
/// <param name="Ignore">Package ids (or globs) to never update.</param>
/// <param name="AllowPrerelease">Whether pre-release versions may be considered "latest".</param>
public sealed record UpdatePolicy(
    UpdateStrategy Strategy,
    IReadOnlyList<string> Ignore,
    bool AllowPrerelease = false)
{
    public static UpdatePolicy Default => new(UpdateStrategy.Minor, [], AllowPrerelease: false);
}

/// <summary>
/// Global targeting settings: which packages to act on (patterns/excludes), where
/// "latest" is resolved (feeds), and the update policy. A single instance governs
/// all managed repositories (per-repo override may be layered on later).
/// </summary>
public sealed class TargetingSettings
{
    public TargetingSettings(
        IReadOnlyList<string> patterns,
        IReadOnlyList<string>? excludes = null,
        IReadOnlyList<string>? feeds = null,
        UpdatePolicy? policy = null)
    {
        Patterns = patterns ?? [];
        Excludes = excludes ?? [];
        Feeds = feeds ?? [];
        Policy = policy ?? UpdatePolicy.Default;
    }

    /// <summary>Package-id globs to target, e.g. <c>Orion180.*</c>.</summary>
    public IReadOnlyList<string> Patterns { get; }

    /// <summary>Package-id globs to exclude from the matched set.</summary>
    public IReadOnlyList<string> Excludes { get; }

    /// <summary>Feed URLs used to resolve the latest version of matched packages.</summary>
    public IReadOnlyList<string> Feeds { get; }

    public UpdatePolicy Policy { get; }

    public static TargetingSettings Empty => new([]);
}
