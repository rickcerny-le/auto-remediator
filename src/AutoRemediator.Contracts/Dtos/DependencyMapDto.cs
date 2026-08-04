namespace AutoRemediator.Contracts.Dtos;

/// <summary>API/UI-facing row of the dependency map.</summary>
/// <param name="RepositorySlug">"org/project/repo".</param>
/// <param name="PackageId">Matched package id.</param>
/// <param name="CurrentVersion">Version pinned in the repo (null if unresolved).</param>
/// <param name="LatestVersion">Latest version from the feed (null if unresolved).</param>
/// <param name="Status">"Outdated" | "UpToDate" | "Unknown".</param>
public sealed record DependencyMapEntryDto(
    string RepositorySlug,
    string PackageId,
    string? CurrentVersion,
    string? LatestVersion,
    string Status);

/// <summary>The aggregate dependency map returned by the API.</summary>
/// <param name="Entries">All matched-package rows across configured repositories.</param>
public sealed record DependencyMapDto(IReadOnlyList<DependencyMapEntryDto> Entries);
