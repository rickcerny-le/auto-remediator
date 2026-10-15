using AutoRemediator.Contracts;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Messaging;

namespace AutoRemediator.Infrastructure.Remediation;

/// <summary>The outcome of asking for a remediation run.</summary>
public abstract record RunRequestResult
{
    private RunRequestResult() { }

    /// <summary>A <see cref="RemediationRunRequested"/> was published for this run id.</summary>
    public sealed record Enqueued(Guid RunId) : RunRequestResult;

    /// <summary>Nothing was published: the repository is held for review by this run.</summary>
    public sealed record Held(Guid HeldRunId) : RunRequestResult;
}

/// <summary>
/// The one place a remediation run is requested, shared by the scheduler and the API's Run now so the
/// message shape cannot drift between them.
/// </summary>
public interface IRemediationRunRequester
{
    /// <summary>
    /// Publishes a <see cref="RemediationRunRequested"/> for the repository, unless it is held for review.
    /// </summary>
    /// <remarks>
    /// This does not check <see cref="ManagedRepository.Enabled"/>. Disabled means "not scheduled", not
    /// "off": a person may run a disabled repository on demand, and the scheduler filters to enabled
    /// repositories before calling. It also does not record <c>SkippedHeld</c>; that is the scheduler's
    /// bookkeeping. The held check is advisory, as for review commands: the worker does not re-check it.
    /// </remarks>
    Task<RunRequestResult> RequestAsync(
        ManagedRepository repo, RemediationTrigger trigger, CancellationToken cancellationToken = default);
}

public sealed class RemediationRunRequester(
    IRemediationRunStore runStore,
    IMessagePublisher publisher,
    TimeProvider timeProvider) : IRemediationRunRequester
{
    public async Task<RunRequestResult> RequestAsync(
        ManagedRepository repo, RemediationTrigger trigger, CancellationToken cancellationToken = default)
    {
        // A repository with an open proposal receives no new updates until it is resolved (FR-024).
        var held = await runStore.FindAwaitingReviewAsync(repo.Id, cancellationToken);
        if (held is not null)
        {
            return new RunRequestResult.Held(held.Id);
        }

        var message = new RemediationRunRequested(
            RunId: Guid.NewGuid(),
            RepositoryId: repo.Id,
            Organization: repo.Organization,
            Project: repo.Project,
            RepositoryName: repo.Name,
            RequestedAtUtc: timeProvider.GetUtcNow(),
            Trigger: trigger);

        await publisher.PublishAsync(RemediationQueues.RemediationRuns, message, cancellationToken);
        return new RunRequestResult.Enqueued(message.RunId);
    }
}
