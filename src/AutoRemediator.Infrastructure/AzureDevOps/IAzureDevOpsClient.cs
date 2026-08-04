using AutoRemediator.Domain;

namespace AutoRemediator.Infrastructure.AzureDevOps;

/// <summary>A file read from a repository.</summary>
public sealed record RepositoryFile(string Path, string Content);

/// <summary>
/// Read-only Azure DevOps access: verify a repository exists and read its dependency
/// manifests from the default/target branch. Write operations (branch/commit/PR) are
/// intentionally out of scope in this slice.
/// </summary>
public interface IAzureDevOpsClient
{
    Task<bool> RepositoryExistsAsync(ManagedRepository repository, CancellationToken cancellationToken = default);

    /// <summary>Returns the contents of the dependency manifests (`Directory.Packages.props`, `*.csproj`).</summary>
    Task<IReadOnlyList<RepositoryFile>> GetManifestsAsync(ManagedRepository repository, CancellationToken cancellationToken = default);
}
