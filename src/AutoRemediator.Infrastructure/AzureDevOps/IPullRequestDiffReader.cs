namespace AutoRemediator.Infrastructure.AzureDevOps;

/// <summary>
/// Reads what a pull request changes, so the UI can show it without leaving the browser. Only the local
/// repository mode has an implementation that returns data; on the Azure DevOps path the reader is
/// unavailable and the link to it is never emitted.
/// </summary>
public interface IPullRequestDiffReader
{
    /// <summary>True when this host can compute pull request diffs.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The diff for the pull request at <paramref name="pullRequestUrl"/>, or null when the URL is not one this
    /// reader understands, the record is unreadable, or its commits no longer exist. Never throws for those.
    /// </summary>
    Task<PullRequestDiff?> GetDiffAsync(string pullRequestUrl, CancellationToken cancellationToken);
}

/// <summary>How a file changed between the pull request's base and head.</summary>
public enum PullRequestChangeKind
{
    Added,
    Modified,
    Deleted,
}

/// <summary>A pull request's changes: the branches, both commit ids and the changed files (capped).</summary>
public sealed record PullRequestDiff(
    string SourceBranch,
    string TargetBranch,
    string BaseCommitId,
    string HeadCommitId,
    IReadOnlyList<PullRequestDiffFile> Files);

/// <summary>
/// One changed file with both sides of the comparison. <paramref name="Path"/> is rooted with <c>/</c>.
/// Contents are null for the side that does not exist and for binary files; <paramref name="IsTruncated"/>
/// is set when a side was cut at the size cap.
/// </summary>
public sealed record PullRequestDiffFile(
    string Path,
    PullRequestChangeKind ChangeKind,
    string? OriginalContent,
    string? NewContent,
    bool IsBinary,
    bool IsTruncated);
