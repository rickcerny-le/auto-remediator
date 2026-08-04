using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Feeds;
using NuGet.Versioning;

namespace AutoRemediator.Infrastructure.Analysis;

/// <summary>Builds the outdated-dependency map across configured repositories.</summary>
public interface IDependencyMapService
{
    Task<DependencyMap> BuildAsync(CancellationToken cancellationToken = default);
}

internal sealed class DependencyMapService(
    IManagedRepositoryStore repositories,
    ITargetingSettingsStore settingsStore,
    IAzureDevOpsClient azureDevOps,
    IFeedVersionResolver feedVersions) : IDependencyMapService
{
    public async Task<DependencyMap> BuildAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        var repos = await repositories.ListAsync(cancellationToken);

        var entries = new List<DependencyMapEntry>();

        foreach (var repo in repos.Where(r => r.Enabled))
        {
            var manifests = await azureDevOps.GetManifestsAsync(repo, cancellationToken);

            var matched = manifests
                .SelectMany(m => ManifestParser.Parse(m.Content))
                .Where(p => PackagePatternMatcher.IsMatch(p.Id, settings.Patterns, settings.Excludes))
                // A package can appear in several manifests; keep one, preferring a concrete version.
                .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.FirstOrDefault(x => x.Version is not null) ?? g.First());

            foreach (var package in matched)
            {
                var latest = await feedVersions.GetLatestVersionAsync(
                    package.Id, settings.Feeds, settings.Policy.AllowPrerelease, cancellationToken);

                entries.Add(new DependencyMapEntry(
                    repo.Slug,
                    package.Id,
                    package.Version,
                    latest,
                    ComputeStatus(package.Version, latest)));
            }
        }

        return new DependencyMap(entries);
    }

    private static DependencyStatus ComputeStatus(string? current, string? latest)
    {
        if (current is null || latest is null
            || !NuGetVersion.TryParse(current, out var currentVersion)
            || !NuGetVersion.TryParse(latest, out var latestVersion))
        {
            return DependencyStatus.Unknown;
        }

        return currentVersion < latestVersion ? DependencyStatus.Outdated : DependencyStatus.UpToDate;
    }
}
