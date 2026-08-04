using System.Xml.Linq;

namespace AutoRemediator.Domain;

/// <summary>A package declared in a manifest. <see cref="Version"/> is null when unresolved (e.g. an MSBuild variable).</summary>
public sealed record DeclaredPackage(string Id, string? Version);

/// <summary>
/// Extracts declared packages from .NET dependency manifests:
/// - <c>Directory.Packages.props</c> — <c>&lt;PackageVersion Include="X" Version="Y" /&gt;</c> (Central Package Management)
/// - <c>*.csproj</c> — <c>&lt;PackageReference Include="X" Version="Y" /&gt;</c> (attribute or child element)
/// Versions expressed as MSBuild variables (e.g. <c>$(OrionVersion)</c>) are returned as null (unknown).
/// </summary>
public static class ManifestParser
{
    public static IReadOnlyList<DeclaredPackage> Parse(string xmlContent)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return [];
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xmlContent);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var packages = new List<DeclaredPackage>();

        // Match both PackageVersion (CPM) and PackageReference (csproj), ignoring XML namespaces.
        foreach (var element in doc.Descendants())
        {
            var localName = element.Name.LocalName;
            if (localName is not ("PackageVersion" or "PackageReference"))
            {
                continue;
            }

            var id = (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var version = (string?)element.Attribute("Version")
                          ?? (string?)element.Elements().FirstOrDefault(e => e.Name.LocalName == "Version");

            packages.Add(new DeclaredPackage(id.Trim(), NormalizeVersion(version)));
        }

        return packages;
    }

    private static string? NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        version = version.Trim();

        // Unresolved MSBuild variable — cannot determine the concrete version here.
        return version.Contains("$(", StringComparison.Ordinal) ? null : version;
    }
}
