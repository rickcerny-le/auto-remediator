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
    /// Longest a single model call may take before its attempt is abandoned. The attempt count and
    /// token budget belong to the loop rather than the agent, and are bound from this same section
    /// by <c>RemediationLoopOptions</c> in the orchestration layer.
    /// </summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromMinutes(3);
}
