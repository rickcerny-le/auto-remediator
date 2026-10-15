namespace AutoRemediator.Domain;

/// <summary>Where an effective value came from.</summary>
public enum SettingSource
{
    /// <summary>The global settings.</summary>
    Global,

    /// <summary>The repository's own settings.</summary>
    Repository,

    /// <summary>A list item present in both the global and repository lists.</summary>
    Both,

    /// <summary>The built-in default.</summary>
    Default,
}

/// <summary>A list entry with its origin.</summary>
public sealed record EffectiveItem(string Value, SettingSource Source);

/// <summary>A scalar with its origin.</summary>
public sealed record EffectiveValue<T>(T Value, SettingSource Source);

/// <summary>
/// The settings that actually apply to one repository, with a source for every value.
/// <see cref="Targeting"/> is ready to hand to the planner, analyzer and verification.
/// </summary>
public sealed record EffectiveSettings(
    TargetingSettings Targeting,
    IReadOnlyList<EffectiveItem> Patterns,
    IReadOnlyList<EffectiveItem> Excludes,
    IReadOnlyList<EffectiveItem> Feeds,
    IReadOnlyList<EffectiveItem> Ignore,
    EffectiveValue<UpdateStrategy> Strategy,
    EffectiveValue<bool> AllowPrerelease,
    EffectiveValue<string> Schedule,
    bool InheritGlobal,
    IReadOnlyList<string> Warnings);
