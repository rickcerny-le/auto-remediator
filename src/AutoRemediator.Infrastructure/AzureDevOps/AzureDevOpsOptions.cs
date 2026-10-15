namespace AutoRemediator.Infrastructure.AzureDevOps;

/// <summary>
/// Azure DevOps connection settings, bound from the "AzureDevOps" configuration section.
/// The PAT is sourced from Key Vault (via managed identity) in Azure and from user-secrets
/// locally — this type just receives the resolved value.
/// </summary>
public sealed class AzureDevOpsOptions
{
    public const string SectionName = "AzureDevOps";

    /// <summary>Organization base URL, e.g. https://dev.azure.com/Contoso.</summary>
    public string? OrganizationUrl { get; set; }

    /// <summary>Personal access token (Code: Read &amp; Write, plus pull-request contribution — approval pushes a branch and opens a pull request).</summary>
    public string? Pat { get; set; }

    /// <summary><c>AzureDevOps</c> (default when absent) or <c>Local</c>, case-insensitive.</summary>
    public string? Mode { get; set; }

    /// <summary>True when <see cref="Mode"/> is <c>Local</c>: repositories are plain git repos on disk.</summary>
    public bool IsLocal => string.Equals(Mode?.Trim(), "Local", StringComparison.OrdinalIgnoreCase);

    /// <summary>Local mode: folder holding one git repository per managed repository name.</summary>
    public string? LocalRoot { get; set; }

    /// <summary>Local mode: folder for pull request records. Defaults to a <c>prs</c> folder beside <see cref="LocalRoot"/>.</summary>
    public string? LocalPullRequestsRoot { get; set; }

    /// <summary><see cref="LocalPullRequestsRoot"/>, or its default; null when neither it nor <see cref="LocalRoot"/> is set.</summary>
    public string? EffectiveLocalPullRequestsRoot
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(LocalPullRequestsRoot))
            {
                return LocalPullRequestsRoot;
            }

            if (string.IsNullOrWhiteSpace(LocalRoot))
            {
                return null;
            }

            var parent = Path.GetDirectoryName(Path.GetFullPath(LocalRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return Path.Combine(parent ?? string.Empty, "prs");
        }
    }
}
