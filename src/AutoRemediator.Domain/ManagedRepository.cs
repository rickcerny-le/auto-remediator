using AutoRemediator.Shared;

namespace AutoRemediator.Domain;

/// <summary>
/// An Azure DevOps repository enrolled for automated dependency remediation.
/// </summary>
public sealed class ManagedRepository : Entity<Guid>
{
    public ManagedRepository(
        Guid id,
        string organization,
        string project,
        string name,
        bool enabled = true,
        string targetBranch = "main",
        RepositorySettings? settings = null)
        : base(id)
    {
        Settings = settings ?? RepositorySettings.Inherit;
        Organization = Guard.AgainstNullOrWhiteSpace(organization);
        Project = Guard.AgainstNullOrWhiteSpace(project);
        Name = Guard.AgainstNullOrWhiteSpace(name);
        Enabled = enabled;
        TargetBranch = Guard.AgainstNullOrWhiteSpace(targetBranch);
    }

    public string Organization { get; }
    public string Project { get; }
    public string Name { get; private set; }
    public bool Enabled { get; private set; }

    /// <summary>Branch the tool reads from and (later) targets PRs against.</summary>
    public string TargetBranch { get; private set; }

    /// <summary>Per-repository settings layered over the global settings.</summary>
    public RepositorySettings Settings { get; private set; }

    /// <summary>Fully-qualified slug, e.g. "org/project/repo".</summary>
    public string Slug => $"{Organization}/{Project}/{Name}";

    public void Enable() => Enabled = true;
    public void Disable() => Enabled = false;
    public void SetTargetBranch(string branch) => TargetBranch = Guard.AgainstNullOrWhiteSpace(branch);
    public void SetSettings(RepositorySettings settings) => Settings = settings ?? throw new ArgumentNullException(nameof(settings));
}
