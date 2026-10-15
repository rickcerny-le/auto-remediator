using System.Text.Json.Serialization;

namespace AutoRemediator.Contracts.Messages;

/// <summary>What caused a remediation run to be requested.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RemediationTrigger>))]
public enum RemediationTrigger
{
    /// <summary>The scheduler enqueued it. Also what a message without a trigger means.</summary>
    Scheduled,

    /// <summary>A person asked for it (Run now).</summary>
    Manual,
}

/// <summary>
/// Message published by the scheduler (one per enrolled repository) or by the API's Run now,
/// and consumed by the remediation worker to kick off a dependency-update run.
/// </summary>
/// <param name="RunId">Unique id for this remediation run.</param>
/// <param name="RepositoryId">Id of the <c>ManagedRepository</c> to process.</param>
/// <param name="Organization">Azure DevOps organization.</param>
/// <param name="Project">Azure DevOps project.</param>
/// <param name="RepositoryName">Repository name.</param>
/// <param name="RequestedAtUtc">When the run was requested.</param>
/// <param name="Trigger">What caused the request. Defaults to <see cref="RemediationTrigger.Scheduled"/> so messages already queued without it still read correctly.</param>
public sealed record RemediationRunRequested(
    Guid RunId,
    Guid RepositoryId,
    string Organization,
    string Project,
    string RepositoryName,
    DateTimeOffset RequestedAtUtc,
    RemediationTrigger Trigger = RemediationTrigger.Scheduled);
