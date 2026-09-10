using AutoRemediator.Domain;

namespace AutoRemediator.Infrastructure.Remediation;

/// <summary>
/// Stands in when no real agent is registered — a host that does not compose the agents library, or
/// one where no model is configured. Reports itself unavailable so the loop records that remediation
/// was unavailable instead of pretending to have tried.
///
/// This is what makes the model a soft dependency at the composition level: infrastructure can be
/// used without any agent at all, and dependency updates still flow.
/// </summary>
internal sealed class UnavailableRemediationAgent : IRemediationAgent
{
    public bool IsAvailable => false;

    public Task<RemediationProposal> ProposeAsync(
        RemediationAttempt attempt,
        CancellationToken cancellationToken = default)
        => Task.FromResult(RemediationProposal.None("no remediation agent is registered"));
}
