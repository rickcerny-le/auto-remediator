using System.Text;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Verification;
using Microsoft.Extensions.Logging;

namespace AutoRemediator.Infrastructure.Remediation;

/// <summary>Executes a remediation run for one repository (bump matched packages → verify → PR).</summary>
public interface IRemediationRunner
{
    Task<RemediationRun> RunAsync(RemediationRunRequested request, CancellationToken cancellationToken = default);
}

internal sealed class RemediationRunner(
    IManagedRepositoryStore repositories,
    ITargetingSettingsStore settingsStore,
    IUpdatePlanner planner,
    IVerificationService verification,
    IRemediationLoop remediation,
    IVerificationLogStore logs,
    IAzureDevOpsClient azureDevOps,
    IRemediationRunStore runStore,
    TimeProvider timeProvider,
    ILogger<RemediationRunner> logger) : IRemediationRunner
{
    /// <summary>
    /// The only branch this system writes to. Changes always reach a repository as a pull request
    /// into its target branch — never a direct push to it, and never to a protected branch,
    /// whether or not an agent contributed.
    /// </summary>
    public const string UpdateBranch = "autoremediator/dependency-updates";

    /// <summary>
    /// True when the rejection includes compiler errors, which are what source edits can address.
    /// Restore-time (`NU`) diagnostics alone are version math and are left alone.
    /// </summary>
    private static bool HasCompileDiagnostics(VerificationOutcome outcome)
        => outcome.Diagnostics.Any(d => d.Code.StartsWith("CS", StringComparison.OrdinalIgnoreCase));

    public async Task<RemediationRun> RunAsync(RemediationRunRequested request, CancellationToken cancellationToken = default)
    {
        var slug = $"{request.Organization}/{request.Project}/{request.RepositoryName}";
        var run = new RemediationRun(request.RunId, request.RepositoryId, slug, timeProvider.GetUtcNow());
        await runStore.SaveAsync(run, cancellationToken);

        try
        {
            var repo = await repositories.GetAsync(request.RepositoryId, cancellationToken)
                       ?? throw new InvalidOperationException($"Repository {request.RepositoryId} is not configured.");

            run.Advance(RunStatus.Analyzing);
            var settings = await settingsStore.GetAsync(cancellationToken);
            var plan = await planner.PlanAsync(repo, settings, cancellationToken);

            if (!plan.HasChanges)
            {
                // Short-circuits before any tree download: a run with nothing to do pays for no
                // archive, no restore and no build.
                run.NoUpdates(timeProvider.GetUtcNow());
                await runStore.SaveAsync(run, cancellationToken);
                logger.LogInformation("Run {RunId}: no matched outdated packages for {Slug}.", run.Id, slug);
                return run;
            }

            run.Advance(RunStatus.Applying);
            var branchHead = await azureDevOps.GetBranchHeadAsync(repo, UpdateBranch, cancellationToken);
            var baseCommit = branchHead
                             ?? await azureDevOps.GetBranchHeadAsync(repo, repo.TargetBranch, cancellationToken)
                             ?? throw new InvalidOperationException($"Could not resolve head of target branch '{repo.TargetBranch}'.");

            // Verify against the same commit the push will be based on, so the tree that was
            // compiled and the commit's parent agree. The session owns the extracted tree for the
            // rest of the run so it can be verified again after remediation edits.
            run.Advance(RunStatus.Verifying);
            await runStore.SaveAsync(run, cancellationToken);

            using var session = await verification.OpenAsync(run.Id, repo, baseCommit, plan, settings, cancellationToken);
            var verified = await session.VerifyAsync(cancellationToken);
            run.Verified(verified.Outcome);

            // The AI loop only helps with compile breaks. A restore conflict is version math, and an
            // agent handed one would either flail or "fix" it by editing a manifest.
            if (verified.Outcome.Rejected && HasCompileDiagnostics(verified.Outcome))
            {
                run.Advance(RunStatus.Remediating);
                await runStore.SaveAsync(run, cancellationToken);

                var repair = await remediation.RunAsync(run.Id, session, verified, cancellationToken);
                var transcriptReference = await logs.StoreAsync(
                    run.Id, repair.Transcript, "remediation-transcript.md", cancellationToken);

                run.Remediated(repair.Attempts, transcriptReference);
                verified = repair.Result;
                run.Verified(verified.Outcome);
            }

            if (verified.Outcome.Rejected)
            {
                run.VerificationFailed(plan.Updates, verified.Outcome, timeProvider.GetUtcNow());
                await runStore.SaveAsync(run, cancellationToken);
                logger.LogInformation(
                    "Run {RunId}: verification rejected the change for {Slug} with {Count} diagnostic(s); no pull request opened.",
                    run.Id, slug, verified.Outcome.Diagnostics.Count);
                return run;
            }

            run.Advance(RunStatus.Pushing);
            var changes = plan.ChangedManifests
                .Select(c => new FileChange(c.Path, c.NewContent))
                .Concat(verified.LockFileChanges)
                .Concat(session.Workspace is null
                    ? []
                    : await session.Workspace.AppliedEditChangesAsync(cancellationToken))
                .ToList();

            await azureDevOps.PushFilesAsync(repo, UpdateBranch, baseCommit, changes, CommitMessage(plan), cancellationToken);

            run.Advance(RunStatus.CreatingPr);
            var prUrl = await azureDevOps.EnsurePullRequestAsync(
                repo, UpdateBranch, repo.TargetBranch,
                PullRequestTitle(plan),
                PullRequestDescription(plan, verified.Outcome, run.RemediationAttempts),
                cancellationToken);

            run.Completed(plan.Updates, prUrl, timeProvider.GetUtcNow());
            await runStore.SaveAsync(run, cancellationToken);
            logger.LogInformation("Run {RunId}: opened/updated PR {Url} with {Count} update(s) for {Slug}.",
                run.Id, prUrl, plan.Updates.Count, slug);
            return run;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Failed(ex.Message, timeProvider.GetUtcNow());
            await runStore.SaveAsync(run, cancellationToken);
            logger.LogError(ex, "Run {RunId} failed for {Slug}.", run.Id, slug);
            return run;
        }
    }

    private static string CommitMessage(RepositoryUpdatePlan plan)
        => $"Update {plan.Updates.Count} package(s) [AutoRemediator]";

    private static string PullRequestTitle(RepositoryUpdatePlan plan)
        => $"Automated dependency updates ({plan.Updates.Count} package(s))";

    private static string PullRequestDescription(
        RepositoryUpdatePlan plan,
        VerificationOutcome verification,
        int? remediationAttempts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Automated dependency update by **AutoRemediator**.");
        sb.AppendLine();
        sb.AppendLine("| Package | From | To | Kind |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var u in plan.Updates)
        {
            var kind = u.Kind == UpdateKind.Collateral ? "collateral" : "matched";
            if (u.BeyondPolicy)
            {
                kind += " ⚠ beyond policy";
            }

            sb.AppendLine($"| {u.PackageId} | {u.FromVersion} | {u.ToVersion} | {kind} |");
        }

        if (plan.Updates.Any(u => u.BeyondPolicy))
        {
            sb.AppendLine();
            sb.AppendLine("> ⚠ Some collateral bumps were escalated **beyond the update policy** to keep the dependency set consistent.");
        }

        sb.AppendLine();

        // A reviewer must never be left to assume a change was verified when it was not.
        if (verification.IsVerified)
        {
            sb.AppendLine("✅ **Verified locally** — `dotnet restore` and `dotnet build` both succeeded against this change.");
        }
        else
        {
            sb.AppendLine($"⚠ **Not verified locally** — verification was skipped: {verification.SkipReason}");
        }

        // Disclosed rather than left to be inferred from the diff: a reviewer must know that a
        // model wrote source in here, and how many tries it took.
        if (remediationAttempts is > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"🤖 **Contains AI-authored source edits.** A compile break introduced by this update was "
                          + $"repaired by an AI agent over {remediationAttempts} attempt(s). Review the source changes "
                          + "with that in mind — the full transcript of what the agent was shown and what it proposed "
                          + "is attached to this run in AutoRemediator.");
        }

        sb.AppendLine();
        sb.AppendLine("This pull request is still validated by this repository's own CI, which additionally runs the tests.");

        return sb.ToString();
    }
}
