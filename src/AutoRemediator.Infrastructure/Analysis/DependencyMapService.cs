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
    IEffectiveSettingsProvider settingsProvider,
    IRepositoryAnalyzer analyzer) : IDependencyMapService
{
    public async Task<DependencyMap> BuildAsync(CancellationToken cancellationToken = default)
    {
        var enabled = (await repositories.ListAsync(cancellationToken)).Where(r => r.Enabled).ToList();
        var effective = await settingsProvider.GetAsync(enabled, cancellationToken);

        var entries = new List<DependencyMapEntry>();

        foreach (var repo in enabled)
        {
            var settings = effective[repo.Id].Targeting;

            // Nothing can match without patterns, so skip the manifest read entirely.
            if (settings.Patterns.Count == 0)
            {
                continue;
            }

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
