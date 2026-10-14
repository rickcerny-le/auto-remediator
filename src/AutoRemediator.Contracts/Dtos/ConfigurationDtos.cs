namespace AutoRemediator.Contracts.Dtos;

/// <summary>A managed repository as exposed by the API/UI.</summary>
public sealed record ManagedRepositoryDto(
    Guid Id,
    string Organization,
    string Project,
    string Name,
    bool Enabled,
    string TargetBranch,
    RepositorySettingsDto? Settings = null);

/// <summary>Create/update payload for a managed repository (id assigned by the server on create).</summary>
public sealed record ManagedRepositoryInput(
    string Organization,
    string Project,
    string Name,
    bool Enabled = true,
    string TargetBranch = "main",
    RepositorySettingsDto? Settings = null);

/// <summary>Global targeting settings as exposed by the API/UI.</summary>
public sealed record TargetingSettingsDto(
    IReadOnlyList<string> Patterns,
    IReadOnlyList<string> Excludes,
    IReadOnlyList<string> Feeds,
    string Strategy,
    IReadOnlyList<string> Ignore,
    bool AllowPrerelease,
    string? DefaultSchedule = null);

/// <summary>
/// Per-repository settings layered over the global ones. Null scalars are inherited;
/// <paramref name="Schedule"/> is a 5-field UTC cron expression.
/// </summary>
public sealed record RepositorySettingsDto(
    bool InheritGlobal,
    IReadOnlyList<string> Patterns,
    IReadOnlyList<string> Excludes,
    IReadOnlyList<string> Feeds,
    IReadOnlyList<string> Ignore,
    string? Strategy,
    bool? AllowPrerelease,
    string? Schedule);

/// <summary>An effective list entry; <paramref name="Source"/> is Global, Repository or Both.</summary>
public sealed record EffectiveListItemDto(string Value, string Source);

/// <summary>An effective scalar; <paramref name="Source"/> is Global, Repository or Default.</summary>
public sealed record EffectiveValueDto(string Value, string Source);

/// <summary>The settings that actually apply to one repository, with a source for every value.</summary>
public sealed record EffectiveSettingsDto(
    Guid RepositoryId,
    bool InheritGlobal,
    IReadOnlyList<EffectiveListItemDto> Patterns,
    IReadOnlyList<EffectiveListItemDto> Excludes,
    IReadOnlyList<EffectiveListItemDto> Feeds,
    IReadOnlyList<EffectiveListItemDto> Ignore,
    EffectiveValueDto Strategy,
    EffectiveValueDto AllowPrerelease,
    EffectiveValueDto Schedule,
    IReadOnlyList<string> Warnings);
