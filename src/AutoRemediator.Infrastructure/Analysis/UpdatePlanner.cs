using AutoRemediator.Domain;

namespace AutoRemediator.Infrastructure.Analysis;

/// <summary>A manifest whose content changed after applying the bumps.</summary>
public sealed record ChangedManifest(string Path, string NewContent);

/// <summary>The updates and edited manifests for one repository.</summary>
public sealed record RepositoryUpdatePlan(IReadOnlyList<DependencyUpdate> Updates, IReadOnlyList<ChangedManifest> ChangedManifests)
{
    public bool HasChanges => Updates.Count > 0 && ChangedManifests.Count > 0;

    public static RepositoryUpdatePlan Empty => new([], []);
}

/// <summary>Computes the matched-outdated version bumps and the resulting edited manifests.</summary>
public interface IUpdatePlanner
{
    Task<RepositoryUpdatePlan> PlanAsync(ManagedRepository repository, TargetingSettings settings, CancellationToken cancellationToken = default);
}

internal sealed class UpdatePlanner(IRepositoryAnalyzer analyzer) : IUpdatePlanner
{
    public async Task<RepositoryUpdatePlan> PlanAsync(ManagedRepository repository, TargetingSettings settings, CancellationToken cancellationToken = default)
    {
        var analysis = await analyzer.AnalyzeAsync(repository, settings, cancellationToken);

        var updates = new Dictionary<string, DependencyUpdate>(StringComparer.OrdinalIgnoreCase);
        var changed = new List<ChangedManifest>();

        foreach (var manifest in analysis.Manifests)
        {
            var outdated = manifest.Packages
                .Where(p => p.Status == DependencyStatus.Outdated && p.CurrentVersion is not null && p.LatestVersion is not null)
                .ToList();

            if (outdated.Count == 0)
            {
                continue;
            }

            var content = manifest.Content;
            foreach (var package in outdated)
            {
                content = ManifestEditor.SetVersion(content, package.PackageId, package.LatestVersion!);
                updates[package.PackageId] = new DependencyUpdate(package.PackageId, package.CurrentVersion!, package.LatestVersion!);
            }

            if (!string.Equals(content, manifest.Content, StringComparison.Ordinal))
            {
                changed.Add(new ChangedManifest(manifest.Path, content));
            }
        }

        return new RepositoryUpdatePlan(updates.Values.ToList(), changed);
    }
}
