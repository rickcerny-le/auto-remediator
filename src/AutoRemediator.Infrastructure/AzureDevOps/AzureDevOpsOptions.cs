namespace AutoRemediator.Infrastructure.AzureDevOps;

/// <summary>
/// Azure DevOps connection settings, bound from the "AzureDevOps" configuration section.
/// The PAT is sourced from Key Vault (via managed identity) in Azure and from user-secrets
/// locally — this type just receives the resolved value.
/// </summary>
public sealed class AzureDevOpsOptions
{
    public const string SectionName = "AzureDevOps";

    /// <summary>Organization base URL, e.g. https://dev.azure.com/Orion180.</summary>
    public string? OrganizationUrl { get; set; }

    /// <summary>Personal access token (Code:Read + Packaging:Read).</summary>
    public string? Pat { get; set; }
}
