using AutoRemediator.Shared;

namespace AutoRemediator.Domain;

/// <summary>
/// An Azure DevOps repository enrolled for automated dependency remediation.
/// Placeholder aggregate for the scaffold — behavior is added in later changes.
/// </summary>
public sealed class ManagedRepository : Entity<Guid>
{
    public ManagedRepository(
        Guid id,
        string organization,
        string project,
        string name,
        bool enabled = true,
        string targetBranch = "main")
        : base(id)
    {
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

    /// <summary>Fully-qualified slug, e.g. "org/project/repo".</summary>
    public string Slug => $"{Organization}/{Project}/{Name}";

    public void Enable() => Enabled = true;
    public void Disable() => Enabled = false;
    public void SetTargetBranch(string branch) => TargetBranch = Guard.AgainstNullOrWhiteSpace(branch);
}
