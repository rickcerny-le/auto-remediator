using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.AzureDevOps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Infrastructure.Verification;

/// <summary>
/// A temporary working tree for one run: the repository extracted from an archive, with the
/// computed manifest edits applied. Disposing deletes the directory.
/// </summary>
public interface IVerificationWorkspace : IDisposable
{
    /// <summary>Absolute path of the extracted repository root.</summary>
    string Root { get; }

    /// <summary>Reads a repository-relative path from the tree, or null when absent.</summary>
    Task<string?> ReadAsync(string repositoryRelativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Repository-relative paths written purely to support verification. These must never reach a
    /// commit — see <see cref="LockFileChanges"/> for the files that legitimately do.
    /// </summary>
    IReadOnlyCollection<string> GeneratedPaths { get; }

    /// <summary>
    /// `packages.lock.json` files that restore created or modified, as commit-ready changes with
    /// Azure DevOps-style rooted paths. Empty when the repository does not use lock files.
    /// </summary>
    Task<IReadOnlyList<FileChange>> LockFileChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// What to hand `dotnet restore`/`dotnet build`: the repository's solution when it has one,
    /// otherwise every project. Empty when the tree contains nothing buildable.
    /// </summary>
    IReadOnlyList<string> BuildTargets { get; }
}

/// <summary>Creates a <see cref="IVerificationWorkspace"/> for a run.</summary>
public interface IVerificationWorkspaceFactory
{
    /// <summary>
    /// Downloads the repository at <paramref name="commitId"/>, extracts it, applies
    /// <paramref name="edits"/>, and writes a `NuGet.config` for the configured feeds.
    /// </summary>
    Task<IVerificationWorkspace> CreateAsync(
        ManagedRepository repository,
        string commitId,
        IReadOnlyList<ChangedManifest> edits,
        TargetingSettings settings,
        CancellationToken cancellationToken = default);
}

internal sealed class VerificationWorkspaceFactory(
    IAzureDevOpsClient azureDevOps,
    IOptions<AzureDevOpsOptions> azureDevOpsOptions,
    ILogger<VerificationWorkspaceFactory> logger) : IVerificationWorkspaceFactory
{
    // Restore reuses the PAT already resolved for feed version lookups; no new secret.
    private readonly string? _pat = azureDevOpsOptions.Value.Pat;

    public async Task<IVerificationWorkspace> CreateAsync(
        ManagedRepository repository,
        string commitId,
        IReadOnlyList<ChangedManifest> edits,
        TargetingSettings settings,
        CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(Path.GetTempPath(), "autoremediator", Path.GetRandomFileName());
        var workspace = new VerificationWorkspace(root);

        try
        {
            Directory.CreateDirectory(root);

            await using var archive = await azureDevOps.GetRepositoryArchiveAsync(repository, commitId, cancellationToken);
            Extract(archive, root);

            var baseline = await workspace.SnapshotLockFilesAsync(cancellationToken);
            logger.LogDebug(
                "Extracted {Slug} at {Commit} to {Root} ({LockFiles} lock file(s)).",
                repository.Slug, commitId, root, baseline.Count);

            await workspace.ApplyEditsAsync(edits, cancellationToken);
            await workspace.WriteNuGetConfigAsync(settings, _pat, cancellationToken);

            return workspace;
        }
        catch
        {
            // A partially-built workspace still owns a directory.
            workspace.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Extracts the archive, stripping the single wrapper directory Azure DevOps adds when the
    /// archive is scoped to the repository root, so paths stay repository-relative.
    /// </summary>
    private static void Extract(Stream archive, string root)
    {
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read);

        var prefix = WrapperDirectory(zip);

        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.Length == 0 && entry.Name.Length == 0)
            {
                continue;
            }

            var relative = prefix is null ? entry.FullName : entry.FullName[prefix.Length..];
            if (relative.Length == 0)
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

            // Zip-slip guard: never write outside the workspace root.
            if (!destination.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    /// <summary>
    /// The wrapper directory the archive nests everything under, or null when there is none.
    ///
    /// Deliberately conservative: a single shared top-level directory is not enough, because plenty of
    /// repositories keep all their content under one folder (`src/`), and stripping that would corrupt
    /// every path. Only a directory whose name is not a recognizable source folder is treated as a
    /// wrapper — getting this wrong silently relocates the tree.
    /// </summary>
    private static string? WrapperDirectory(ZipArchive zip)
    {
        string? candidate = null;

        foreach (var entry in zip.Entries)
        {
            var slash = entry.FullName.IndexOf('/');
            if (slash < 0)
            {
                return null; // A root-level file means there is no wrapper directory.
            }

            var top = entry.FullName[..(slash + 1)];
            if (candidate is null)
            {
                candidate = top;
            }
            else if (!string.Equals(candidate, top, StringComparison.Ordinal))
            {
                return null;
            }
        }

        if (candidate is null)
        {
            return null;
        }

        var name = candidate.TrimEnd('/');
        return SourceDirectoryNames.Contains(name) ? null : candidate;
    }

    /// <summary>Top-level folder names that belong to the repository rather than to the archive.</summary>
    private static readonly HashSet<string> SourceDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "src", "source", "sources", "lib", "libs", "app", "apps", "test", "tests", "samples", "build", "eng", "tools",
    };
}

internal sealed class VerificationWorkspace(string root) : IVerificationWorkspace
{
    private const string LockFileName = "packages.lock.json";

    private readonly HashSet<string> _generated = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _lockFileBaseline = new(StringComparer.OrdinalIgnoreCase);

    public string Root => root;

    public IReadOnlyCollection<string> GeneratedPaths => _generated;

    /// <summary>
    /// A bare `dotnet restore` only works when the working directory holds exactly one project or
    /// solution; plenty of repositories keep neither at the root, where it fails with MSB1003 for a
    /// reason that has nothing to do with the bump. So the target is resolved explicitly: the
    /// shallowest solution if there is one, otherwise every project.
    /// </summary>
    public IReadOnlyList<string> BuildTargets => _buildTargets ??= DiscoverBuildTargets();

    private IReadOnlyList<string>? _buildTargets;

    private IReadOnlyList<string> DiscoverBuildTargets()
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var solutions = Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Depth)
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (solutions.Count > 0)
        {
            return [solutions[0]];
        }

        return Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private int Depth(string path)
        => Path.GetRelativePath(root, path).Count(c => c == Path.DirectorySeparatorChar || c == '/');

    public async Task<string?> ReadAsync(string repositoryRelativePath, CancellationToken cancellationToken = default)
    {
        var path = Resolve(repositoryRelativePath);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }

    /// <summary>
    /// Writes the edited manifest contents over their counterparts in the tree.
    ///
    /// Every edit must land on a file that the archive actually contained. Creating a missing one
    /// instead would be the worst possible failure: the edit would go to a path nothing builds, the
    /// unedited tree would compile, and the run would report the change as verified.
    /// </summary>
    public async Task ApplyEditsAsync(IReadOnlyList<ChangedManifest> edits, CancellationToken cancellationToken)
    {
        foreach (var edit in edits)
        {
            var path = Resolve(edit.Path);

            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"'{edit.Path}' is not present in the extracted tree, so the change cannot be verified against it.");
            }

            await File.WriteAllTextAsync(path, edit.NewContent, cancellationToken);
        }
    }

    /// <summary>
    /// Writes a `NuGet.config` declaring the configured feeds with PAT credentials. The repository's
    /// own sources are preserved after the configured ones — `clear` controls ordering and credentials
    /// without dropping a third-party source the repository legitimately needs.
    /// </summary>
    public async Task WriteNuGetConfigAsync(TargetingSettings settings, string? pat, CancellationToken cancellationToken)
    {
        var existing = ExistingSources();
        var sources = new XElement("packageSources", new XElement("clear"));
        var credentials = new XElement("packageSourceCredentials");

        var index = 0;
        foreach (var feed in settings.Feeds)
        {
            var key = $"autoremediator{++index}";
            sources.Add(new XElement("add", new XAttribute("key", key), new XAttribute("value", feed)));

            if (!string.IsNullOrEmpty(pat))
            {
                credentials.Add(new XElement(
                    key,
                    new XElement("add", new XAttribute("key", "Username"), new XAttribute("value", "pat")),
                    new XElement("add", new XAttribute("key", "ClearTextPassword"), new XAttribute("value", pat))));
            }
        }

        foreach (var (key, value) in existing)
        {
            sources.Add(new XElement("add", new XAttribute("key", key), new XAttribute("value", value)));
        }

        var config = new XElement("configuration", sources);
        if (credentials.HasElements)
        {
            config.Add(credentials);
        }

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), config);
        var path = Resolve("NuGet.config");
        await File.WriteAllTextAsync(path, document.ToString(), Encoding.UTF8, cancellationToken);
        _generated.Add("NuGet.config");
    }

    /// <summary>
    /// Reads the repository's own `packageSources` before they are overwritten, so a legitimately
    /// needed third-party source is not lost. Returns empty when there is no config to read.
    /// </summary>
    private List<KeyValuePair<string, string>> ExistingSources()
    {
        var sources = new List<KeyValuePair<string, string>>();

        var path = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), "NuGet.config", StringComparison.OrdinalIgnoreCase));

        if (path is null)
        {
            return sources;
        }

        try
        {
            var document = XDocument.Load(path);
            foreach (var add in document.Root?.Element("packageSources")?.Elements("add") ?? [])
            {
                var key = add.Attribute("key")?.Value;
                var value = add.Attribute("value")?.Value;
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                {
                    sources.Add(new KeyValuePair<string, string>(key, value));
                }
            }
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            // An unreadable config is not worth failing verification over; the generated
            // sources alone still resolve the managed packages.
        }

        return sources;
    }

    /// <summary>Records lock-file contents before restore, so changes can be detected afterwards.</summary>
    public async Task<IReadOnlyDictionary<string, string>> SnapshotLockFilesAsync(CancellationToken cancellationToken)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in EnumerateLockFiles())
        {
            snapshot[path] = await File.ReadAllTextAsync(path, cancellationToken);
        }

        _lockFileBaseline = snapshot;
        return snapshot;
    }

    public async Task<IReadOnlyList<FileChange>> LockFileChangesAsync(CancellationToken cancellationToken = default)
    {
        var changes = new List<FileChange>();

        foreach (var path in EnumerateLockFiles())
        {
            var content = await File.ReadAllTextAsync(path, cancellationToken);

            // Only lock files the repository already committed are pushed back. Restore can create
            // one where none existed; introducing it would change the repository's build contract.
            if (!_lockFileBaseline.TryGetValue(path, out var before) || string.Equals(before, content, StringComparison.Ordinal))
            {
                continue;
            }

            changes.Add(new FileChange(ToRepositoryPath(path), content));
        }

        return changes;
    }

    private IEnumerable<string> EnumerateLockFiles()
        => Directory.Exists(root)
            ? Directory.EnumerateFiles(root, LockFileName, SearchOption.AllDirectories)
            : [];

    /// <summary>Maps an absolute workspace path to the rooted, forward-slashed form the ADO API expects.</summary>
    private string ToRepositoryPath(string absolutePath)
        => "/" + Path.GetRelativePath(root, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    private string Resolve(string repositoryRelativePath)
        => Path.Combine(root, repositoryRelativePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a locked file must not turn cleanup into a run failure. The
            // directory is under the OS temp path and will be reclaimed.
        }
    }
}
