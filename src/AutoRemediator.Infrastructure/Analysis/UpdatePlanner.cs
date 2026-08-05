using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Alignment;

namespace AutoRemediator.Infrastructure.Analysis;

/// <summary>A manifest whose content changed after applying the bumps.</summary>
public sealed record ChangedManifest(string Path, string NewContent);

/// <summary>The updates and edited manifests for one repository.</summary>
public sealed record RepositoryUpdatePlan(IReadOnlyList<DependencyUpdate> Updates, IReadOnlyList<ChangedManifest> ChangedManifests)
{
    public bool HasChanges => Updates.Count > 0 && ChangedManifests.Count > 0;

    public static RepositoryUpdatePlan Empty => new([], []);
}

/// <summary>
/// Computes the policy primary bumps, then adds the minimal collateral bumps from intra-family
/// alignment, and produces the edited manifests reflecting both.
/// </summary>
internal sealed class UpdatePlanner(IRepositoryAnalyzer analyzer, IDependencyAligner aligner) : IUpdatePlanner
{
    public async Task<RepositoryUpdatePlan> PlanAsync(ManagedRepository repository, TargetingSettings settings, CancellationToken cancellationToken = default)
    {
        var analysis = await analyzer.AnalyzeAsync(repository, settings, cancellationToken);

        // Declared matched packages (dedup by id, prefer a concrete current version).
        var declaredCurrent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Primary targets: outdated matched packages → their policy target.
        var primaryTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var package in analysis.Manifests.SelectMany(m => m.Packages))
        {
            if (package.CurrentVersion is not null && !declaredCurrent.ContainsKey(package.PackageId))
            {
                declaredCurrent[package.PackageId] = package.CurrentVersion;
            }

            if (package.Status == DependencyStatus.Outdated && package.CurrentVersion is not null && package.LatestVersion is not null)
            {
                primaryTargets[package.PackageId] = package.LatestVersion;
            }
        }

        // chosen = current overlaid with the policy primaries; then align the family.
        var chosen = new Dictionary<string, string>(declaredCurrent, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, target) in primaryTargets)
        {
            chosen[id] = target;
        }

        var alignment = await aligner.AlignAsync(declaredCurrent, chosen, settings, cancellationToken);

        // Updates = declared packages whose resolved version moved.
        var updates = new List<DependencyUpdate>();
        foreach (var (id, current) in declaredCurrent)
        {
            if (!alignment.Chosen.TryGetValue(id, out var to) || string.Equals(to, current, StringComparison.Ordinal))
            {
                continue;
            }

            updates.Add(new DependencyUpdate(
                id, current, to,
                primaryTargets.ContainsKey(id) ? UpdateKind.Matched : UpdateKind.Collateral,
                alignment.BeyondPolicy.Contains(id)));
        }

        // Edit each manifest for the packages it declares that changed.
        var finalVersions = updates.ToDictionary(u => u.PackageId, u => u.ToVersion, StringComparer.OrdinalIgnoreCase);
        var changed = new List<ChangedManifest>();
        foreach (var manifest in analysis.Manifests)
        {
            var content = manifest.Content;
            foreach (var package in manifest.Packages)
            {
                if (finalVersions.TryGetValue(package.PackageId, out var to))
                {
                    content = ManifestEditor.SetVersion(content, package.PackageId, to);
                }
            }

            if (!string.Equals(content, manifest.Content, StringComparison.Ordinal))
            {
                changed.Add(new ChangedManifest(manifest.Path, content));
            }
        }

        return new RepositoryUpdatePlan(updates, changed);
    }
}

/// <summary>Computes the version bumps (policy primaries + collateral alignment) and edited manifests.</summary>
public interface IUpdatePlanner
{
    Task<RepositoryUpdatePlan> PlanAsync(ManagedRepository repository, TargetingSettings settings, CancellationToken cancellationToken = default);
}
