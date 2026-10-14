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
    RunRemediationDto? Remediation = null,
    string? ProposalUrl = null,
    string? ReviewNote = null,
    string? PullRequestDiffUrl = null);

/// <summary>
/// What the AI repair loop did for a run. Present only when the loop ran; an attempt count of zero
/// means it was entered but produced no attempt.
/// </summary>
public sealed record RunRemediationDto(int Attempts, string? TranscriptUrl);

/// <summary>One file in a held change proposal, with both sides of the comparison.</summary>
/// <param name="OriginalContent">Content at <c>BaseCommitId</c>; null when the file did not exist.</param>
/// <param name="Origin"><c>"Manifest"</c>, <c>"LockFile"</c> or <c>"AgentEdit"</c>.</param>
public sealed record ProposedFileDto(
    string Path,
    string? OriginalContent,
    string NewContent,
    string Origin);

/// <summary>
/// A verified, AI-repaired change held for review. Everything a person needs to decide, from
/// stored data only (FR-010).
/// </summary>
public sealed record ChangeProposalDto(
    Guid RunId,
    string RepositorySlug,
    string BaseCommitId,
    DateTimeOffset VerifiedAtUtc,
    int Attempts,
    string? TranscriptUrl,
    string? ReviewNote,
    int ReviewCommandCount,
    IReadOnlyList<ProposedFileDto> Files,
    IReadOnlyList<RunDiagnosticDto> ProvokingDiagnostics);


/// <summary>
/// A local pull request's changes, read-only. Present only in local repository mode.
/// </summary>
public sealed record PullRequestDiffDto(
    string SourceBranch,
    string TargetBranch,
    string BaseCommitId,
    string HeadCommitId,
    IReadOnlyList<PullRequestDiffFileDto> Files);

/// <summary>One changed file in a pull request diff.</summary>
/// <param name="Path">Rooted with <c>/</c>.</param>
/// <param name="ChangeKind"><c>"Added"</c>, <c>"Modified"</c> or <c>"Deleted"</c>.</param>
/// <param name="OriginalContent">Content at the base commit; null when added, binary, or absent.</param>
/// <param name="NewContent">Content at the head commit; null when deleted, binary, or absent.</param>
/// <param name="IsTruncated">A side was cut at the size cap.</param>
public sealed record PullRequestDiffFileDto(
    string Path,
    string ChangeKind,
    string? OriginalContent,
    string? NewContent,
    bool IsBinary,
    bool IsTruncated);
