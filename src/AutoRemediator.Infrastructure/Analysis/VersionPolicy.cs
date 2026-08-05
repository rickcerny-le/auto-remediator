using AutoRemediator.Domain;
using NuGet.Versioning;

namespace AutoRemediator.Infrastructure.Analysis;

/// <summary>
/// Selects a target version relative to the current pin, capped by the update strategy.
/// Pure and deterministic — the version-selection heart of the update policy.
/// </summary>
public static class VersionPolicy
{
    /// <summary>
    /// Returns the highest available version greater than <paramref name="current"/> that is
    /// allowed by <paramref name="strategy"/>, or null when none qualifies (already current
    /// under policy). Patch stays within the same major.minor; Minor within the same major;
    /// Major takes the highest overall.
    /// </summary>
    public static NuGetVersion? SelectTarget(NuGetVersion current, IEnumerable<NuGetVersion> available, UpdateStrategy strategy)
    {
        var candidates = available.Where(v => v > current);

        candidates = strategy switch
        {
            UpdateStrategy.Patch => candidates.Where(v => v.Major == current.Major && v.Minor == current.Minor),
            UpdateStrategy.Minor => candidates.Where(v => v.Major == current.Major),
            _ => candidates, // Major: no band.
        };

        NuGetVersion? best = null;
        foreach (var candidate in candidates)
        {
            if (best is null || candidate > best)
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>True when moving from <paramref name="current"/> to <paramref name="candidate"/> stays within the strategy band.</summary>
    public static bool IsWithinBand(NuGetVersion current, NuGetVersion candidate, UpdateStrategy strategy) => strategy switch
    {
        UpdateStrategy.Patch => candidate.Major == current.Major && candidate.Minor == current.Minor,
        UpdateStrategy.Minor => candidate.Major == current.Major,
        _ => true,
    };
}
