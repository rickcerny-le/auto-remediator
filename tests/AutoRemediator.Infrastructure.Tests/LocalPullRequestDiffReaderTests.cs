// Each call is bounded by git itself; threading a test cancellation token through every call adds noise only.
#pragma warning disable xUnit1051
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.AzureDevOps;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Infrastructure.Tests;

public sealed class LocalPullRequestDiffReaderTests : IDisposable
{
    private const string Props = "<Project><ItemGroup><PackageVersion Include=\"Contoso.Core\" Version=\"1.0.0\" /></ItemGroup></Project>";

    private readonly string _base = Path.Combine(Path.GetTempPath(), "lprd-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;
    private readonly string _prs;
    private readonly string _repoPath;
    private readonly ManagedRepository _repo = new(Guid.NewGuid(), "org", "proj", "demo");
    private readonly LocalPullRequestDiffReader? _reader;
    private readonly LocalRepositoryClient? _client;

    public LocalPullRequestDiffReaderTests()
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
        File.WriteAllText(Path.Combine(_repoPath, "readme.md"), "hello");
        File.WriteAllText(Path.Combine(_repoPath, "old.txt"), "to be deleted");
        Commit("init");

        var options = Options.Create(new AzureDevOpsOptions { Mode = "Local", LocalRoot = _root, LocalPullRequestsRoot = _prs });
        _reader = new LocalPullRequestDiffReader(options, NullLogger<LocalPullRequestDiffReader>.Instance);
        _client = new LocalRepositoryClient(options, TimeProvider.System, NullLogger<LocalRepositoryClient>.Instance);
    }

    private LocalPullRequestDiffReader Reader
    {
        get
        {
            if (_reader is null)
            {
                Assert.Skip("git was not found on PATH.");
            }

            return _reader;
        }
    }

    [Fact]
    public async Task Is_available_unlike_the_default_reader()
    {
        Assert.True(Reader.IsAvailable);
        Assert.False(new UnavailablePullRequestDiffReader().IsAvailable);
        Assert.Null(await new UnavailablePullRequestDiffReader().GetDiffAsync("file:///x.json", default));
    }

    [Fact]
    public async Task A_modified_file_matches_git_show()
    {
        var main = Git(_repoPath, "rev-parse", "main").Trim();
        await _client!.PushFilesAsync(_repo, "update", main, [new FileChange("/Directory.Packages.props", Props.Replace("1.0.0", "2.0.0"))], "bump");
        var url = await _client.EnsurePullRequestAsync(_repo, "update", "main", "t", "d");

        var diff = await Reader.GetDiffAsync(url, default);

        Assert.NotNull(diff);
        Assert.Equal("update", diff.SourceBranch);
        Assert.Equal("main", diff.TargetBranch);
        Assert.Equal(main, diff.BaseCommitId);
        Assert.Equal(Git(_repoPath, "rev-parse", "update").Trim(), diff.HeadCommitId);
        var file = Assert.Single(diff.Files);
        Assert.Equal("/Directory.Packages.props", file.Path);
        Assert.Equal(PullRequestChangeKind.Modified, file.ChangeKind);
        Assert.Equal(Git(_repoPath, "show", "main:Directory.Packages.props"), file.OriginalContent);
        Assert.Equal(Git(_repoPath, "show", "update:Directory.Packages.props"), file.NewContent);
        Assert.False(file.IsBinary);
        Assert.False(file.IsTruncated);
    }

    [Fact]
    public async Task Added_and_deleted_files_have_one_missing_side_and_are_sorted()
    {
        Git(_repoPath, "checkout", "-b", "feature");
        Directory.CreateDirectory(Path.Combine(_repoPath, "src"));
        File.WriteAllText(Path.Combine(_repoPath, "src", "new.txt"), "brand new");
        File.Delete(Path.Combine(_repoPath, "old.txt"));
        Commit("change");

        var diff = await Reader.GetDiffAsync(WriteRecord("feature"), default);

        Assert.NotNull(diff);
        Assert.Equal(["/old.txt", "/src/new.txt"], diff.Files.Select(f => f.Path).ToArray());
        var deleted = diff.Files[0];
        Assert.Equal(PullRequestChangeKind.Deleted, deleted.ChangeKind);
        Assert.Equal("to be deleted", deleted.OriginalContent);
        Assert.Null(deleted.NewContent);
        var added = diff.Files[1];
        Assert.Equal(PullRequestChangeKind.Added, added.ChangeKind);
        Assert.Null(added.OriginalContent);
        Assert.Equal("brand new", added.NewContent);
    }

    [Fact]
    public async Task A_binary_file_is_listed_without_content()
    {
        Git(_repoPath, "checkout", "-b", "feature");
        File.WriteAllBytes(Path.Combine(_repoPath, "logo.bin"), [1, 2, 0, 3, 4]);
        Commit("binary");

        var diff = await Reader.GetDiffAsync(WriteRecord("feature"), default);

        var file = Assert.Single(diff!.Files);
        Assert.Equal("/logo.bin", file.Path);
        Assert.True(file.IsBinary);
        Assert.Null(file.OriginalContent);
        Assert.Null(file.NewContent);
    }

    [Fact]
    public async Task Content_over_256_kb_is_cut_and_flagged()
    {
        Git(_repoPath, "checkout", "-b", "feature");
        File.WriteAllText(Path.Combine(_repoPath, "big.txt"), new string('x', 300 * 1024));
        File.WriteAllText(Path.Combine(_repoPath, "small.txt"), "small");
        Commit("big");

        var diff = await Reader.GetDiffAsync(WriteRecord("feature"), default);

        var big = diff!.Files.Single(f => f.Path == "/big.txt");
        Assert.True(big.IsTruncated);
        Assert.Equal(256 * 1024, big.NewContent!.Length);
        Assert.False(diff.Files.Single(f => f.Path == "/small.txt").IsTruncated);
    }

    [Fact]
    public async Task The_file_list_is_capped_at_200()
    {
        Git(_repoPath, "checkout", "-b", "feature");
        for (var i = 0; i < 205; i++)
        {
            File.WriteAllText(Path.Combine(_repoPath, $"f{i:000}.txt"), "x");
        }

        Commit("many");

        var diff = await Reader.GetDiffAsync(WriteRecord("feature"), default);

        Assert.Equal(200, diff!.Files.Count);
        Assert.Equal("/f000.txt", diff.Files[0].Path);
    }

    [Fact]
    public async Task Urls_that_are_not_pull_request_records_give_null()
    {
        Assert.Null(await Reader.GetDiffAsync("https://dev.azure.com/o/p/_git/r/pullrequest/1", default));
        Assert.Null(await Reader.GetDiffAsync("not a url", default));

        var outside = Path.Combine(_base, "outside.json");
        Directory.CreateDirectory(_base);
        File.WriteAllText(outside, "{}");
        Assert.Null(await Reader.GetDiffAsync(new Uri(outside).AbsoluteUri, default));

        // Traversal out of the records root is rejected even though the file exists.
        Directory.CreateDirectory(Path.Combine(_prs, "demo"));
        Assert.Null(await Reader.GetDiffAsync(new Uri(Path.Combine(_prs, "demo", "..", "..", "outside.json")).AbsoluteUri, default));

        // Missing file inside the root.
        Assert.Null(await Reader.GetDiffAsync(new Uri(Path.Combine(_prs, "demo", "9.json")).AbsoluteUri, default));
    }

    [Fact]
    public async Task A_malformed_record_gives_null()
    {
        Directory.CreateDirectory(Path.Combine(_prs, "demo"));
        var garbage = Path.Combine(_prs, "demo", "1.json");
        File.WriteAllText(garbage, "{ not json");
        Assert.Null(await Reader.GetDiffAsync(new Uri(garbage).AbsoluteUri, default));

        var empty = Path.Combine(_prs, "demo", "2.json");
        File.WriteAllText(empty, "{}");
        Assert.Null(await Reader.GetDiffAsync(new Uri(empty).AbsoluteUri, default));

        var injected = Path.Combine(_prs, "demo", "3.json");
        File.WriteAllText(injected, JsonSerializer.Serialize(new { repository = "demo", baseCommit = "--output=evil", headCommit = "main" }));
        Assert.Null(await Reader.GetDiffAsync(new Uri(injected).AbsoluteUri, default));
    }

    [Fact]
    public async Task An_unknown_commit_gives_null()
    {
        var head = Git(_repoPath, "rev-parse", "main").Trim();
        var url = WriteRecord("main", baseCommit: new string('a', 40), headCommit: head);

        Assert.Null(await Reader.GetDiffAsync(url, default));
    }

    [Fact]
    public async Task A_record_refreshed_by_a_second_push_shows_the_new_head()
    {
        var main = Git(_repoPath, "rev-parse", "main").Trim();
        await _client!.PushFilesAsync(_repo, "update", main, [new FileChange("/Directory.Packages.props", Props.Replace("1.0.0", "2.0.0"))], "bump");
        var url = await _client.EnsurePullRequestAsync(_repo, "update", "main", "t", "d");
        var first = await Reader.GetDiffAsync(url, default);

        var pushed = Git(_repoPath, "rev-parse", "update").Trim();
        await _client.PushFilesAsync(_repo, "update", pushed, [new FileChange("/Directory.Packages.props", Props.Replace("1.0.0", "3.0.0"))], "bump again");
        Assert.Equal(url, await _client.EnsurePullRequestAsync(_repo, "update", "main", "t", "d"));

        var second = await Reader.GetDiffAsync(url, default);

        Assert.Contains("2.0.0", first!.Files.Single().NewContent);
        Assert.Equal(Git(_repoPath, "rev-parse", "update").Trim(), second!.HeadCommitId);
        Assert.Contains("3.0.0", second.Files.Single().NewContent);
    }

    /// <summary>Writes a record by hand; the commits default to the heads of main and the source branch.</summary>
    private string WriteRecord(string source, string? baseCommit = null, string? headCommit = null)
    {
        Directory.CreateDirectory(Path.Combine(_prs, "demo"));
        var path = Path.Combine(_prs, "demo", "1.json");
        var record = new LocalPullRequestRecord(
            1, "org", "proj", "demo", source, "main",
            baseCommit ?? Git(_repoPath, "rev-parse", "main").Trim(),
            headCommit ?? Git(_repoPath, "rev-parse", source).Trim(),
            "t", "d", "active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Uri(path).AbsoluteUri);
        File.WriteAllText(path, JsonSerializer.Serialize(record, new JsonSerializerOptions(JsonSerializerDefaults.Web)), new UTF8Encoding(false));
        return record.Url;
    }

    private void Commit(string message)
    {
        Git(_repoPath, "add", "-A");
        Git(_repoPath, "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false", "commit", "-m", message);
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
