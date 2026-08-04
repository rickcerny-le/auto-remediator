namespace AutoRemediator.Contracts.Dtos;

/// <summary>A managed repository as exposed by the API/UI.</summary>
public sealed record ManagedRepositoryDto(
    Guid Id,
    string Organization,
    string Project,
    string Name,
    bool Enabled,
    string TargetBranch);

/// <summary>Create/update payload for a managed repository (id assigned by the server on create).</summary>
public sealed record ManagedRepositoryInput(
    string Organization,
    string Project,
    string Name,
    bool Enabled = true,
    string TargetBranch = "main");

/// <summary>Global targeting settings as exposed by the API/UI.</summary>
public sealed record TargetingSettingsDto(
    IReadOnlyList<string> Patterns,
    IReadOnlyList<string> Excludes,
    IReadOnlyList<string> Feeds,
    string Strategy,
    IReadOnlyList<string> Ignore,
    bool AllowPrerelease);
