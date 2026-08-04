using AutoRemediator.Contracts.Messages;

namespace AutoRemediator.Agents;

/// <summary>
/// Runs the AI loop that addresses breaking changes introduced by a dependency update.
/// </summary>
public interface IRemediationAgent
{
    Task<RemediationOutcome> RemediateAsync(RemediationRunRequested run, CancellationToken cancellationToken = default);
}

/// <summary>Result of a remediation attempt.</summary>
/// <param name="Handled">Whether the agent produced changes for the run.</param>
/// <param name="Summary">Human-readable summary of what happened.</param>
public sealed record RemediationOutcome(bool Handled, string Summary);
