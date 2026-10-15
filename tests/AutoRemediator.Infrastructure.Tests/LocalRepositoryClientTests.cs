// Each call is bounded by git itself; threading a test cancellation token through every client call adds noise only.
#pragma warning disable xUnit1051
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.AzureDevOps;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Infrastructure.Tests;

public sealed class LocalRepositoryClientTests : IDisposable
{
    private const string Props = "<Project><ItemGroup><PackageVersion Include=\"Contoso.Core\" Version=\"1.0.0\" /></ItemGroup></Project>";
    private const string Csproj = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";

    private readonly string _base = Path.Combine(Path.GetTempPath(), "lrc-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;
    private readonly string _prs;
    private readonly string _repoPath;
    private readonly ManagedRepository _repo = new(Guid.NewGuid(), "org", "proj", "demo");
    private readonly LocalRepositoryClient? _client;

    public LocalRepositoryClientTests()
    {
        _root = Path.Combine(_base, "repos");
        _prs = Path.Combine(_base, "prs");
        _repoPath = Path.Combine(_root, "demo");

        if (!GitAvailable())
        {
            return;
        }
        Directory.CreateDirectory(_repoPath);

        Git(_repoPath, "init", "-b", "main");
        Git(_repoPath, "config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(_repoPath, "Directory.Packages.props"), Props);
        File.WriteAllText(Path.Combine(_repoPath, "readme.md"), "hi");
        Directory.CreateDirectory(Path.Combine(_repoPath, "src"));
        File.WriteAllText(Path.Combine(_repoPath, "src", "App.csproj"), Csproj);
        File.WriteAllText(Path.Combine(_repoPath, "src", "notes.txt"), "not a manifest");
        Git(_repoPath, "add", "-A");
        Git(_repoPath, "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false", "commit", "-m", "init");

        _client = new LocalRepositoryClient(
            Options.Create(new AzureDevOpsOptions { Mode = "Local", LocalRoot = _root, LocalPullRequestsRoot = _prs }),
            TimeProvider.System,
            NullLogger<LocalRepositoryClient>.Instance);
    }

    private LocalRepositoryClient Client
    {
        get
        {
            if (_client is null)
            {
                Assert.Skip("git was not found on PATH.");
            }

            return _client;
        }
    }

    [Fact]
    public async Task Repository_exists_only_for_a_git_repo_with_a_safe_name()
    {
        Assert.True(await Client.RepositoryExistsAsync(_repo));
        Assert.False(await Client.RepositoryExistsAsync(new ManagedRepository(Guid.NewGuid(), "o", "p", "other")));

        // A plain folder is not a repo, even though it sits inside the temp tree.
        Directory.CreateDirectory(Path.Combine(_root, "plain"));
        Assert.False(await Client.RepositoryExistsAsync(new ManagedRepository(Guid.NewGuid(), "o", "p", "plain")));

        Assert.False(await Client.RepositoryExistsAsync(new ManagedRepository(Guid.NewGuid(), "o", "p", "..\\x")));
        Assert.False(await Client.RepositoryExistsAsync(new ManagedRepository(Guid.NewGuid(), "o", "p", "../demo")));
        Assert.False(await Client.RepositoryExistsAsync(new ManagedRepository(Guid.NewGuid(), "o", "p", "a/b")));
    }

    [Fact]
    public async Task Manifests_come_from_the_target_branch_with_rooted_paths()
    {
        var manifests = await Client.GetManifestsAsync(_repo);

        Assert.Equal(["/Directory.Packages.props", "/src/App.csproj"], manifests.Select(m => m.Path).Order().ToArray());
        Assert.Equal(Props, manifests.Single(m => m.Path == "/Directory.Packages.props").Content);
        Assert.DoesNotContain(manifests, m => m.Path.EndsWith("notes.txt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Manifests_are_empty_for_a_missing_branch_or_repo()
    {
        var noBranch = new ManagedRepository(Guid.NewGuid(), "o", "p", "demo", targetBranch: "develop");
        var noRepo = new ManagedRepository(Guid.NewGuid(), "o", "p", "nothing");

        Assert.Empty(await Client.GetManifestsAsync(noBranch));
        Assert.Empty(await Client.GetManifestsAsync(noRepo));
    }

    [Fact]
    public async Task Branch_head_is_returned_and_null_when_missing()
    {
        var head = await Client.GetBranchHeadAsync(_repo, "main");

        Assert.Equal(Git(_repoPath, "rev-parse", "main").Trim(), head);
        Assert.Null(await Client.GetBranchHeadAsync(_repo, "nope"));
        Assert.Null(await Client.GetBranchHeadAsync(new ManagedRepository(Guid.NewGuid(), "o", "p", "nothing"), "main"));
    }

    [Fact]
    public async Task Archive_has_committed_files_only()
    {
        File.WriteAllText(Path.Combine(_repoPath, "uncommitted.txt"), "x");
        var head = (await Client.GetBranchHeadAsync(_repo, "main"))!;

        using var zip = new ZipArchive(await Client.GetRepositoryArchiveAsync(_repo, head));

        Assert.Contains(zip.Entries, e => e.FullName == "readme.md");
        Assert.Contains(zip.Entries, e => e.FullName == "src/App.csproj");
        Assert.DoesNotContain(zip.Entries, e => e.FullName == "uncommitted.txt");
    }

    [Fact]
    public async Task Archive_stream_is_seekable_and_at_the_start()
    {
        var head = (await Client.GetBranchHeadAsync(_repo, "main"))!;

        await using var stream = await Client.GetRepositoryArchiveAsync(_repo, head);

        Assert.True(stream.CanSeek);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task Push_creates_the_branch_without_touching_main_or_the_working_tree()
    {
        var head = (await Client.GetBranchHeadAsync(_repo, "main"))!;

        await Client.PushFilesAsync(_repo, "update", head, [new FileChange("/Directory.Packages.props", Props.Replace("1.0.0", "2.0.0"))], "bump");

        Assert.Equal(head, await Client.GetBranchHeadAsync(_repo, "main"));
        var branchHead = await Client.GetBranchHeadAsync(_repo, "update");
        Assert.NotNull(branchHead);
        Assert.NotEqual(head, branchHead);
        Assert.Contains("2.0.0", Git(_repoPath, "show", "update:Directory.Packages.props"));
        Assert.Equal(Props, File.ReadAllText(Path.Combine(_repoPath, "Directory.Packages.props")));
        Assert.Equal(string.Empty, Git(_repoPath, "status", "--porcelain").Trim());
        Assert.Equal("main", Git(_repoPath, "rev-parse", "--abbrev-ref", "HEAD").Trim());
    }

    [Fact]
    public async Task Pushed_content_is_utf8_without_a_bom()
    {
        var head = (await Client.GetBranchHeadAsync(_repo, "main"))!;

        await Client.PushFilesAsync(_repo, "update", head, [new FileChange("/Directory.Packages.props", Props.Replace("1.0.0", "2.0.0"))], "bump");

        var blob = Git(_repoPath, "show", "update:Directory.Packages.props");
        Assert.DoesNotContain('﻿', blob);
        Assert.StartsWith("<Project>", blob, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_push_based_on_the_branch_head_advances_it()
    {
        var main = (await Client.GetBranchHeadAsync(_repo, "main"))!;
        await Client.PushFilesAsync(_repo, "update", main, [new FileChange("/Directory.Packages.props", Props.Replace("1.0.0", "2.0.0"))], "bump");
        var first = (await Client.GetBranchHeadAsync(_repo, "update"))!;

        await Client.PushFilesAsync(_repo, "update", first, [new FileChange("/Directory.Packages.props", Props.Replace("1.0.0", "3.0.0"))], "bump again");

        var second = await Client.GetBranchHeadAsync(_repo, "update");
        Assert.NotEqual(first, second);
        Assert.Equal(first, Git(_repoPath, "rev-parse", "update~1").Trim());
        Assert.Contains("3.0.0", Git(_repoPath, "show", "update:Directory.Packages.props"));
    }

    [Fact]
    public async Task Push_with_a_stale_base_is_rejected()
    {
        var main = (await Client.GetBranchHeadAsync(_repo, "main"))!;
        await Client.PushFilesAsync(_repo, "update", main, [new FileChange("/Directory.Packages.props", Props.Replace("1.0.0", "2.0.0"))], "bump");

        // The branch now exists and has moved past main, so a push still based on main is stale.
        var ex = await Assert.ThrowsAsync<PushRejectedException>(() =>
            Client.PushFilesAsync(_repo, "update", main, [new FileChange("/Directory.Packages.props", Props)], "late"));

        Assert.Equal("GitRefUpdateStaleException", ex.TypeKey);
    }

    [Fact]
    public async Task Push_of_a_missing_file_is_rejected()
    {
        var head = (await Client.GetBranchHeadAsync(_repo, "main"))!;

        var ex = await Assert.ThrowsAsync<PushRejectedException>(() =>
            Client.PushFilesAsync(_repo, "update", head, [new FileChange("/gone.csproj", "x")], "bump"));

        Assert.Equal("GitItemNotFoundException", ex.TypeKey);
        Assert.Null(await Client.GetBranchHeadAsync(_repo, "update"));
    }

    [Fact]
    public async Task Push_refuses_a_path_that_escapes_the_worktree()
    {
        var head = (await Client.GetBranchHeadAsync(_repo, "main"))!;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Client.PushFilesAsync(_repo, "update", head, [new FileChange("/../outside.txt", "x")], "bump"));
    }

    [Fact]
    public async Task No_worktree_is_left_behind()
    {
        var head = (await Client.GetBranchHeadAsync(_repo, "main"))!;
        await Client.PushFilesAsync(_repo, "update", head, [new FileChange("/Directory.Packages.props", Props.Replace("1.0.0", "2.0.0"))], "bump");
        await Assert.ThrowsAsync<PushRejectedException>(() =>
            Client.PushFilesAsync(_repo, "other", head, [new FileChange("/gone.csproj", "x")], "bump"));

        var worktrees = Git(_repoPath, "worktree", "list").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Single(worktrees);
    }

    [Fact]
    public async Task Ensure_pull_request_writes_a_record_with_commits_and_a_file_url()
    {
        var main = (await Client.GetBranchHeadAsync(_repo, "main"))!;
        await Client.PushFilesAsync(_repo, "update", main, [new FileChange("/Directory.Packages.props", Props.Replace("1.0.0", "2.0.0"))], "bump");
        var head = (await Client.GetBranchHeadAsync(_repo, "update"))!;

        var url = await Client.EnsurePullRequestAsync(_repo, "update", "main", "Title", "Body");

        Assert.StartsWith("file:///", url);
        var recordPath = Path.Combine(_prs, "demo", "1.json");
        Assert.True(File.Exists(recordPath));
        var record = ReadRecord(recordPath);
        Assert.Equal(1, record.GetProperty("id").GetInt32());
        Assert.Equal("org", record.GetProperty("organization").GetString());
        Assert.Equal("proj", record.GetProperty("project").GetString());
        Assert.Equal("demo", record.GetProperty("repository").GetString());
        Assert.Equal("update", record.GetProperty("sourceBranch").GetString());
        Assert.Equal("main", record.GetProperty("targetBranch").GetString());
        Assert.Equal(main, record.GetProperty("baseCommit").GetString());
        Assert.Equal(head, record.GetProperty("headCommit").GetString());
        Assert.Equal("Title", record.GetProperty("title").GetString());
        Assert.Equal("Body", record.GetProperty("description").GetString());
        Assert.Equal("active", record.GetProperty("status").GetString());
        Assert.Equal(url, record.GetProperty("url").GetString());
        Assert.Equal(record.GetProperty("createdAtUtc").GetDateTimeOffset(), record.GetProperty("updatedAtUtc").GetDateTimeOffset());
        Assert.Equal(new Uri(recordPath).AbsoluteUri, url);
    }

    [Fact]
    public async Task Ensure_pull_request_is_idempotent_and_refreshes_the_head()
    {
        var main = (await Client.GetBranchHeadAsync(_repo, "main"))!;
        await Client.PushFilesAsync(_repo, "update", main, [new FileChange("/Directory.Packages.props", Props.Replace("1.0.0", "2.0.0"))], "bump");
        var first = await Client.EnsurePullRequestAsync(_repo, "update", "main", "Title", "Body");
        var before = ReadRecord(Path.Combine(_prs, "demo", "1.json"));

        var pushed = (await Client.GetBranchHeadAsync(_repo, "update"))!;
        await Client.PushFilesAsync(_repo, "update", pushed, [new FileChange("/Directory.Packages.props", Props.Replace("1.0.0", "3.0.0"))], "bump again");
        var newHead = (await Client.GetBranchHeadAsync(_repo, "update"))!;
        var second = await Client.EnsurePullRequestAsync(_repo, "update", "main", "Other title", "Other body");

        Assert.Equal(first, second);
        Assert.False(File.Exists(Path.Combine(_prs, "demo", "2.json")));
        var after = ReadRecord(Path.Combine(_prs, "demo", "1.json"));
        Assert.Equal(newHead, after.GetProperty("headCommit").GetString());
        Assert.Equal(main, after.GetProperty("baseCommit").GetString());
        Assert.Equal(before.GetProperty("id").GetInt32(), after.GetProperty("id").GetInt32());
        Assert.Equal(before.GetProperty("url").GetString(), after.GetProperty("url").GetString());
        Assert.Equal(before.GetProperty("createdAtUtc").GetDateTimeOffset(), after.GetProperty("createdAtUtc").GetDateTimeOffset());
        Assert.Equal("Title", after.GetProperty("title").GetString());
        Assert.Equal("Body", after.GetProperty("description").GetString());
    }

    [Fact]
    public async Task A_different_target_branch_gets_a_new_record()
    {
        var main = (await Client.GetBranchHeadAsync(_repo, "main"))!;
        Git(_repoPath, "branch", "develop", main);
        await Client.PushFilesAsync(_repo, "update", main, [new FileChange("/Directory.Packages.props", Props.Replace("1.0.0", "2.0.0"))], "bump");

        var toMain = await Client.EnsurePullRequestAsync(_repo, "update", "main", "t", "d");
        var toDevelop = await Client.EnsurePullRequestAsync(_repo, "update", "develop", "t", "d");

        Assert.NotEqual(toMain, toDevelop);
        Assert.True(File.Exists(Path.Combine(_prs, "demo", "1.json")));
        Assert.Equal("develop", ReadRecord(Path.Combine(_prs, "demo", "2.json")).GetProperty("targetBranch").GetString());
    }

    [Fact]
    public async Task Ensure_pull_request_fails_when_a_branch_is_missing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client.EnsurePullRequestAsync(_repo, "update", "main", "t", "d"));
    }

    [Fact]
    public void Pull_request_root_defaults_to_a_prs_folder_beside_the_repos()
    {
        var options = new AzureDevOpsOptions { LocalRoot = Path.Combine("x", "repos") + Path.DirectorySeparatorChar };

        Assert.Equal(Path.Combine(Path.GetFullPath("x"), "prs"), options.EffectiveLocalPullRequestsRoot);
        Assert.True(new AzureDevOpsOptions { Mode = "local" }.IsLocal);
        Assert.False(new AzureDevOpsOptions().IsLocal);
    }

    private static JsonElement ReadRecord(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static bool GitAvailable()
    {
        try
        {
            var info = new ProcessStartInfo("git", "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var process = Process.Start(info);
            process!.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string Git(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        using var p = Process.Start(info)!;
        var error = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)}: {error.Result}");
        return output;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_base))
            {
                foreach (var file in Directory.EnumerateFiles(_base, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(_base, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
