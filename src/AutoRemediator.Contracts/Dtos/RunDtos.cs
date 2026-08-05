namespace AutoRemediator.Contracts.Dtos;

/// <summary>A remediation run as shown in the runs feed.</summary>
public sealed record RunSummaryDto(
    Guid Id,
    string RepositorySlug,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    int UpdateCount,
    string? PullRequestUrl);

/// <summary>An update applied by a run, as shown on the run detail page.</summary>
public sealed record RunUpdateDto(
    string PackageId,
    string FromVersion,
    string ToVersion,
    string Kind,
    bool BeyondPolicy);

/// <summary>A remediation run's full detail.</summary>
public sealed record RunDetailDto(
    Guid Id,
    string RepositorySlug,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? PullRequestUrl,
    string? Error,
    IReadOnlyList<RunUpdateDto> Updates);
