using System.Text;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace AutoRemediator.Infrastructure.Remediation;

/// <summary>Executes a remediation run for one repository (bump matched packages → PR).</summary>
public interface IRemediationRunner
{
    Task<RemediationRun> RunAsync(RemediationRunRequested request, CancellationToken cancellationToken = default);
}

internal sealed class RemediationRunner(
    IManagedRepositoryStore repositories,
    ITargetingSettingsStore settingsStore,
    IUpdatePlanner planner,
    IAzureDevOpsClient azureDevOps,
    IRemediationRunStore runStore,
    TimeProvider timeProvider,
    ILogger<RemediationRunner> logger) : IRemediationRunner
{
    public const string UpdateBranch = "autoremediator/dependency-updates";

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

            var changes = plan.ChangedManifests.Select(c => new FileChange(c.Path, c.NewContent)).ToList();
            await azureDevOps.PushFilesAsync(repo, UpdateBranch, baseCommit, changes, CommitMessage(plan), cancellationToken);

            run.Advance(RunStatus.CreatingPr);
            var prUrl = await azureDevOps.EnsurePullRequestAsync(
                repo, UpdateBranch, repo.TargetBranch, PullRequestTitle(plan), PullRequestDescription(plan), cancellationToken);

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

    private static string PullRequestDescription(RepositoryUpdatePlan plan)
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

        return sb.ToString();
    }
}
