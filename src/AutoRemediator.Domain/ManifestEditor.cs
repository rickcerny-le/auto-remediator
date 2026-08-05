using System.Text.RegularExpressions;

namespace AutoRemediator.Domain;

/// <summary>
/// Sets a package's version in manifest text with a targeted replacement, preserving all
/// other content (comments, formatting, ordering). Handles both the attribute form
/// (<c>Version="x"</c>) and the child-element form (<c>&lt;Version&gt;x&lt;/Version&gt;</c>)
/// of <c>PackageVersion</c>/<c>PackageReference</c>. Returns the content unchanged when the
/// package is not present.
/// </summary>
public static class ManifestEditor
{
    public static string SetVersion(string content, string packageId, string newVersion)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content;
        }

        var id = Regex.Escape(packageId);

        // Opening tag of PackageVersion/PackageReference for this Include id (self-closing or not).
        var openingTag = new Regex(
            $"<(PackageVersion|PackageReference)\\b[^>]*?Include\\s*=\\s*\"{id}\"[^>]*?>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var replacedAttribute = false;
        var result = openingTag.Replace(content, match =>
        {
            var tag = match.Value;
            var versionAttr = new Regex("(Version\\s*=\\s*\")([^\"]*)(\")", RegexOptions.IgnoreCase);
            if (versionAttr.IsMatch(tag))
            {
                replacedAttribute = true;
                return versionAttr.Replace(tag, m => m.Groups[1].Value + newVersion + m.Groups[3].Value, 1);
            }

            return tag; // No Version attribute — try the child-element form below.
        }, 1);

        if (replacedAttribute)
        {
            return result;
        }

        // Child-element form: <PackageReference Include="pkg"> ... <Version>old</Version> ... </...>
        var childVersion = new Regex(
            $"(<(PackageVersion|PackageReference)\\b[^>]*?Include\\s*=\\s*\"{id}\"[^>]*?>.*?<Version>)([^<]*)(</Version>)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return childVersion.Replace(result, m => m.Groups[1].Value + newVersion + m.Groups[4].Value, 1);
    }
}
