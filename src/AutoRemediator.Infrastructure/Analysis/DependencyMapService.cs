using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;

namespace AutoRemediator.Infrastructure.Analysis;

/// <summary>Builds the outdated-dependency map across configured repositories.</summary>
public interface IDependencyMapService
{
    Task<DependencyMap> BuildAsync(CancellationToken cancellationToken = default);
}

internal sealed class DependencyMapService(
    IManagedRepositoryStore repositories,
    ITargetingSettingsStore settingsStore,
    IRepositoryAnalyzer analyzer) : IDependencyMapService
{
    public async Task<DependencyMap> BuildAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        var repos = await repositories.ListAsync(cancellationToken);

        var entries = new List<DependencyMapEntry>();

        foreach (var repo in repos.Where(r => r.Enabled))
        {
            var analysis = await analyzer.AnalyzeAsync(repo, settings, cancellationToken);

            // A package may appear in several manifests; show it once per repo.
            var deduped = analysis.Manifests
                .SelectMany(m => m.Packages)
                .GroupBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First());

            foreach (var package in deduped)
            {
                entries.Add(new DependencyMapEntry(
                    repo.Slug, package.PackageId, package.CurrentVersion, package.LatestVersion, package.Status));
            }
        }

        return new DependencyMap(entries);
    }
}
