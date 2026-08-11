using System.Text;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Verification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Infrastructure.Remediation;

/// <summary>
/// Bounds on the repair loop, bound from the "Agents" configuration section alongside the agent's
/// own per-call timeout. These belong to the orchestration rather than to the agent: the agent
/// reports what it spent, the loop decides when to stop.
/// </summary>
public sealed class RemediationLoopOptions
{
    public const string SectionName = "Agents";

    /// <summary>
    /// Most repair attempts for one run. A compile break that has not converged in a few attempts is
    /// usually the wrong shape of problem rather than one more edit away.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Ceiling on tokens across all attempts in a run, regardless of how cheap each looks.</summary>
    public int TokenBudget { get; set; } = 120_000;
}

/// <summary>What the loop achieved, for the run record and the pull request.</summary>
/// <param name="Attempts">How many attempts were made; zero when the loop could not start one.</param>
/// <param name="Repaired">True when a later verification succeeded.</param>
/// <param name="Result">The final verification result — the repaired one, or the last failure.</param>
/// <param name="Transcript">Human-readable record of what was tried.</param>
public sealed record RemediationLoopResult(
    int Attempts,
    bool Repaired,
    VerificationResult Result,
    string Transcript);

/// <summary>Runs the AI repair loop over an open verification session.</summary>
public interface IRemediationLoop
{
    Task<RemediationLoopResult> RunAsync(
        Guid runId,
        IVerificationSession session,
        VerificationResult rejected,
        CancellationToken cancellationToken = default);
}

internal sealed class RemediationLoop(
    IRemediationAgent agent,
    IOptions<RemediationLoopOptions> options,
    ILogger<RemediationLoop> logger) : IRemediationLoop
{
    private readonly RemediationLoopOptions _options = options.Value;

    public async Task<RemediationLoopResult> RunAsync(
        Guid runId,
        IVerificationSession session,
        VerificationResult rejected,
        CancellationToken cancellationToken = default)
    {
        var transcript = new StringBuilder();
        var current = rejected;
        var attempts = 0;
        var tokensSpent = 0;

        transcript.AppendLine($"# AI remediation for run {runId}");
        transcript.AppendLine();
        AppendDiagnostics(transcript, "Initial diagnostics", current.Outcome.Diagnostics);

        if (!agent.IsAvailable)
        {
            // The model is a soft dependency: say so plainly rather than implying an attempt.
            logger.LogInformation("Run {RunId}: no remediation agent is available; leaving the change rejected.", runId);
            transcript.AppendLine("No model is configured, so no repair was attempted.");
            return new RemediationLoopResult(0, false, current, transcript.ToString());
        }

        if (session.Workspace is not { } workspace)
        {
            transcript.AppendLine("No working tree was available, so no repair was attempted.");
            return new RemediationLoopResult(0, false, current, transcript.ToString());
        }

        var source = new WorkspaceSourceReader(workspace);

        while (attempts < _options.MaxAttempts)
        {
            if (tokensSpent >= _options.TokenBudget)
            {
                transcript.AppendLine($"Stopped: the token budget of {_options.TokenBudget} was exhausted after {attempts} attempt(s).");
                logger.LogInformation("Run {RunId}: token budget exhausted after {Attempts} attempt(s).", runId, attempts);
                break;
            }

            attempts++;
            transcript.AppendLine();
            transcript.AppendLine($"## Attempt {attempts}");

            var proposal = await agent.ProposeAsync(
                new RemediationAttempt(attempts, current.Outcome.Diagnostics, source), cancellationToken);

            tokensSpent += proposal.TokensUsed;
            transcript.AppendLine($"Model reported {proposal.TokensUsed} token(s); {tokensSpent} spent of {_options.TokenBudget}.");

            if (proposal.Summary is not null)
            {
                transcript.AppendLine($"Agent: {proposal.Summary}");
            }

            if (!proposal.HasEdits)
            {
                transcript.AppendLine("No edits proposed; stopping.");
                break;
            }

            var applied = 0;
            foreach (var edit in proposal.Edits)
            {
                var outcome = await workspace.ApplyProposedEditAsync(edit, cancellationToken);
                if (outcome.Applied)
                {
                    applied++;
                    transcript.AppendLine($"- applied `{outcome.Path}`");
                }
                else
                {
                    transcript.AppendLine($"- REJECTED `{outcome.Path}`: {outcome.RejectionReason}");
                }
            }

            if (applied == 0)
            {
                // Re-verifying an unchanged tree would return the same diagnostics forever.
                transcript.AppendLine("No proposed edit was permitted; stopping.");
                break;
            }

            current = await session.VerifyAsync(cancellationToken);
            transcript.AppendLine($"Verification after attempt {attempts}: {current.Outcome.Classification}.");

            if (current.Outcome.IsVerified)
            {
                logger.LogInformation("Run {RunId}: repaired after {Attempts} attempt(s).", runId, attempts);
                transcript.AppendLine();
                transcript.AppendLine($"Repaired after {attempts} attempt(s).");
                return new RemediationLoopResult(attempts, true, current, transcript.ToString());
            }

            if (current.Outcome.Classification != VerificationClassification.DependencyFailure)
            {
                // The change is no longer being rejected — it can no longer be judged. Stop rather
                // than keep editing against a verdict that means "we do not know".
                transcript.AppendLine("Verification can no longer judge the change; stopping.");
                break;
            }

            AppendDiagnostics(transcript, "Remaining diagnostics", current.Outcome.Diagnostics);
        }

        if (attempts >= _options.MaxAttempts && !current.Outcome.IsVerified)
        {
            transcript.AppendLine();
            transcript.AppendLine($"Stopped: reached the maximum of {_options.MaxAttempts} attempt(s) without a building tree.");
        }

        logger.LogInformation(
            "Run {RunId}: remediation did not repair the change after {Attempts} attempt(s).", runId, attempts);

        return new RemediationLoopResult(attempts, false, current, transcript.ToString());
    }

    private static void AppendDiagnostics(
        StringBuilder transcript,
        string heading,
        IReadOnlyList<VerificationDiagnostic> diagnostics)
    {
        transcript.AppendLine($"### {heading} ({diagnostics.Count})");
        foreach (var d in diagnostics)
        {
            var where = d.Path is null ? string.Empty : $" {d.Path}{(d.Line is null ? string.Empty : $":{d.Line}")}";
            transcript.AppendLine($"- {d.Code}{where}: {d.Message}");
        }
    }

    /// <summary>Adapts the verification workspace to the domain's narrow source-reading contract.</summary>
    private sealed class WorkspaceSourceReader(IVerificationWorkspace workspace) : ISourceReader
    {
        public Task<string?> ReadAsync(string repositoryRelativePath, CancellationToken cancellationToken = default)
            => workspace.ReadAsync(repositoryRelativePath, cancellationToken);
    }
}
