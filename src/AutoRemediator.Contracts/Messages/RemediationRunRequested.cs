namespace AutoRemediator.Contracts.Messages;

/// <summary>
/// Message published by the scheduler (one per enrolled repository) and consumed
/// by the remediation worker to kick off a dependency-update run.
/// </summary>
/// <param name="RunId">Unique id for this remediation run.</param>
/// <param name="RepositoryId">Id of the <c>ManagedRepository</c> to process.</param>
/// <param name="Organization">Azure DevOps organization.</param>
/// <param name="Project">Azure DevOps project.</param>
/// <param name="RepositoryName">Repository name.</param>
/// <param name="RequestedAtUtc">When the run was requested.</param>
public sealed record RemediationRunRequested(
    Guid RunId,
    Guid RepositoryId,
    string Organization,
    string Project,
    string RepositoryName,
    DateTimeOffset RequestedAtUtc);
