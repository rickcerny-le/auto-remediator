namespace AutoRemediator.Domain;

/// <summary>
/// Per-repository layer on top of the global <see cref="TargetingSettings"/>. Lists add to
/// (or, with <see cref="InheritGlobal"/> off, replace) the global lists; scalars override when set.
/// Resolved by <see cref="EffectiveSettingsResolver"/>.
/// </summary>
public sealed record RepositorySettings
{
    public RepositorySettings(
        bool inheritGlobal = true,
        IReadOnlyList<string>? patterns = null,
        IReadOnlyList<string>? excludes = null,
        IReadOnlyList<string>? feeds = null,
        IReadOnlyList<string>? ignore = null,
        UpdateStrategy? strategy = null,
        bool? allowPrerelease = null,
        string? schedule = null)
    {
        InheritGlobal = inheritGlobal;
        Patterns = Clean(patterns);
        Excludes = Clean(excludes);
        Feeds = Clean(feeds);
        Ignore = Clean(ignore);
        Strategy = strategy;
        AllowPrerelease = allowPrerelease;
        Schedule = string.IsNullOrWhiteSpace(schedule) ? null : schedule.Trim();
    }

    /// <summary>
    /// When true (default) lists are unioned with the global ones and unset scalars fall back to
    /// the global values. When false only this repository's targeting fields apply. The schedule
    /// is not affected by this switch.
    /// </summary>
    public bool InheritGlobal { get; }

    public IReadOnlyList<string> Patterns { get; }
    public IReadOnlyList<string> Excludes { get; }
    public IReadOnlyList<string> Feeds { get; }
    public IReadOnlyList<string> Ignore { get; }

    /// <summary>Null = not overridden.</summary>
    public UpdateStrategy? Strategy { get; }

    /// <summary>Null = not overridden.</summary>
    public bool? AllowPrerelease { get; }

    /// <summary>Cron expression (5 fields, UTC); null = use the global default schedule.</summary>
    public string? Schedule { get; }

    /// <summary>Everything inherited: no additions, no overrides.</summary>
    public static RepositorySettings Inherit => new();

    /// <summary>True when any list is non-empty or any scalar is set.</summary>
    public bool HasOverrides =>
        Patterns.Count > 0 || Excludes.Count > 0 || Feeds.Count > 0 || Ignore.Count > 0
        || Strategy is not null || AllowPrerelease is not null || Schedule is not null;

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? values) =>
        values is null
            ? []
            : values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToArray();
}
