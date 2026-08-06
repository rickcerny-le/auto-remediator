using System.IO.Compression;
using System.Text;

namespace AutoRemediator.Infrastructure.Tests;

/// <summary>Builds in-memory zip archives shaped like the Azure DevOps repository-archive response.</summary>
internal static class TestArchive
{
    /// <summary>A zip containing the given repository-relative paths and contents.</summary>
    public static Stream Of(params (string Path, string Content)[] entries)
    {
        var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = zip.CreateEntry(path.TrimStart('/'));
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <summary>A minimal valid zip with a single project file, for tests that do not inspect the tree.</summary>
    public static Stream Empty() => Of(("Directory.Packages.props", "<Project />"));
}
