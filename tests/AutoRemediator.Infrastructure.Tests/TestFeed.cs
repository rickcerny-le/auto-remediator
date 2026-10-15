using System.IO.Compression;
using System.Security;

namespace AutoRemediator.Infrastructure.Tests;

/// <summary>
/// A throwaway local NuGet folder feed: flat <c>.nupkg</c> files holding only a nuspec (no assemblies).
/// Enough for version listing, dependency reading and restore; not enough to compile against.
/// </summary>
internal sealed class TestFeed : IDisposable
{
    public TestFeed()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ar-feed-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Path);
    }

    /// <summary>Absolute folder path, usable directly as a feed.</summary>
    public string Path { get; }

    /// <summary>
    /// A package id unique per call, so the global packages cache can never serve a stale copy
    /// from an earlier run.
    /// </summary>
    public static string UniqueId(string prefix) => $"{prefix}.T{Guid.NewGuid().ToString("N")[..8]}";

    /// <summary>Writes <c>{id}.{version}.nupkg</c>; dependencies are (id, version range) pairs.</summary>
    public TestFeed Add(string id, string version, params (string Id, string Range)[] dependencies)
        => AddWithFiles(id, version, new Dictionary<string, string>(), dependencies);

    /// <summary>
    /// Like <see cref="Add"/>, plus extra package entries keyed by their path inside the package, for example
    /// <c>build/{id}.props</c> or <c>src/Bad.cs</c> (use forward slashes).
    /// </summary>
    public TestFeed AddWithFiles(
        string id,
        string version,
        IReadOnlyDictionary<string, string> files,
        params (string Id, string Range)[] dependencies)
    {
        var dependenciesXml = dependencies.Length == 0
            ? string.Empty
            : "<dependencies><group targetFramework=\".NETStandard2.0\">"
              + string.Concat(dependencies.Select(d =>
                  $"<dependency id=\"{SecurityElement.Escape(d.Id)}\" version=\"{SecurityElement.Escape(d.Range)}\" />"))
              + "</group></dependencies>";

        var nuspec = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                <authors>tests</authors>
                <description>Test package {id}</description>
                {dependenciesXml}
              </metadata>
            </package>
            """;

        using var stream = File.Create(System.IO.Path.Combine(Path, $"{id}.{version}.nupkg"));
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(zip, $"{id}.nuspec", nuspec);

        foreach (var (name, content) in files)
        {
            WriteEntry(zip, name, content);
        }

        return this;
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
