using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.AzureDevOps;
using Microsoft.Extensions.Options;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace AutoRemediator.Infrastructure.Alignment;

/// <summary>An intra-family dependency declared by a package version.</summary>
public sealed record FamilyDependency(string PackageId, VersionRange Range);

/// <summary>Reads a package version's dependencies from the feed, filtered to the family.</summary>
public interface IPackageDependencyReader
{
    Task<IReadOnlyList<FamilyDependency>> GetFamilyDependenciesAsync(
        string packageId,
        string version,
        IReadOnlyList<string> feeds,
        IReadOnlyList<string> familyPatterns,
        CancellationToken cancellationToken = default);
}

internal sealed class PackageDependencyReader(IOptions<AzureDevOpsOptions> options) : IPackageDependencyReader
{
    private readonly string _pat = options.Value.Pat ?? string.Empty;

    public async Task<IReadOnlyList<FamilyDependency>> GetFamilyDependenciesAsync(
        string packageId,
        string version,
        IReadOnlyList<string> feeds,
        IReadOnlyList<string> familyPatterns,
        CancellationToken cancellationToken = default)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return [];
        }

        var byId = new Dictionary<string, FamilyDependency>(StringComparer.OrdinalIgnoreCase);

        foreach (var feed in feeds)
        {
            foreach (var dependency in await GetFromFeedAsync(packageId, nugetVersion, feed, familyPatterns, cancellationToken))
            {
                // Union across feeds/framework groups: keep the most demanding lower bound.
                if (!byId.TryGetValue(dependency.PackageId, out var existing) || MinOf(dependency) > MinOf(existing))
                {
                    byId[dependency.PackageId] = dependency;
                }
            }
        }

        return byId.Values.ToList();
    }

    private async Task<IReadOnlyList<FamilyDependency>> GetFromFeedAsync(
        string packageId,
        NuGetVersion version,
        string feed,
        IReadOnlyList<string> familyPatterns,
        CancellationToken cancellationToken)
    {
        try
        {
            var source = new PackageSource(feed);
            if (!string.IsNullOrEmpty(_pat))
            {
                source.Credentials = new PackageSourceCredential(feed, "pat", _pat, isPasswordClearText: true, validAuthenticationTypesText: null);
            }

            var repository = Repository.Factory.GetCoreV3(source);
            var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
            using var cache = new SourceCacheContext();

            var info = await resource.GetDependencyInfoAsync(packageId, version, cache, NullLogger.Instance, cancellationToken);
            if (info is null)
            {
                return [];
            }

            // Union all framework groups; keep only family-matching dependency ids.
            return info.DependencyGroups
                .SelectMany(g => g.Packages)
                .Where(p => PackagePatternMatcher.IsMatch(p.Id, familyPatterns, []))
                .Select(p => new FamilyDependency(p.Id, p.VersionRange))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }

    private static NuGetVersion MinOf(FamilyDependency dependency)
        => dependency.Range.MinVersion ?? new NuGetVersion(0, 0, 0);
}
