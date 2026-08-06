using System.Xml.Linq;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Verification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Infrastructure.Tests;

public class VerificationWorkspaceTests
{
    private static readonly ManagedRepository Repo = new(Guid.NewGuid(), "orion180", "platform", "web-api");

    private static TargetingSettings Settings => new(["Orion180.*"], feeds: ["https://feed/v3/index.json"]);

    private static VerificationWorkspaceFactory Factory(Func<Stream> archive, string? pat = "secret-pat") =>
        new(new ArchiveAdo(archive),
            Options.Create(new AzureDevOpsOptions { Pat = pat }),
            NullLogger<VerificationWorkspaceFactory>.Instance);

    private static async Task<IVerificationWorkspace> CreateAsync(
        Func<Stream> archive,
        IReadOnlyList<ChangedManifest>? edits = null,
        string? pat = "secret-pat")
        => await Factory(archive, pat).CreateAsync(
            Repo, "commit-abc", edits ?? [], Settings, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Extracts_the_tree_to_a_temporary_directory()
    {
        using var workspace = await CreateAsync(() => TestArchive.Of(
            ("Directory.Packages.props", "<Project />"),
            ("src/Web/Web.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />")));

        Assert.True(Directory.Exists(workspace.Root));
        Assert.Equal("<Project />", await workspace.ReadAsync("Directory.Packages.props", TestContext.Current.CancellationToken));
        Assert.NotNull(await workspace.ReadAsync("/src/Web/Web.csproj", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Strips_the_wrapper_directory_the_archive_adds()
    {
        using var workspace = await CreateAsync(() => TestArchive.Of(
            ("web-api/Directory.Packages.props", "<Project />"),
            ("web-api/src/Web/Web.csproj", "<Project />")));

        Assert.Equal("<Project />", await workspace.ReadAsync("Directory.Packages.props", TestContext.Current.CancellationToken));
        Assert.NotNull(await workspace.ReadAsync("src/Web/Web.csproj", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Keeps_top_level_directories_when_there_is_no_single_wrapper()
    {
        using var workspace = await CreateAsync(() => TestArchive.Of(
            ("src/A/A.csproj", "<Project />"),
            ("tests/B/B.csproj", "<Project />")));

        Assert.NotNull(await workspace.ReadAsync("src/A/A.csproj", TestContext.Current.CancellationToken));
        Assert.NotNull(await workspace.ReadAsync("tests/B/B.csproj", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Applies_the_computed_edits_over_the_extracted_manifests()
    {
        using var workspace = await CreateAsync(
            () => TestArchive.Of(("Directory.Packages.props", "<Project><PackageVersion Include=\"Orion180.Core\" Version=\"1.0.0\" /></Project>")),
            [new ChangedManifest("/Directory.Packages.props", "<Project><PackageVersion Include=\"Orion180.Core\" Version=\"2.0.0\" /></Project>")]);

        var content = await workspace.ReadAsync("Directory.Packages.props", TestContext.Current.CancellationToken);
        Assert.Contains("2.0.0", content);
        Assert.DoesNotContain("1.0.0", content);
    }

    [Fact]
    public async Task Writes_a_NuGet_config_with_the_configured_feed_and_pat()
    {
        using var workspace = await CreateAsync(() => TestArchive.Empty());

        var config = await workspace.ReadAsync("NuGet.config", TestContext.Current.CancellationToken);
        Assert.NotNull(config);

        var document = XDocument.Parse(config!);
        var sources = document.Root!.Element("packageSources")!;
        Assert.NotNull(sources.Element("clear"));
        Assert.Contains(sources.Elements("add"), e => e.Attribute("value")?.Value == "https://feed/v3/index.json");
        Assert.Contains(document.Root.Element("packageSourceCredentials")!.Descendants("add"),
            e => e.Attribute("value")?.Value == "secret-pat");
    }

    [Fact]
    public async Task Preserves_the_repositorys_own_sources_after_the_configured_ones()
    {
        var existing = """
            <configuration>
              <packageSources>
                <add key="thirdparty" value="https://thirdparty/v3/index.json" />
              </packageSources>
            </configuration>
            """;

        using var workspace = await CreateAsync(() => TestArchive.Of(
            ("NuGet.config", existing),
            ("Directory.Packages.props", "<Project />")));

        var config = await workspace.ReadAsync("NuGet.config", TestContext.Current.CancellationToken);
        var values = XDocument.Parse(config!).Root!.Element("packageSources")!
            .Elements("add").Select(e => e.Attribute("value")?.Value).ToList();

        Assert.Equal(["https://feed/v3/index.json", "https://thirdparty/v3/index.json"], values);
    }

    [Fact]
    public async Task Omits_credentials_when_no_pat_is_configured()
    {
        using var workspace = await CreateAsync(() => TestArchive.Empty(), pat: null);

        var config = await workspace.ReadAsync("NuGet.config", TestContext.Current.CancellationToken);
        Assert.Null(XDocument.Parse(config!).Root!.Element("packageSourceCredentials"));
    }

    [Fact]
    public async Task Generated_config_is_marked_as_not_committable()
    {
        using var workspace = await CreateAsync(() => TestArchive.Empty());

        Assert.Contains("NuGet.config", workspace.GeneratedPaths);
    }

    [Fact]
    public async Task Modified_lock_file_becomes_a_commit_ready_change()
    {
        using var workspace = await CreateAsync(() => TestArchive.Of(
            ("Directory.Packages.props", "<Project />"),
            ("src/Web/packages.lock.json", """{ "version": 1, "dependencies": { "Orion180.Core": "1.0.0" } }""")));

        // Stand in for restore rewriting the lock file.
        var lockPath = Path.Combine(workspace.Root, "src", "Web", "packages.lock.json");
        await File.WriteAllTextAsync(lockPath, """{ "version": 1, "dependencies": { "Orion180.Core": "2.0.0" } }""",
            TestContext.Current.CancellationToken);

        var changes = await workspace.LockFileChangesAsync(TestContext.Current.CancellationToken);

        var change = Assert.Single(changes);
        Assert.Equal("/src/Web/packages.lock.json", change.Path);
        Assert.Contains("2.0.0", change.Content);
    }

    [Fact]
    public async Task Untouched_lock_file_is_not_a_change()
    {
        using var workspace = await CreateAsync(() => TestArchive.Of(
            ("Directory.Packages.props", "<Project />"),
            ("packages.lock.json", """{ "version": 1 }""")));

        Assert.Empty(await workspace.LockFileChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Lock_file_created_where_none_existed_is_not_introduced()
    {
        using var workspace = await CreateAsync(() => TestArchive.Of(("Directory.Packages.props", "<Project />")));

        // Restore can emit a lock file even when the repository does not commit one.
        await File.WriteAllTextAsync(
            Path.Combine(workspace.Root, "packages.lock.json"), """{ "version": 1 }""",
            TestContext.Current.CancellationToken);

        Assert.Empty(await workspace.LockFileChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Dispose_deletes_the_workspace_directory()
    {
        var workspace = await CreateAsync(() => TestArchive.Empty());
        var root = workspace.Root;
        Assert.True(Directory.Exists(root));

        workspace.Dispose();

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task A_failed_download_leaves_no_directory_behind()
    {
        var before = TempDirectoryCount();

        await Assert.ThrowsAsync<HttpRequestException>(() => Factory(() => throw new HttpRequestException("boom"))
            .CreateAsync(Repo, "commit-abc", [], Settings, TestContext.Current.CancellationToken));

        Assert.Equal(before, TempDirectoryCount());
    }

    [Fact]
    public async Task A_corrupt_archive_leaves_no_directory_behind()
    {
        var before = TempDirectoryCount();

        await Assert.ThrowsAnyAsync<Exception>(() => Factory(() => new MemoryStream([1, 2, 3, 4, 5]))
            .CreateAsync(Repo, "commit-abc", [], Settings, TestContext.Current.CancellationToken));

        Assert.Equal(before, TempDirectoryCount());
    }

    [Fact]
    public async Task Entries_escaping_the_root_are_not_written()
    {
        using var workspace = await CreateAsync(() => TestArchive.Of(
            ("Directory.Packages.props", "<Project />"),
            ("../escaped.txt", "should not be written")));

        var escaped = Path.Combine(Directory.GetParent(workspace.Root)!.FullName, "escaped.txt");
        Assert.False(File.Exists(escaped));
    }

    private static int TempDirectoryCount()
    {
        var root = Path.Combine(Path.GetTempPath(), "autoremediator");
        return Directory.Exists(root) ? Directory.GetDirectories(root).Length : 0;
    }

    private sealed class ArchiveAdo(Func<Stream> archive) : IAzureDevOpsClient
    {
        public Task<bool> RepositoryExistsAsync(ManagedRepository r, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<RepositoryFile>> GetManifestsAsync(ManagedRepository r, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RepositoryFile>>([]);
        public Task<string?> GetBranchHeadAsync(ManagedRepository r, string branch, CancellationToken ct = default)
            => Task.FromResult<string?>("commit-abc");
        public Task<Stream> GetRepositoryArchiveAsync(ManagedRepository r, string commitId, CancellationToken ct = default)
            => Task.FromResult(archive());
        public Task PushFilesAsync(ManagedRepository r, string b, string c, IReadOnlyList<FileChange> ch, string m, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<string> EnsurePullRequestAsync(ManagedRepository r, string s, string t, string ti, string d, CancellationToken ct = default)
            => Task.FromResult("https://pr");
    }
}
