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
        var versionsCache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var analyzed = new List<AnalyzedManifest>();

        foreach (var manifest in manifests)
        {
            var matched = ManifestParser.Parse(manifest.Content)
                .Where(p => PackagePatternMatcher.IsMatch(p.Id, settings.Patterns, settings.Excludes));

            var packages = new List<AnalyzedPackage>();
            foreach (var package in matched)
            {
                packages.Add(await AnalyzePackageAsync(package, settings, versionsCache, cancellationToken));
            }

            analyzed.Add(new AnalyzedManifest(manifest.Path, manifest.Content, packages));
        }

        return new RepositoryAnalysis(analyzed);
    }

    private async Task<AnalyzedPackage> AnalyzePackageAsync(
        DeclaredPackage package,
        TargetingSettings settings,
        Dictionary<string, IReadOnlyList<string>> versionsCache,
        CancellationToken cancellationToken)
    {
        // Held by the ignore list — shown but never targeted; no feed lookup.
        if (PackagePatternMatcher.IsMatch(package.Id, settings.Policy.Ignore, []))
        {
            return new AnalyzedPackage(package.Id, package.Version, LatestVersion: null, DependencyStatus.Ignored);
        }

        if (!versionsCache.TryGetValue(package.Id, out var versions))
        {
            versions = await feedVersions.GetVersionsAsync(package.Id, settings.Feeds, settings.Policy.AllowPrerelease, cancellationToken);
            versionsCache[package.Id] = versions;
        }

        if (package.Version is null || !NuGetVersion.TryParse(package.Version, out var current))
        {
            return new AnalyzedPackage(package.Id, package.Version, LatestVersion: null, DependencyStatus.Unknown);
        }

        var available = versions
            .Select(v => NuGetVersion.TryParse(v, out var parsed) ? parsed : null)
            .Where(v => v is not null)!
            .Cast<NuGetVersion>()
            .ToList();

        // No versions resolved from the feed — latest is undeterminable.
        if (available.Count == 0)
        {
            return new AnalyzedPackage(package.Id, package.Version, LatestVersion: null, DependencyStatus.Unknown);
        }

        var target = VersionPolicy.SelectTarget(current, available, settings.Policy.Strategy);

        return target is null
            ? new AnalyzedPackage(package.Id, package.Version, LatestVersion: null, DependencyStatus.UpToDate)
            : new AnalyzedPackage(package.Id, package.Version, target.ToNormalizedString(), DependencyStatus.Outdated);
    }
}
