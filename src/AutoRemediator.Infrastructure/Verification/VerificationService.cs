using System.Text;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.AzureDevOps;
using Microsoft.Extensions.Logging;

namespace AutoRemediator.Infrastructure.Verification;

/// <summary>
/// A verification result: how the change was judged, plus the lock-file changes restore produced
/// (empty unless the repository commits lock files).
/// </summary>
public sealed record VerificationResult(
    VerificationOutcome Outcome,
    IReadOnlyList<FileChange> LockFileChanges)
{
    public static VerificationResult Skipped(string reason, string? logReference = null)
        => new(VerificationOutcome.Skipped(reason, logReference), []);
}

/// <summary>Restores and builds a computed change locally, before anything is pushed.</summary>
public interface IVerificationService
{
    Task<VerificationResult> VerifyAsync(
        Guid runId,
        ManagedRepository repository,
        string commitId,
        RepositoryUpdatePlan plan,
        TargetingSettings settings,
        CancellationToken cancellationToken = default);
}

internal sealed class VerificationService(
    IVerificationWorkspaceFactory workspaces,
    IDotnetCliRunner dotnet,
    IVerificationLogStore logs,
    ILogger<VerificationService> logger) : IVerificationService
{
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(20);

    public async Task<VerificationResult> VerifyAsync(
        Guid runId,
        ManagedRepository repository,
        string commitId,
        RepositoryUpdatePlan plan,
        TargetingSettings settings,
        CancellationToken cancellationToken = default)
    {
        IVerificationWorkspace? workspace = null;

        try
        {
            try
            {
                workspace = await workspaces.CreateAsync(repository, commitId, plan.ChangedManifests, settings, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // No tree means nothing was verified — that is not evidence the bump is bad.
                logger.LogWarning(ex, "Run {RunId}: could not materialize {Slug} for verification.", runId, repository.Slug);
                return VerificationResult.Skipped($"the repository tree could not be prepared: {ex.Message}");
            }

            if (workspace.BuildTargets.Count == 0)
            {
                logger.LogWarning("Run {RunId}: {Slug} contains no solution or project to verify.", runId, repository.Slug);
                return VerificationResult.Skipped("the repository contains no solution or project to build");
            }

            // Restore gates build: its failures are cheaper to reach and are exactly the class of
            // problem feed-metadata alignment cannot see (third-party and transitive conflicts).
            var restore = await RunForEachTargetAsync(
                workspace, target => $"restore \"{target}\" --nologo", RestoreTimeout, cancellationToken);

            if (!restore.Succeeded)
            {
                return await FailedAsync(runId, workspace, "restore", restore, cancellationToken);
            }

            var build = await RunForEachTargetAsync(
                workspace,
                target => $"build \"{target}\" --no-restore --nologo -p:GenerateFullPaths=true",
                BuildTimeout,
                cancellationToken);

            if (!build.Succeeded)
            {
                return await FailedAsync(runId, workspace, "build", build, cancellationToken, restore.Output);
            }

            var logReference = await logs.StoreAsync(runId, Combine(restore.Output, build.Output), cancellationToken);
            var lockFiles = await workspace.LockFileChangesAsync(cancellationToken);

            logger.LogInformation(
                "Run {RunId}: verified {Slug} locally ({LockFiles} lock file change(s)).",
                runId, repository.Slug, lockFiles.Count);

            return new VerificationResult(VerificationOutcome.Verified(logReference), lockFiles);
        }
        finally
        {
            workspace?.Dispose();
        }
    }

    /// <summary>
    /// Runs a stage over every build target, stopping at the first failure. Output is accumulated so
    /// the stored log shows every target that ran, not just the one that failed.
    /// </summary>
    private async Task<CliResult> RunForEachTargetAsync(
        IVerificationWorkspace workspace,
        Func<string, string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var combined = new StringBuilder();
        CliResult? last = null;

        foreach (var target in workspace.BuildTargets)
        {
            var result = await dotnet.RunAsync(arguments(target), workspace.Root, timeout, cancellationToken);
            combined.AppendLine(result.Output);
            last = result;

            if (!result.Succeeded)
            {
                return result with { Output = combined.ToString() };
            }
        }

        return (last ?? new CliResult(0, string.Empty, false)) with { Output = combined.ToString() };
    }

    /// <summary>Classifies a failed stage and stores its log.</summary>
    private async Task<VerificationResult> FailedAsync(
        Guid runId,
        IVerificationWorkspace workspace,
        string stage,
        CliResult result,
        CancellationToken cancellationToken,
        string? precedingOutput = null)
    {
        var combined = precedingOutput is null ? result.Output : Combine(precedingOutput, result.Output);
        var logReference = await logs.StoreAsync(runId, combined, cancellationToken);

        if (result.TimedOut)
        {
            logger.LogWarning("Run {RunId}: {Stage} timed out.", runId, stage);
            return VerificationResult.Skipped($"{stage} exceeded its time limit", logReference);
        }

        var diagnostics = DiagnosticParser.Parse(result.Output, workspace.Root);

        if (!DiagnosticParser.IsDependencyFailure(diagnostics, result.Output))
        {
            var reason = DiagnosticParser.DescribeEnvironmentFailure(diagnostics, result.Output);
            logger.LogWarning("Run {RunId}: {Stage} failed for environmental reasons — {Reason}", runId, stage, reason);
            return VerificationResult.Skipped($"{stage} could not be trusted: {reason}", logReference);
        }

        logger.LogInformation(
            "Run {RunId}: {Stage} rejected the change with {Count} diagnostic(s).", runId, stage, diagnostics.Count);

        return new VerificationResult(VerificationOutcome.DependencyFailure(diagnostics, logReference), []);
    }

    private static string Combine(string restore, string build)
        => $"=== dotnet restore ==={Environment.NewLine}{restore}{Environment.NewLine}=== dotnet build ==={Environment.NewLine}{build}";
}
