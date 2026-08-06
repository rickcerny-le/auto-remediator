using AutoRemediator.Domain;

namespace AutoRemediator.Infrastructure.AzureDevOps;

/// <summary>A file read from a repository.</summary>
public sealed record RepositoryFile(string Path, string Content);

/// <summary>A file edit to push (path as returned by the manifest reads, content = new text).</summary>
public sealed record FileChange(string Path, string Content);

/// <summary>
/// Azure DevOps access over the REST API. Reads a repository's manifests and performs the
/// write operations needed for remediation — pushing a commit to a branch and creating or
/// finding a pull request — without cloning the repository.
/// </summary>
public interface IAzureDevOpsClient
{
    Task<bool> RepositoryExistsAsync(ManagedRepository repository, CancellationToken cancellationToken = default);

    /// <summary>Returns the contents of the dependency manifests (`Directory.Packages.props`, `*.csproj`).</summary>
    Task<IReadOnlyList<RepositoryFile>> GetManifestsAsync(ManagedRepository repository, CancellationToken cancellationToken = default);

    /// <summary>Latest commit id of <paramref name="branch"/>, or null if the branch does not exist.</summary>
    Task<string?> GetBranchHeadAsync(ManagedRepository repository, string branch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the repository's full tree at <paramref name="commitId"/> as a zip archive, for local
    /// verification. Requires no `git` binary and no credential beyond the PAT used for reads. Git LFS
    /// and submodules are not included by the archive and so are unsupported. Throws when the request
    /// fails, so the caller can classify verification as skipped rather than silently verifying nothing.
    /// </summary>
    Task<Stream> GetRepositoryArchiveAsync(
        ManagedRepository repository,
        string commitId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes <paramref name="changes"/> as a single commit to <paramref name="branch"/>, updating
    /// the ref from <paramref name="baseCommitId"/> (the branch head, or the target branch head when
    /// creating the branch).
    /// </summary>
    Task PushFilesAsync(
        ManagedRepository repository,
        string branch,
        string baseCommitId,
        IReadOnlyList<FileChange> changes,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the active PR from <paramref name="sourceBranch"/> into <paramref name="targetBranch"/>, creating one if none exists. Returns its web URL.</summary>
    Task<string> EnsurePullRequestAsync(
        ManagedRepository repository,
        string sourceBranch,
        string targetBranch,
        string title,
        string description,
        CancellationToken cancellationToken = default);
}
