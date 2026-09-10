using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Remediation;
using AutoRemediator.Infrastructure.Verification;
using Microsoft.Extensions.Logging;

namespace AutoRemediator.Infrastructure.Review;

/// <summary>
/// Executes the four review commands against a held proposal. Runs in the worker, which owns the
/// Azure DevOps client — the API only enqueues (FR-013).
/// </summary>
public interface IReviewCommandHandler
{
    Task HandleAsync(Guid runId, ReviewCommand command, CancellationToken cancellationToken = default);
}

internal sealed class ReviewCommandHandler(
    IRemediationRunStore runStore,
    IChangeProposalStore proposals,
    IManagedRepositoryStore repositories,
    ITargetingSettingsStore settingsStore,
    IVerificationService verification,
    IRemediationLoop remediationLoop,
    IRemediationAgent agent,
    IVerificationLogStore logs,
    IAzureDevOpsClient azureDevOps,
    TimeProvider timeProvider,
    ILogger<ReviewCommandHandler> logger) : IReviewCommandHandler
{
    public async Task HandleAsync(Guid runId, ReviewCommand command, CancellationToken cancellationToken = default)
    {
        var run = await runStore.GetAsync(runId, cancellationToken);
        if (run is null)
        {
            logger.LogWarning("Review command {Command} for unknown run {RunId}: nothing to act on.", command, runId);
            return;
        }

        if (run.Status != RunStatus.AwaitingReview)
        {
            // Not a failure: the run has already moved on (FR-020). The worker's single-consumer
            // queue is what makes this refusal race-free — see contracts/review-messages.md Decision 7.
            logger.LogInformation(
                "Review command {Command} refused for run {RunId}: status is {Status}, not AwaitingReview.",
                command, runId, run.Status);
            return;
        }

        var proposal = run.ProposalReference is null
            ? null
            : await proposals.ReadAsync(run.ProposalReference, cancellationToken);

        if (proposal is null)
        {
            logger.LogWarning(
                "Review command {Command} for run {RunId}: the proposal at {Reference} could not be read.",
                command, runId, run.ProposalReference);
            run.ReviewBlocked("the stored proposal could not be read");
            await runStore.SaveAsync(run, cancellationToken);
            return;
        }

        switch (command)
        {
            case ReviewCommand.Approve:
                await ApproveAsync(run, proposal, cancellationToken);
                break;
            case ReviewCommand.Rebuild:
                await RebuildAsync(run, proposal, cancellationToken);
                break;
            case ReviewCommand.Retry:
                await RetryAsync(run, proposal, cancellationToken);
                break;
            case ReviewCommand.Discard:
                run.Discarded(timeProvider.GetUtcNow());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, "Unrecognized review command.");
        }

        await runStore.SaveAsync(run, cancellationToken);
    }

    /// <summary>Pushes the stored change set with no re-verification (FR-016) and no tree materialized.</summary>
    private async Task ApproveAsync(RemediationRun run, ChangeProposal proposal, CancellationToken cancellationToken)
    {
        var repo = await GetRepositoryAsync(run, cancellationToken);
        var changes = proposal.Files.Select(f => new FileChange(f.Path, f.NewContent)).ToList();

        try
        {
            await azureDevOps.PushFilesAsync(
                repo, RemediationRunner.UpdateBranch, proposal.BaseCommitId, changes,
                PullRequestContent.CommitMessage(run.Updates.Count), cancellationToken);
        }
        catch (PushRejectedException ex)
        {
            run.ReviewBlocked($"The update branch moved since this proposal was verified ({ex.TypeKey}); rebuild to try again.");
            return;
        }

        var description = PullRequestContent.Description(
            run.Updates, VerificationOutcome.Verified(), proposal.Attempts,
            (proposal.VerifiedAtUtc, proposal.BaseCommitId));

        var prUrl = await azureDevOps.EnsurePullRequestAsync(
            repo, RemediationRunner.UpdateBranch, repo.TargetBranch,
            PullRequestContent.Title(run.Updates.Count), description, cancellationToken);

        run.Completed(run.Updates, prUrl, timeProvider.GetUtcNow());
    }

    /// <summary>Re-verifies at the current branch head, replaying every recorded edit. Replaces the proposal only on success.</summary>
    private async Task RebuildAsync(RemediationRun run, ChangeProposal proposal, CancellationToken cancellationToken)
    {
        var repo = await GetRepositoryAsync(run, cancellationToken);
        var settings = await settingsStore.GetAsync(cancellationToken);

        var currentHead = await azureDevOps.GetBranchHeadAsync(repo, RemediationRunner.UpdateBranch, cancellationToken)
                          ?? await azureDevOps.GetBranchHeadAsync(repo, repo.TargetBranch, cancellationToken)
                          ?? throw new InvalidOperationException($"Could not resolve head of target branch '{repo.TargetBranch}'.");

        var plan = new RepositoryUpdatePlan(run.Updates, ManifestEdits(proposal));

        using var session = await verification.OpenAsync(run.Id, repo, currentHead, plan, settings, cancellationToken);

        if (session.Workspace is { } workspace)
        {
            foreach (var edit in proposal.AgentEdits)
            {
                await workspace.ApplyProposedEditAsync(new ProposedEdit(edit.Path, edit.NewContent), cancellationToken);
            }
        }

        var verified = await session.VerifyAsync(cancellationToken);

        if (!verified.Outcome.IsVerified)
        {
            run.ReviewBlocked("rebuild did not verify against the current branch head; the previous proposal is unchanged");
            return;
        }

        var files = session.Workspace is null ? [] : await session.Workspace.ProposedFilesAsync(cancellationToken);
        var rebuilt = new ChangeProposal(
            run.Id, run.RepositoryId, currentHead, timeProvider.GetUtcNow(),
            files, proposal.ProvokingDiagnostics, proposal.Attempts, proposal.TranscriptReference);

        var reference = await proposals.StoreAsync(rebuilt, cancellationToken);
        run.ProposalReplaced(reference, timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Re-verifies at the proposal's stored base commit, dropping the recorded agent edits and
    /// running the loop again from the diagnostics that provoked the original repair (FR-018).
    /// </summary>
    private async Task RetryAsync(RemediationRun run, ChangeProposal proposal, CancellationToken cancellationToken)
    {
        if (!agent.IsAvailable)
        {
            run.ReviewBlocked("no model is configured, so retry could not run");
            return;
        }

        var repo = await GetRepositoryAsync(run, cancellationToken);
        var settings = await settingsStore.GetAsync(cancellationToken);

        var plan = new RepositoryUpdatePlan(run.Updates, ManifestEdits(proposal));

        using var session = await verification.OpenAsync(run.Id, repo, proposal.BaseCommitId, plan, settings, cancellationToken);
        var rejected = new VerificationResult(VerificationOutcome.DependencyFailure(proposal.ProvokingDiagnostics), []);

        var repair = await remediationLoop.RunAsync(run.Id, session, rejected, cancellationToken);

        if (!repair.Result.Outcome.IsVerified)
        {
            run.ReviewBlocked("retry did not produce a change that verifies; the previous proposal is unchanged");
            return;
        }

        var transcriptReference = await logs.StoreAsync(run.Id, repair.Transcript, "remediation-transcript.md", cancellationToken);
        var files = session.Workspace is null ? [] : await session.Workspace.ProposedFilesAsync(cancellationToken);

        var retried = new ChangeProposal(
            run.Id, run.RepositoryId, proposal.BaseCommitId, timeProvider.GetUtcNow(),
            files, proposal.ProvokingDiagnostics, repair.Attempts, transcriptReference);

        var reference = await proposals.StoreAsync(retried, cancellationToken);
        run.ProposalReplaced(reference, timeProvider.GetUtcNow());
    }

    private async Task<ManagedRepository> GetRepositoryAsync(RemediationRun run, CancellationToken cancellationToken)
        => await repositories.GetAsync(run.RepositoryId, cancellationToken)
           ?? throw new InvalidOperationException($"Repository {run.RepositoryId} is not configured.");

    private static IReadOnlyList<ChangedManifest> ManifestEdits(ChangeProposal proposal)
        => proposal.ManifestEdits.Select(f => new ChangedManifest(f.Path, f.NewContent)).ToList();
}
