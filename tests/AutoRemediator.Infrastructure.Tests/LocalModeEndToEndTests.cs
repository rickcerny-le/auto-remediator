using System.Diagnostics;
using System.Text.Json;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Remediation;
using AutoRemediator.Infrastructure.Review;
using AutoRemediator.Infrastructure.Verification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Infrastructure.Tests;

/// <summary>
/// Drives the real <see cref="IRemediationRunner"/> composed by <c>AddInfrastructure</c> in Local mode against
/// temporary git repositories and a temporary folder feed. Only storage is replaced by in-memory fakes;
/// the planner, aligner, feed resolver, verification (real <c>dotnet restore</c>/<c>build</c>),
/// <c>LocalRepositoryClient</c> and the "no model" agent are all real.
/// </summary>
[Trait("Category", "LocalE2E")]
public sealed class LocalModeEndToEndTests : IAsyncLifetime
{
    private const string UpdateBranch = "autoremediator/dependency-updates";

    private static readonly Lazy<bool> ToolsAvailable = new(() => Runs("git", "--version") && Runs("dotnet", "--version"));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ar-e2e-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly TestFeed _feed = new();
    private readonly List<string> _packageIds = [];
    private readonly InMemoryRepositoryStore _repositories = new();
    private readonly InMemorySettingsStore _settings = new();
    private readonly InMemoryRunStore _runs = new();
    private readonly InMemoryLogStore _logs = new();
    private ServiceProvider? _provider;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string ReposRoot => Path.Combine(_root, "repos");
    private string PrsRoot => Path.Combine(_root, "prs");

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(ReposRoot);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        _feed.Dispose();
        DeleteTree(_root);

        // The ids are unique to this run, so nothing else can be using their global-cache folders.
        var cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        cache = string.IsNullOrWhiteSpace(cache)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
            : cache;
        foreach (var id in _packageIds)
        {
            DeleteTree(Path.Combine(cache, id.ToLowerInvariant()));
        }
    }

    // ---- Cases ---------------------------------------------------------------------

    [Fact]
    public async Task Patch_bump_with_central_package_management_opens_a_local_pull_request()
    {
        RequireTools();
        var id = Id("Contoso.Logging");
        _feed.Add(id, "1.2.0").Add(id, "1.2.1").Add(id, "1.2.3");
        var repo = CentralRepo("patch-bump", (id, "1.2.0"));
        UseSettings([id]);
        var mainHead = Git(repo.Path, "rev-parse", "main").Trim();
        var propsBefore = File.ReadAllText(Path.Combine(repo.Path, "Directory.Packages.props"));

        var run = await RunAsync(repo.Managed);

        Assert.Equal(RunStatus.Completed, run.Status);
        var update = Assert.Single(run.Updates);
        Assert.Equal(("1.2.0", "1.2.3"), (update.FromVersion, update.ToVersion));
        Assert.Contains($"Version=\"1.2.3\"", Git(repo.Path, "show", $"{UpdateBranch}:Directory.Packages.props"));

        // main and the working tree are untouched.
        Assert.Equal(mainHead, Git(repo.Path, "rev-parse", "main").Trim());
        Assert.Equal(propsBefore, File.ReadAllText(Path.Combine(repo.Path, "Directory.Packages.props")));
        Assert.Equal(string.Empty, Git(repo.Path, "status", "--porcelain").Trim());
        Assert.Single(Git(repo.Path, "worktree", "list").Split('\n', StringSplitOptions.RemoveEmptyEntries));

        var record = ReadRecord(repo.Name, 1);
        Assert.Equal(run.PullRequestUrl, record.GetProperty("url").GetString());
        Assert.StartsWith("file:///", run.PullRequestUrl);
        Assert.Equal(mainHead, record.GetProperty("baseCommit").GetString());
        Assert.Equal(Git(repo.Path, "rev-parse", UpdateBranch).Trim(), record.GetProperty("headCommit").GetString());
    }

    [Fact]
    public async Task A_second_run_reuses_the_same_pull_request()
    {
        RequireTools();
        var id = Id("Contoso.Logging");
        _feed.Add(id, "1.2.0").Add(id, "1.2.3");
        var repo = CentralRepo("idempotent", (id, "1.2.0"));
        UseSettings([id]);

        var first = await RunAsync(repo.Managed);
        var second = await RunAsync(repo.Managed);

        Assert.Equal(RunStatus.Completed, first.Status);
        Assert.Equal(RunStatus.Completed, second.Status);
        Assert.Equal(first.PullRequestUrl, second.PullRequestUrl);
        Assert.True(File.Exists(PrPath(repo.Name, 1)));
        Assert.False(File.Exists(PrPath(repo.Name, 2)));
    }

    [Fact]
    public async Task A_package_already_at_the_policy_target_ends_NoUpdates()
    {
        RequireTools();
        var id = Id("Contoso.Logging");
        _feed.Add(id, "1.2.0").Add(id, "1.2.3");
        var repo = CentralRepo("up-to-date", (id, "1.2.3"));
        UseSettings([id]);

        var run = await RunAsync(repo.Managed);

        Assert.Equal(RunStatus.NoUpdates, run.Status);
        Assert.False(BranchExists(repo.Path, UpdateBranch));
        Assert.False(Directory.Exists(Path.Combine(PrsRoot, repo.Name)));
    }

    [Fact]
    public async Task A_repo_only_pattern_updates_that_repository_and_leaves_an_inheriting_sibling_alone()
    {
        RequireTools();
        var shared = Id("Fabrikam.Shared");
        var unrelated = Id("Contoso.Unrelated");
        _feed.Add(shared, "1.0.0").Add(shared, "1.0.2");
        var overriding = CentralRepo("repo-pattern", (shared, "1.0.0"));
        var inheriting = CentralRepo("inherits", (shared, "1.0.0"));
        UseSettings([unrelated]); // the global patterns do not match the shared package

        var overridden = new ManagedRepository(
            overriding.Managed.Id, "local", "scenarios", overriding.Name,
            settings: new RepositorySettings(patterns: [shared]));

        var first = await RunAsync(overridden);
        var second = await RunAsync(inheriting.Managed);

        Assert.Equal(RunStatus.Completed, first.Status);
        var update = Assert.Single(first.Updates);
        Assert.Equal(("1.0.0", "1.0.2"), (update.FromVersion, update.ToVersion));
        Assert.Contains("Version=\"1.0.2\"", Git(overriding.Path, "show", $"{UpdateBranch}:Directory.Packages.props"));

        Assert.Equal(RunStatus.NoUpdates, second.Status);
        Assert.False(BranchExists(inheriting.Path, UpdateBranch));
    }

    [Fact]
    public async Task A_restore_conflict_is_rejected_without_entering_the_repair_loop()
    {
        RequireTools();
        var id = Id("Fabrikam.Reporting");
        var missing = Id("Fabrikam.Missing");
        _feed.Add(id, "1.0.0").Add(id, "1.1.0", (missing, "[1.0.0,2.0.0)"));
        var repo = CentralRepo("restore-conflict", (id, "1.0.0"));
        UseSettings([id]);

        var run = await RunAsync(repo.Managed);

        Assert.Equal(RunStatus.VerificationFailed, run.Status);
        Assert.Contains(run.Verification!.Diagnostics, d => d.Code.StartsWith("NU", StringComparison.Ordinal));
        Assert.Null(run.RemediationAttempts);
        Assert.False(BranchExists(repo.Path, UpdateBranch));
        Assert.False(Directory.Exists(Path.Combine(PrsRoot, repo.Name)));
    }

    [Fact]
    public async Task A_dependency_family_move_is_flagged_as_collateral_beyond_policy()
    {
        RequireTools();
        var abstractions = Id("Fabrikam.Data.Abstractions");
        var data = Id("Fabrikam.Data");
        _feed.Add(abstractions, "1.0.0").Add(abstractions, "2.0.0")
            .Add(data, "1.0.0", (abstractions, "[1.0.0,2.0.0)"))
            .Add(data, "1.2.0", (abstractions, "[2.0.0,3.0.0)"));
        var repo = CentralRepo("family-alignment", (abstractions, "1.0.0"), (data, "1.0.0"));
        UseSettings([abstractions, data]);

        var run = await RunAsync(repo.Managed);

        Assert.Equal(RunStatus.Completed, run.Status);
        var collateral = Assert.Single(run.Updates, u => u.Kind == UpdateKind.Collateral);
        Assert.Equal(abstractions, collateral.PackageId);
        Assert.Equal("2.0.0", collateral.ToVersion);
        Assert.True(collateral.BeyondPolicy);
        var matched = Assert.Single(run.Updates, u => u.Kind == UpdateKind.Matched);
        Assert.Equal(data, matched.PackageId);
        Assert.Equal("1.2.0", matched.ToVersion);

        var props = Git(repo.Path, "show", $"{UpdateBranch}:Directory.Packages.props");
        Assert.Contains($"Include=\"{abstractions}\" Version=\"2.0.0\"", props);
        Assert.Contains($"Include=\"{data}\" Version=\"1.2.0\"", props);
    }

    [Fact]
    public async Task Per_project_versions_are_edited_in_every_project_file()
    {
        RequireTools();
        var id = Id("Contoso.Http");
        _feed.Add(id, "2.1.0").Add(id, "2.1.3");
        var repo = MakeRepo(
            "per-project",
            ("README.md", "demo"),
            ("src/A/A.csproj", Project(id, "2.1.0")),
            ("src/A/A.cs", "namespace A; public class C { }"),
            ("src/B/B.csproj", Project(id, "2.1.0")),
            ("src/B/B.cs", "namespace B; public class C { }"));
        UseSettings([id]);

        var run = await RunAsync(repo.Managed);

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Contains("Version=\"2.1.3\"", Git(repo.Path, "show", $"{UpdateBranch}:src/A/A.csproj"));
        Assert.Contains("Version=\"2.1.3\"", Git(repo.Path, "show", $"{UpdateBranch}:src/B/B.csproj"));
        Assert.Contains("Version=\"2.1.0\"", Git(repo.Path, "show", "main:src/A/A.csproj"));
    }

    [Fact]
    public async Task A_compile_break_with_no_model_is_rejected_without_a_branch_or_pull_request()
    {
        RequireTools();
        var id = Id("Contoso.Broken");
        _feed.Add(id, "1.0.0").AddWithFiles(
            id,
            "1.0.1",
            new Dictionary<string, string>
            {
                [$"build/{id}.props"] = """
                    <Project>
                      <ItemGroup>
                        <Compile Include="$(MSBuildThisFileDirectory)..\src\Bad.cs" />
                      </ItemGroup>
                    </Project>
                    """,
                ["src/Bad.cs"] = "namespace Broken; internal class Bad { private int x = \"s\"; }",
            });
        var repo = CentralRepo("compile-break", (id, "1.0.0"));
        UseSettings([id]);

        var run = await RunAsync(repo.Managed);

        Assert.Equal(RunStatus.VerificationFailed, run.Status);
        Assert.Contains(run.Verification!.Diagnostics, d => d.Code.StartsWith("CS", StringComparison.Ordinal));
        Assert.Equal(0, run.RemediationAttempts);
        var transcript = await _logs.ReadAsync(run.RemediationTranscriptReference!, Ct);
        Assert.Contains("No model is configured", transcript);
        Assert.False(BranchExists(repo.Path, UpdateBranch));
        Assert.False(Directory.Exists(Path.Combine(PrsRoot, repo.Name)));
    }

    // ---- Host ----------------------------------------------------------------------

    private async Task<RemediationRun> RunAsync(ManagedRepository repo)
    {
        await _repositories.UpsertAsync(repo, Ct);
        _provider ??= BuildProvider();

        await using var scope = _provider.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IRemediationRunner>();
        return await runner.RunAsync(
            new RemediationRunRequested(Guid.NewGuid(), repo.Id, repo.Organization, repo.Project, repo.Name, DateTimeOffset.UtcNow),
            Ct);
    }

    private ServiceProvider BuildProvider()
    {
        var builder = Host.CreateApplicationBuilder();
        // Fake connection strings: construction of the Azure clients is offline, and storage is replaced below.
        builder.Configuration["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] =
            "Endpoint=sb://localhost/;SharedAccessKeyName=key;SharedAccessKey=a2V5a2V5a2V5a2V5a2V5a2V5";
        builder.Configuration["AzureDevOps:Mode"] = "Local";
        builder.Configuration["AzureDevOps:LocalRoot"] = ReposRoot;
        builder.Configuration["AzureDevOps:LocalPullRequestsRoot"] = PrsRoot;

        builder.AddInfrastructure();

        // AddAgents is deliberately not called, so the "no model" agent is used.
        builder.Services.Replace(ServiceDescriptor.Singleton<IManagedRepositoryStore>(_repositories));
        builder.Services.Replace(ServiceDescriptor.Singleton<ITargetingSettingsStore>(_settings));
        builder.Services.Replace(ServiceDescriptor.Singleton<IRemediationRunStore>(_runs));
        builder.Services.Replace(ServiceDescriptor.Singleton<IChangeProposalStore>(new InMemoryProposalStore()));
        builder.Services.Replace(ServiceDescriptor.Singleton<IVerificationLogStore>(_logs));

        return builder.Services.BuildServiceProvider();
    }

    private void UseSettings(IReadOnlyList<string> patterns)
        => _settings.Current = new TargetingSettings(
            patterns, feeds: [_feed.Path], policy: new UpdatePolicy(UpdateStrategy.Minor, []));

    private string Id(string prefix)
    {
        var id = TestFeed.UniqueId(prefix);
        _packageIds.Add(id);
        return id;
    }

    // ---- Fixture repositories ---------------------------------------------------------

    private sealed record Repo(string Name, string Path, ManagedRepository Managed);

    private Repo CentralRepo(string name, params (string Id, string Version)[] packages)
    {
        var versions = string.Concat(packages.Select(p => $"    <PackageVersion Include=\"{p.Id}\" Version=\"{p.Version}\" />\n"));
        var references = string.Concat(packages.Select(p => $"    <PackageReference Include=\"{p.Id}\" />\n"));
        return MakeRepo(
            name,
            ("README.md", "demo"),
            ("Directory.Packages.props", $"<Project>\n  <PropertyGroup>\n    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>\n  </PropertyGroup>\n  <ItemGroup>\n{versions}  </ItemGroup>\n</Project>\n"),
            ("src/App/App.csproj", $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n  <ItemGroup>\n{references}  </ItemGroup>\n</Project>\n"),
            ("src/App/Class1.cs", "namespace App; public class Class1 { }\n"));
    }

    private static string Project(string id, string version)
        => $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n  <ItemGroup>\n    <PackageReference Include=\"{id}\" Version=\"{version}\" />\n  </ItemGroup>\n</Project>\n";

    private Repo MakeRepo(string name, params (string Path, string Content)[] files)
    {
        var path = Path.Combine(ReposRoot, name);
        Directory.CreateDirectory(path);
        Git(path, "init", "-b", "main");
        Git(path, "config", "core.autocrlf", "false");
        foreach (var (relative, content) in files)
        {
            var full = Path.Combine(path, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        Git(path, "add", "-A");
        Git(path, "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false", "commit", "-m", "init");
        return new Repo(name, path, new ManagedRepository(Guid.NewGuid(), "local", "scenarios", name));
    }

    // ---- Helpers -------------------------------------------------------------------

    private string PrPath(string repo, int id) => Path.Combine(PrsRoot, repo, $"{id}.json");

    private JsonElement ReadRecord(string repo, int id)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PrPath(repo, id)));
        return document.RootElement.Clone();
    }

    private static bool BranchExists(string repoPath, string branch)
        => GitRaw(repoPath, "rev-parse", "--verify", "--quiet", $"refs/heads/{branch}").ExitCode == 0;

    private static void RequireTools()
    {
        if (!ToolsAvailable.Value)
        {
            Assert.Skip("git and dotnet must both be available.");
        }
    }

    private static bool Runs(string file, string args)
    {
        try
        {
            var info = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(info);
            process!.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string Git(string dir, params string[] args)
    {
        var (exitCode, output, error) = GitRaw(dir, args);
        Assert.True(exitCode == 0, $"git {string.Join(' ', args)}: {error}");
        return output;
    }

    private static (int ExitCode, string Output, string Error) GitRaw(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error.Result);
    }

    private static void DeleteTree(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            // Git object files are read-only, which blocks a plain recursive delete on Windows.
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
