namespace AutoRemediator.Agents;

/// <summary>
/// Configuration for the Microsoft Agent Framework agents, bound from the "Agents"
/// configuration section. Values point at an Azure AI Foundry deployment.
/// </summary>
public sealed class AgentsOptions
{
    public const string SectionName = "Agents";

    /// <summary>Azure AI Foundry project/endpoint URL.</summary>
    public string? FoundryEndpoint { get; set; }

    /// <summary>Name of the model deployment used for remediation reasoning.</summary>
    public string? ModelDeploymentName { get; set; }
}
