using AutoRemediator.Contracts.Messages;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Agents;

/// <summary>
/// Microsoft Agent Framework-backed remediation agent. Placeholder for the scaffold:
/// it wires up configuration and the MAF agent field, but performs no live model call.
/// The <see cref="AIAgent"/> instance is constructed from an Azure AI Foundry chat
/// client in a later change.
/// </summary>
internal sealed class MafRemediationAgent(
    IOptions<AgentsOptions> options,
    ILogger<MafRemediationAgent> logger) : IRemediationAgent
{
    private readonly AgentsOptions _options = options.Value;

    // Assigned in a later change from an Azure AI Foundry-backed chat client.
    private AIAgent? Agent { get; set; }

    public Task<RemediationOutcome> RemediateAsync(RemediationRunRequested run, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Remediation agent (placeholder) received run {RunId} for {Repository}. Max attempts: {MaxAttempts}; agent initialized: {Initialized}",
            run.RunId,
            run.RepositoryName,
            _options.MaxAttempts,
            Agent is not null);

        return Task.FromResult(new RemediationOutcome(
            Handled: false,
            Summary: "Placeholder MAF agent — no remediation performed in this scaffold."));
    }
}
