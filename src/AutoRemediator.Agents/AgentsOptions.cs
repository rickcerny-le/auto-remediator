namespace AutoRemediator.Agents;

/// <summary>
/// Bounds for the AI remediation loop, bound from the "Agents" configuration section.
///
/// The model endpoint and deployment are deliberately absent: they arrive as an Aspire
/// connection reference resolved by the chat-client integration, so the same code runs
/// against a local model in development and the deployed Foundry account in Azure.
/// </summary>
public sealed class AgentsOptions
{
    public const string SectionName = "Agents";

    /// <summary>Connection name the chat client resolves, matching the AppHost's model deployment.</summary>
    public const string ChatConnectionName = "chat";

    /// <summary>
    /// Most repair attempts for one run. A compile break that has not converged in a few
    /// attempts is usually the wrong shape of problem rather than one more edit away.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Ceiling on tokens consumed across all attempts in a single run. Holds regardless of
    /// how cheap any individual attempt looks.
    /// </summary>
    public int TokenBudget { get; set; } = 120_000;

    /// <summary>Longest a single model call may take before its attempt is abandoned.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromMinutes(3);
}
