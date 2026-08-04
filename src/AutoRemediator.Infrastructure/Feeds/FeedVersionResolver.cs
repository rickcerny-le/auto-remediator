using AutoRemediator.Infrastructure.AzureDevOps;
using Microsoft.Extensions.Options;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace AutoRemediator.Infrastructure.Feeds;

/// <summary>Resolves the latest available version of a package from configured feeds.</summary>
public interface IFeedVersionResolver
{
    Task<string?> GetLatestVersionAsync(
        string packageId,
        IReadOnlyList<string> feeds,
        bool allowPrerelease,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// NuGet.Protocol-based resolver. Authenticates to each (private) feed with the Azure
/// DevOps PAT and returns the highest version across feeds, excluding pre-release unless
/// allowed. Feed failures are swallowed per-feed so one bad source does not fail analysis.
/// </summary>
internal sealed class FeedVersionResolver(IOptions<AzureDevOpsOptions> options) : IFeedVersionResolver
{
    private readonly string _pat = options.Value.Pat ?? string.Empty;

    public async Task<string?> GetLatestVersionAsync(
        string packageId,
        IReadOnlyList<string> feeds,
        bool allowPrerelease,
        CancellationToken cancellationToken = default)
    {
        NuGetVersion? best = null;

        foreach (var feed in feeds)
        {
            var candidate = await GetHighestFromFeedAsync(packageId, feed, allowPrerelease, cancellationToken);
            if (candidate is not null && (best is null || candidate > best))
            {
                best = candidate;
            }
        }

        return best?.ToNormalizedString();
    }

    private async Task<NuGetVersion?> GetHighestFromFeedAsync(
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

            var versions = await resource.GetAllVersionsAsync(packageId, cache, NullLogger.Instance, cancellationToken);

            var candidates = versions.Where(v => allowPrerelease || !v.IsPrerelease).ToList();
            return candidates.Count == 0 ? null : candidates.Max();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A single feed failing must not fail the whole analysis pass.
            return null;
        }
    }
}
