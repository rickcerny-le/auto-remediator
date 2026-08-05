using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.Feeds;
using NuGet.Versioning;

namespace AutoRemediator.Infrastructure.Alignment;

/// <summary>Outcome of aligning the declared family: the resolved versions, which escalated beyond policy, and any unresolvable siblings.</summary>
public sealed record AlignmentResult(
    IReadOnlyDictionary<string, string> Chosen,
    IReadOnlySet<string> BeyondPolicy,
    IReadOnlyList<string> Unresolved);

/// <summary>
/// Raises declared family siblings to the minimal versions that satisfy each package's
/// intra-family dependency ranges, iterating to a fixpoint. Correctness may escalate a
/// sibling beyond the strategy band (flagged). Undeclared siblings are left to NuGet.
/// </summary>
public interface IDependencyAligner
{
    Task<AlignmentResult> AlignAsync(
        IReadOnlyDictionary<string, string> declaredCurrent,
        IReadOnlyDictionary<string, string> chosen,
        TargetingSettings settings,
        CancellationToken cancellationToken = default);
}

internal sealed class DependencyAligner(
    IPackageDependencyReader dependencyReader,
    IFeedVersionResolver feedVersions) : IDependencyAligner
{
    private const int MaxIterations = 16;

    public async Task<AlignmentResult> AlignAsync(
        IReadOnlyDictionary<string, string> declaredCurrent,
        IReadOnlyDictionary<string, string> chosen,
        TargetingSettings settings,
        CancellationToken cancellationToken = default)
    {
        var resolved = new Dictionary<string, string>(chosen, StringComparer.OrdinalIgnoreCase);
        var beyond = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var depsCache = new Dictionary<string, IReadOnlyList<FamilyDependency>>();
        var versionsCache = new Dictionary<string, IReadOnlyList<NuGetVersion>>(StringComparer.OrdinalIgnoreCase);

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var changed = false;

            foreach (var (packageId, versionText) in resolved.ToList())
            {
                if (!NuGetVersion.TryParse(versionText, out _))
                {
                    continue;
                }

                var deps = await GetDepsAsync(packageId, versionText, settings, depsCache, cancellationToken);
                foreach (var dependency in deps)
                {
                    // Only declared siblings can be bumped (undeclared → transitive, NuGet resolves).
                    if (!resolved.TryGetValue(dependency.PackageId, out var depText) || !NuGetVersion.TryParse(depText, out var depVersion))
                    {
                        continue;
                    }

                    if (dependency.Range.Satisfies(depVersion))
                    {
                        continue;
                    }

                    var available = await GetVersionsAsync(dependency.PackageId, settings, versionsCache, cancellationToken);
                    var candidate = available
                        .Where(v => v > depVersion && dependency.Range.Satisfies(v))
                        .OrderBy(v => v)
                        .FirstOrDefault();

                    if (candidate is null)
                    {
                        unresolved.Add(dependency.PackageId);
                        continue;
                    }

                    resolved[dependency.PackageId] = candidate.ToNormalizedString();
                    changed = true;

                    if (declaredCurrent.TryGetValue(dependency.PackageId, out var originalText)
                        && NuGetVersion.TryParse(originalText, out var original)
                        && !VersionPolicy.IsWithinBand(original, candidate, settings.Policy.Strategy))
                    {
                        beyond.Add(dependency.PackageId);
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return new AlignmentResult(resolved, beyond, unresolved.ToList());
    }

    private async Task<IReadOnlyList<FamilyDependency>> GetDepsAsync(
        string packageId, string version, TargetingSettings settings,
        Dictionary<string, IReadOnlyList<FamilyDependency>> cache, CancellationToken cancellationToken)
    {
        var key = $"{packageId}/{version}";
        if (!cache.TryGetValue(key, out var deps))
        {
            deps = await dependencyReader.GetFamilyDependenciesAsync(packageId, version, settings.Feeds, settings.Patterns, cancellationToken);
            cache[key] = deps;
        }

        return deps;
    }

    private async Task<IReadOnlyList<NuGetVersion>> GetVersionsAsync(
        string packageId, TargetingSettings settings,
        Dictionary<string, IReadOnlyList<NuGetVersion>> cache, CancellationToken cancellationToken)
    {
        if (!cache.TryGetValue(packageId, out var versions))
        {
            var raw = await feedVersions.GetVersionsAsync(packageId, settings.Feeds, settings.Policy.AllowPrerelease, cancellationToken);
            versions = raw.Select(v => NuGetVersion.TryParse(v, out var parsed) ? parsed : null)
                .Where(v => v is not null)!
                .Cast<NuGetVersion>()
                .ToList();
            cache[packageId] = versions;
        }

        return versions;
    }
}
