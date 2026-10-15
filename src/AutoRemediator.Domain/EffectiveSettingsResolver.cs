namespace AutoRemediator.Domain;

/// <summary>Built-in fallbacks used when nothing else is configured.</summary>
public static class BuiltInDefaults
{
    /// <summary>Daily at 00:00 UTC.</summary>
    public const string Schedule = "0 0 * * *";
}

/// <summary>
/// Pure, I/O-free and time-free resolution of the settings that apply to one repository.
/// <para>
/// Inheritance on (<see cref="RepositorySettings.InheritGlobal"/> true):
/// each list is the global entries in order, then the repository entries not already present
/// (ordinal, ignore case; first spelling kept). An item in both lists has source Both, otherwise
/// Global or Repository. Strategy and pre-release are the repository value (Repository) when
/// non-null, otherwise the global value (Global).
/// </para>
/// <para>
/// Inheritance off: lists contain only the repository's entries (all Repository). Strategy and
/// pre-release are the repository value (Repository) when non-null, otherwise the
/// <see cref="UpdatePolicy.Default"/> value (Default).
/// </para>
/// <para>
/// Schedule (either mode, the switch does not apply): the repository value (Repository), else
/// <see cref="TargetingSettings.DefaultSchedule"/> (Global), else <see cref="BuiltInDefaults.Schedule"/>
/// (Default).
/// </para>
/// <para>
/// Warnings: no effective patterns, or effective patterns but no feeds.
/// </para>
/// </summary>
public static class EffectiveSettingsResolver
{
    public const string NoPatternsWarning = "No package patterns: nothing will be updated for this repository.";
    public const string NoFeedsWarning = "No feeds: package versions cannot be resolved.";

    public static EffectiveSettings Resolve(TargetingSettings global, RepositorySettings repo)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(repo);

        var inherit = repo.InheritGlobal;
        var patterns = Merge(inherit ? global.Patterns : [], repo.Patterns);
        var excludes = Merge(inherit ? global.Excludes : [], repo.Excludes);
        var feeds = Merge(inherit ? global.Feeds : [], repo.Feeds);
        var ignore = Merge(inherit ? global.Policy.Ignore : [], repo.Ignore);

        var fallbackPolicy = inherit ? global.Policy : UpdatePolicy.Default;
        var fallbackSource = inherit ? SettingSource.Global : SettingSource.Default;

        var strategy = repo.Strategy is { } s
            ? new EffectiveValue<UpdateStrategy>(s, SettingSource.Repository)
            : new EffectiveValue<UpdateStrategy>(fallbackPolicy.Strategy, fallbackSource);
        var prerelease = repo.AllowPrerelease is { } p
            ? new EffectiveValue<bool>(p, SettingSource.Repository)
            : new EffectiveValue<bool>(fallbackPolicy.AllowPrerelease, fallbackSource);

        var schedule = repo.Schedule is not null
            ? new EffectiveValue<string>(repo.Schedule, SettingSource.Repository)
            : global.DefaultSchedule is not null
                ? new EffectiveValue<string>(global.DefaultSchedule, SettingSource.Global)
                : new EffectiveValue<string>(BuiltInDefaults.Schedule, SettingSource.Default);

        var warnings = new List<string>();
        if (patterns.Count == 0)
        {
            warnings.Add(NoPatternsWarning);
        }
        else if (feeds.Count == 0)
        {
            warnings.Add(NoFeedsWarning);
        }

        var targeting = new TargetingSettings(
            Values(patterns),
            Values(excludes),
            Values(feeds),
            new UpdatePolicy(strategy.Value, Values(ignore), prerelease.Value));

        return new EffectiveSettings(
            targeting, patterns, excludes, feeds, ignore, strategy, prerelease, schedule, inherit, warnings);
    }

    private static IReadOnlyList<string> Values(IReadOnlyList<EffectiveItem> items) =>
        items.Select(i => i.Value).ToArray();

    private static IReadOnlyList<EffectiveItem> Merge(IReadOnlyList<string> global, IReadOnlyList<string> repo)
    {
        // Global entries are sources Global; repo entries Repository, or Both when already present.
        var result = new List<(string Value, SettingSource Source)>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in global)
        {
            if (index.TryAdd(value, result.Count))
            {
                result.Add((value, SettingSource.Global));
            }
        }

        foreach (var value in repo)
        {
            if (index.TryGetValue(value, out var at))
            {
                if (result[at].Source == SettingSource.Global)
                {
                    result[at] = (result[at].Value, SettingSource.Both);
                }
            }
            else
            {
                index.Add(value, result.Count);
                result.Add((value, SettingSource.Repository));
            }
        }

        return result.Select(r => new EffectiveItem(r.Value, r.Source)).ToArray();
    }
}
