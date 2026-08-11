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
    public async Task A_repository_kept_entirely_under_src_is_not_stripped()
    {
        // Everything shares a top-level directory, but `src` belongs to the repository. Stripping it
        // would relocate the whole tree and make every manifest path wrong.
        using var workspace = await CreateAsync(() => TestArchive.Of(
            ("src/Directory.Packages.props", "<Project />"),
            ("src/Web/Web.csproj", "<Project />")));

        Assert.NotNull(await workspace.ReadAsync("src/Directory.Packages.props", TestContext.Current.CancellationToken));
        Assert.NotNull(await workspace.ReadAsync("src/Web/Web.csproj", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_edit_for_a_file_absent_from_the_tree_fails_loudly()
    {
        // Silently creating it would verify an unedited tree and report the change as verified.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync(
            () => TestArchive.Of(("Directory.Packages.props", "<Project />")),
            [new ChangedManifest("/src/Nope/Nope.csproj", "<Project />")]));

        Assert.Contains("not present in the extracted tree", ex.Message);
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
    public async Task A_solution_is_preferred_over_the_individual_projects()
    {
        using var workspace = await CreateAsync(() => TestArchive.Of(
            ("App.sln", "Microsoft Visual Studio Solution File"),
            ("src/A/A.csproj", "<Project />"),
            ("src/B/B.csproj", "<Project />")));

        var target = Assert.Single(workspace.BuildTargets);
        Assert.EndsWith("App.sln", target);
    }

    [Fact]
    public async Task The_shallowest_solution_wins()
    {
        using var workspace = await CreateAsync(() => TestArchive.Of(
            ("build/Nested.sln", "nested"),
            ("Root.sln", "root"),
            ("src/A/A.csproj", "<Project />")));

        Assert.EndsWith("Root.sln", Assert.Single(workspace.BuildTargets));
    }

    [Fact]
    public async Task Every_project_is_a_target_when_there_is_no_solution()
    {
        using var workspace = await CreateAsync(() => TestArchive.Of(
            ("src/A/A.csproj", "<Project />"),
            ("src/B/B.csproj", "<Project />")));

        Assert.Equal(2, workspace.BuildTargets.Count);
        Assert.Contains(workspace.BuildTargets, t => t.EndsWith("A.csproj", StringComparison.Ordinal));
        Assert.Contains(workspace.BuildTargets, t => t.EndsWith("B.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_tree_with_nothing_buildable_has_no_targets()
    {
        using var workspace = await CreateAsync(() => TestArchive.Of(("README.md", "# just docs")));

        Assert.Empty(workspace.BuildTargets);
    }

    // ---- Edit safety boundary --------------------------------------------------------

    /// <summary>A tree with the shapes the boundary has to discriminate between.</summary>
    private static Task<IVerificationWorkspace> EditableWorkspaceAsync() => CreateAsync(() => TestArchive.Of(
        ("Directory.Packages.props", "<Project />"),
        ("Directory.Build.props", "<Project />"),
        ("src/App/App.csproj", "<Project />"),
        ("src/App/Program.cs", "public static class Program { public static void Main() { } }"),
        ("src/App/packages.lock.json", """{ "version": 1 }""")));

    [Fact]
    public async Task A_source_edit_inside_the_tree_is_applied()
    {
        using var workspace = await EditableWorkspaceAsync();

        var result = await workspace.ApplyProposedEditAsync(
            new ProposedEdit("src/App/Program.cs", "// repaired"), TestContext.Current.CancellationToken);

        Assert.True(result.Applied);
        Assert.Null(result.RejectionReason);
        Assert.Equal("// repaired", await workspace.ReadAsync("src/App/Program.cs", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_applied_edit_becomes_a_commit_ready_change()
    {
        using var workspace = await EditableWorkspaceAsync();

        await workspace.ApplyProposedEditAsync(
            new ProposedEdit("src/App/Program.cs", "// repaired"), TestContext.Current.CancellationToken);

        var change = Assert.Single(await workspace.AppliedEditChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal("/src/App/Program.cs", change.Path);
        Assert.Equal("// repaired", change.Content);
    }

    [Fact]
    public async Task No_applied_edits_means_no_changes()
    {
        using var workspace = await EditableWorkspaceAsync();

        Assert.Empty(await workspace.AppliedEditChangesAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../escaped.cs", "outside the workspace root")]
    [InlineData("src/../../escaped.cs", "outside the workspace root")]
    [InlineData("src/App/Nope.cs", "not present in the extracted tree")]
    [InlineData("Directory.Packages.props", "not the agent's to edit")]
    [InlineData("Directory.Build.props", "not the agent's to edit")]
    [InlineData("src/App/App.csproj", "not the agent's to edit")]
    [InlineData("src/App/packages.lock.json", "not the agent's to edit")]
    [InlineData("NuGet.config", "not the agent's to edit")]
    [InlineData("", "the path was empty")]
    public async Task A_disallowed_edit_is_rejected_with_a_reason(string path, string expectedReason)
    {
        using var workspace = await EditableWorkspaceAsync();

        var result = await workspace.ApplyProposedEditAsync(
            new ProposedEdit(path, "malicious or mistaken"), TestContext.Current.CancellationToken);

        Assert.False(result.Applied);
        Assert.Contains(expectedReason, result.RejectionReason);
        Assert.Empty(await workspace.AppliedEditChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_rejected_manifest_edit_does_not_change_the_file()
    {
        using var workspace = await EditableWorkspaceAsync();

        await workspace.ApplyProposedEditAsync(
            new ProposedEdit("Directory.Packages.props", "<Project>tampered</Project>"), TestContext.Current.CancellationToken);

        Assert.Equal("<Project />", await workspace.ReadAsync("Directory.Packages.props", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_escaping_edit_writes_nothing_outside_the_root()
    {
        using var workspace = await EditableWorkspaceAsync();
        var outside = Path.Combine(Directory.GetParent(workspace.Root)!.FullName, "escaped.cs");

        await workspace.ApplyProposedEditAsync(
            new ProposedEdit("../escaped.cs", "should not exist"), TestContext.Current.CancellationToken);

        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task The_generated_nuget_config_is_rejected_even_though_it_exists()
    {
        // It is present in the tree, so only the generated-artifact rule stops it.
        using var workspace = await CreateAsync(() => TestArchive.Of(("src/App/Program.cs", "// code")));

        Assert.Contains("NuGet.config", workspace.GeneratedPaths);

        var result = await workspace.ApplyProposedEditAsync(
            new ProposedEdit("NuGet.config", "<configuration />"), TestContext.Current.CancellationToken);

        Assert.False(result.Applied);
    }

    [Fact]
    public async Task Backslashed_and_rooted_paths_still_resolve()
    {
        using var workspace = await EditableWorkspaceAsync();

        var result = await workspace.ApplyProposedEditAsync(
            new ProposedEdit("/src\\App\\Program.cs", "// repaired"), TestContext.Current.CancellationToken);

        Assert.True(result.Applied);
        Assert.Equal("/src/App/Program.cs", Assert.Single(await workspace.AppliedEditChangesAsync(TestContext.Current.CancellationToken)).Path);
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
