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

/// <summary>A diagnostic from local verification, as shown on the run detail page.</summary>
public sealed record RunDiagnosticDto(
    string Code,
    string Message,
    string? Path,
    int? Line,
    int? Column);

/// <summary>
/// The local verification result for a run: `Verified`, `DependencyFailure` or `Skipped`, with the
/// reason when skipped and a link to the full log.
/// </summary>
public sealed record RunVerificationDto(
    string Classification,
    string? SkipReason,
    IReadOnlyList<RunDiagnosticDto> Diagnostics,
    string? LogUrl);

/// <summary>A remediation run's full detail.</summary>
public sealed record RunDetailDto(
    Guid Id,
    string RepositorySlug,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? PullRequestUrl,
    string? Error,
    IReadOnlyList<RunUpdateDto> Updates,
    RunVerificationDto? Verification = null,
    RunRemediationDto? Remediation = null);

/// <summary>
/// What the AI repair loop did for a run. Present only when the loop ran; an attempt count of zero
/// means it was entered but produced no attempt.
/// </summary>
public sealed record RunRemediationDto(int Attempts, string? TranscriptUrl);
