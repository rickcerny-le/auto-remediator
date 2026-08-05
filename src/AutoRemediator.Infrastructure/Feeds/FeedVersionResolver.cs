using AutoRemediator.Infrastructure.AzureDevOps;
using Microsoft.Extensions.Options;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace AutoRemediator.Infrastructure.Feeds;

/// <summary>Resolves the versions of a package available across the configured feeds.</summary>
public interface IFeedVersionResolver
{
    /// <summary>
    /// Returns the distinct versions of <paramref name="packageId"/> available across
    /// <paramref name="feeds"/> as normalized version strings, excluding pre-release unless
    /// <paramref name="allowPrerelease"/> is set. Empty when nothing is found.
    /// </summary>
    Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        IReadOnlyList<string> feeds,
        bool allowPrerelease,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// NuGet.Protocol-based resolver. Authenticates to each (private) feed with the Azure
/// DevOps PAT. Feed failures are swallowed per-feed so one bad source does not fail analysis.
/// </summary>
internal sealed class FeedVersionResolver(IOptions<AzureDevOpsOptions> options) : IFeedVersionResolver
{
    private readonly string _pat = options.Value.Pat ?? string.Empty;

    public async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        IReadOnlyList<string> feeds,
        bool allowPrerelease,
        CancellationToken cancellationToken = default)
    {
        var versions = new HashSet<NuGetVersion>();

        foreach (var feed in feeds)
        {
            foreach (var version in await GetFeedVersionsAsync(packageId, feed, allowPrerelease, cancellationToken))
            {
                versions.Add(version);
            }
        }

        return versions.Select(v => v.ToNormalizedString()).ToList();
    }

    private async Task<IReadOnlyList<NuGetVersion>> GetFeedVersionsAsync(
        string packageId,
        string feed,
        bool allowPrerelease,
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

            var all = await resource.GetAllVersionsAsync(packageId, cache, NullLogger.Instance, cancellationToken);
            return all.Where(v => allowPrerelease || !v.IsPrerelease).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A single feed failing must not fail the whole analysis pass.
            return [];
        }
    }
}
