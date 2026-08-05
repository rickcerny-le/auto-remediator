using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Feeds;
using NuGet.Versioning;

namespace AutoRemediator.Infrastructure.Analysis;

/// <summary>A matched package in a manifest with its current/latest/status.</summary>
public sealed record AnalyzedPackage(string PackageId, string? CurrentVersion, string? LatestVersion, DependencyStatus Status);

/// <summary>A manifest and the matched packages found in it.</summary>
public sealed record AnalyzedManifest(string Path, string Content, IReadOnlyList<AnalyzedPackage> Packages);

/// <summary>Per-repository analysis result across its manifests.</summary>
public sealed record RepositoryAnalysis(IReadOnlyList<AnalyzedManifest> Manifests);

/// <summary>
/// Reads a repository's manifests, matches packages against the configured patterns,
/// and resolves each matched package's latest version — the shared basis for both the
/// dependency map and update planning. Latest-version lookups are cached per pass.
/// </summary>
public interface IRepositoryAnalyzer
{
    Task<RepositoryAnalysis> AnalyzeAsync(ManagedRepository repository, TargetingSettings settings, CancellationToken cancellationToken = default);
}

internal sealed class RepositoryAnalyzer(
    IAzureDevOpsClient azureDevOps,
    IFeedVersionResolver feedVersions) : IRepositoryAnalyzer
{
    public async Task<RepositoryAnalysis> AnalyzeAsync(ManagedRepository repository, TargetingSettings settings, CancellationToken cancellationToken = default)
    {
        var manifests = await azureDevOps.GetManifestsAsync(repository, cancellationToken);
        var latestCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var analyzed = new List<AnalyzedManifest>();

        foreach (var manifest in manifests)
        {
            var matched = ManifestParser.Parse(manifest.Content)
                .Where(p => PackagePatternMatcher.IsMatch(p.Id, settings.Patterns, settings.Excludes));

            var packages = new List<AnalyzedPackage>();
            foreach (var package in matched)
            {
                if (!latestCache.TryGetValue(package.Id, out var latest))
                {
                    latest = await feedVersions.GetLatestVersionAsync(package.Id, settings.Feeds, settings.Policy.AllowPrerelease, cancellationToken);
                    latestCache[package.Id] = latest;
                }

                packages.Add(new AnalyzedPackage(package.Id, package.Version, latest, ComputeStatus(package.Version, latest)));
            }

            analyzed.Add(new AnalyzedManifest(manifest.Path, manifest.Content, packages));
        }

        return new RepositoryAnalysis(analyzed);
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
