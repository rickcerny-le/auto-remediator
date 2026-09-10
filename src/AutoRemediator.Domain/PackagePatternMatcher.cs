using System.Text.RegularExpressions;

namespace AutoRemediator.Domain;

/// <summary>
/// Matches package ids against include/exclude globs (case-insensitive), e.g.
/// <c>Contoso.*</c>. A package matches when it matches any include and no exclude.
/// </summary>
public static class PackagePatternMatcher
{
    public static bool IsMatch(string packageId, IEnumerable<string> includes, IEnumerable<string> excludes)
    {
        ArgumentNullException.ThrowIfNull(packageId);

        var included = includes is not null && includes.Any(p => GlobToRegex(p).IsMatch(packageId));
        if (!included)
        {
            return false;
        }

        var excluded = excludes is not null && excludes.Any(p => GlobToRegex(p).IsMatch(packageId));
        return !excluded;
    }

    private static Regex GlobToRegex(string glob)
    {
        var pattern = "^" + Regex.Escape(glob)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
