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

/// <summary>
/// A materialized working tree that can be verified more than once. The tree is downloaded and the
/// computed edits applied when the session opens; each <see cref="VerifyAsync"/> compiles whatever
/// the tree currently contains, so edits applied between calls — by the AI remediation loop — are
/// picked up. Disposing deletes the tree.
/// </summary>
public interface IVerificationSession : IDisposable
{
    /// <summary>
    /// The working tree, or null when it could not be prepared. Null means verification never ran,
    /// which is not evidence against the change — <see cref="VerifyAsync"/> reports it as skipped.
    /// </summary>
    IVerificationWorkspace? Workspace { get; }

    /// <summary>Restores and builds the tree's current contents. Callable repeatedly.</summary>
    Task<VerificationResult> VerifyAsync(CancellationToken cancellationToken = default);
}

/// <summary>Opens a verification session over a repository at a commit.</summary>
public interface IVerificationService
{
    /// <summary>
    /// Downloads the repository at <paramref name="commitId"/>, applies the plan's edits, and returns
    /// a session ready to verify. Does not throw when the tree cannot be prepared — the returned
    /// session reports that as a skipped verification instead.
    /// </summary>
    Task<IVerificationSession> OpenAsync(
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
    public async Task<IVerificationSession> OpenAsync(
        Guid runId,
        ManagedRepository repository,
        string commitId,
        RepositoryUpdatePlan plan,
        TargetingSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var workspace = await workspaces.CreateAsync(
                repository, commitId, plan.ChangedManifests, settings, cancellationToken);

            return new VerificationSession(workspace, repository, runId, dotnet, logs, logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No tree means nothing was verified — that is not evidence the bump is bad.
            logger.LogWarning(ex, "Run {RunId}: could not materialize {Slug} for verification.", runId, repository.Slug);
            return VerificationSession.Unavailable(
                $"the repository tree could not be prepared: {ex.Message}");
        }
    }
}

internal sealed class VerificationSession : IVerificationSession
{
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(20);

    private readonly ManagedRepository? _repository;
    private readonly Guid _runId;
    private readonly IDotnetCliRunner? _dotnet;
    private readonly IVerificationLogStore? _logs;
    private readonly ILogger? _logger;
    private readonly VerificationResult? _unavailable;

    private int _verifications;

    internal VerificationSession(
        IVerificationWorkspace workspace,
        ManagedRepository repository,
        Guid runId,
        IDotnetCliRunner dotnet,
        IVerificationLogStore logs,
        ILogger logger)
    {
        Workspace = workspace;
        _repository = repository;
        _runId = runId;
        _dotnet = dotnet;
        _logs = logs;
        _logger = logger;
    }

    private VerificationSession(VerificationResult unavailable) => _unavailable = unavailable;

    /// <summary>A session over a tree that could not be prepared; verification reports it as skipped.</summary>
    internal static VerificationSession Unavailable(string reason)
        => new(VerificationResult.Skipped(reason));

    public IVerificationWorkspace? Workspace { get; }

    public async Task<VerificationResult> VerifyAsync(CancellationToken cancellationToken = default)
    {
        if (_unavailable is not null || Workspace is null)
        {
            return _unavailable ?? VerificationResult.Skipped("the repository tree is unavailable");
        }

        var workspace = Workspace;
        var slug = _repository!.Slug;

        if (workspace.BuildTargets.Count == 0)
        {
            _logger!.LogWarning("Run {RunId}: {Slug} contains no solution or project to verify.", _runId, slug);
            return VerificationResult.Skipped("the repository contains no solution or project to build");
        }

        // Each verification stores its own log so a repeated verification cannot overwrite the
        // evidence from an earlier attempt.
        var attempt = ++_verifications;

        // Restore gates build: its failures are cheaper to reach and are exactly the class of
        // problem feed-metadata alignment cannot see (third-party and transitive conflicts).
        var restore = await RunForEachTargetAsync(
            workspace, target => $"restore \"{target}\" --nologo", RestoreTimeout, cancellationToken);

        if (!restore.Succeeded)
        {
            return await FailedAsync(workspace, "restore", restore, attempt, cancellationToken);
        }

        var build = await RunForEachTargetAsync(
            workspace,
            target => $"build \"{target}\" --no-restore --nologo -p:GenerateFullPaths=true",
            BuildTimeout,
            cancellationToken);

        if (!build.Succeeded)
        {
            return await FailedAsync(workspace, "build", build, attempt, cancellationToken, restore.Output);
        }

        var logReference = await StoreLogAsync(Combine(restore.Output, build.Output), attempt, cancellationToken);
        var lockFiles = await workspace.LockFileChangesAsync(cancellationToken);

        _logger!.LogInformation(
            "Run {RunId}: verified {Slug} locally on verification {Attempt} ({LockFiles} lock file change(s)).",
            _runId, slug, attempt, lockFiles.Count);

        return new VerificationResult(VerificationOutcome.Verified(logReference), lockFiles);
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
            var result = await _dotnet!.RunAsync(arguments(target), workspace.Root, timeout, cancellationToken);
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
        IVerificationWorkspace workspace,
        string stage,
        CliResult result,
        int attempt,
        CancellationToken cancellationToken,
        string? precedingOutput = null)
    {
        var combined = precedingOutput is null ? result.Output : Combine(precedingOutput, result.Output);
        var logReference = await StoreLogAsync(combined, attempt, cancellationToken);

        if (result.TimedOut)
        {
            _logger!.LogWarning("Run {RunId}: {Stage} timed out.", _runId, stage);
            return VerificationResult.Skipped($"{stage} exceeded its time limit", logReference);
        }

        var diagnostics = DiagnosticParser.Parse(result.Output, workspace.Root);

        if (!DiagnosticParser.IsDependencyFailure(diagnostics, result.Output))
        {
            var reason = DiagnosticParser.DescribeEnvironmentFailure(diagnostics, result.Output);
            _logger!.LogWarning("Run {RunId}: {Stage} failed for environmental reasons — {Reason}", _runId, stage, reason);
            return VerificationResult.Skipped($"{stage} could not be trusted: {reason}", logReference);
        }

        _logger!.LogInformation(
            "Run {RunId}: {Stage} rejected the change with {Count} diagnostic(s).", _runId, stage, diagnostics.Count);

        return new VerificationResult(VerificationOutcome.DependencyFailure(diagnostics, logReference), []);
    }

    private Task<string?> StoreLogAsync(string content, int attempt, CancellationToken cancellationToken)
        => _logs!.StoreAsync(
            _runId,
            content,
            attempt == 1 ? "verification.log" : $"verification-{attempt}.log",
            cancellationToken);

    private static string Combine(string restore, string build)
        => $"=== dotnet restore ==={Environment.NewLine}{restore}{Environment.NewLine}=== dotnet build ==={Environment.NewLine}{build}";

    public void Dispose() => Workspace?.Dispose();
}
