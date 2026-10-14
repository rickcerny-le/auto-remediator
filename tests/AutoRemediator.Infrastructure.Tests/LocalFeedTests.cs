using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Alignment;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Feeds;
using AutoRemediator.Infrastructure.Verification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Infrastructure.Tests;

/// <summary>
/// A feed given as a plain local folder path works through the resolver, the dependency reader and
/// the verification workspace's NuGet.config, with and without a PAT configured.
/// </summary>
public class LocalFeedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IOptions<AzureDevOpsOptions> Pat(string? pat) => Options.Create(new AzureDevOpsOptions { Pat = pat });

    public static IEnumerable<object?[]> Pats => [[null], ["dummy"]];

    [Theory]
    [MemberData(nameof(Pats))]
    public async Task Resolver_returns_normalized_versions_without_prerelease(string? pat)
    {
        using var feed = new TestFeed();
        var id = TestFeed.UniqueId("Contoso.Http");
        feed.Add(id, "1.0.0").Add(id, "2.1.0.0").Add(id, "2.2.0").Add(id, "3.0.0-beta.1");

        var versions = await new FeedVersionResolver(Pat(pat)).GetVersionsAsync(id, [feed.Path], false, Ct);

        Assert.Equal(["1.0.0", "2.1.0", "2.2.0"], versions.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [MemberData(nameof(Pats))]
    public async Task Resolver_includes_prerelease_when_allowed(string? pat)
    {
        using var feed = new TestFeed();
        var id = TestFeed.UniqueId("Fabrikam.Telemetry");
        feed.Add(id, "1.0.0").Add(id, "1.1.0-beta.2");

        var versions = await new FeedVersionResolver(Pat(pat)).GetVersionsAsync(id, [feed.Path], true, Ct);

        Assert.Equal(["1.0.0", "1.1.0-beta.2"], versions.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [MemberData(nameof(Pats))]
    public async Task Resolver_returns_empty_for_an_unknown_id(string? pat)
    {
        using var feed = new TestFeed();
        feed.Add(TestFeed.UniqueId("Contoso.Http"), "1.0.0");

        var versions = await new FeedVersionResolver(Pat(pat)).GetVersionsAsync(TestFeed.UniqueId("Nope"), [feed.Path], false, Ct);

        Assert.Empty(versions);
    }

    [Theory]
    [MemberData(nameof(Pats))]
    public async Task Resolver_returns_empty_for_a_missing_folder_without_throwing(string? pat)
    {
        var missing = Path.Combine(Path.GetTempPath(), "ar-missing-" + Guid.NewGuid().ToString("N"));

        var versions = await new FeedVersionResolver(Pat(pat)).GetVersionsAsync("Contoso.Http", [missing], false, Ct);

        Assert.Empty(versions);
    }

    [Fact]
    public async Task Resolver_gives_identical_results_with_and_without_a_pat()
    {
        using var feed = new TestFeed();
        var id = TestFeed.UniqueId("Contoso.Logging");
        feed.Add(id, "1.2.0").Add(id, "1.2.1").Add(id, "1.2.3");

        var without = await new FeedVersionResolver(Pat(null)).GetVersionsAsync(id, [feed.Path], false, Ct);
        var with = await new FeedVersionResolver(Pat("dummy")).GetVersionsAsync(id, [feed.Path], false, Ct);

        Assert.Equal(3, without.Count);
        Assert.Equal(without.Order(StringComparer.Ordinal), with.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Pats))]
    public async Task Dependency_reader_returns_family_ranges_from_the_nuspec(string? pat)
    {
        using var feed = new TestFeed();
        var abstractions = TestFeed.UniqueId("Fabrikam.Data.Abstractions");
        var data = TestFeed.UniqueId("Fabrikam.Data");
        feed.Add(abstractions, "2.0.0")
            .Add(data, "1.2.0", (abstractions, "[2.0.0,3.0.0)"), ("Newtonsoft.Json", "13.0.1"));

        var dependencies = await new PackageDependencyReader(Pat(pat))
            .GetFamilyDependenciesAsync(data, "1.2.0", [feed.Path], ["Fabrikam.*"], Ct);

        var dependency = Assert.Single(dependencies);
        Assert.Equal(abstractions, dependency.PackageId);
        Assert.Equal("[2.0.0, 3.0.0)", dependency.Range.ToString());
    }

    [Fact]
    public async Task Dependency_reader_returns_nothing_for_a_missing_folder_or_version()
    {
        using var feed = new TestFeed();
        var id = TestFeed.UniqueId("Fabrikam.Data");
        feed.Add(id, "1.0.0");
        var missing = Path.Combine(Path.GetTempPath(), "ar-missing-" + Guid.NewGuid().ToString("N"));
        var reader = new PackageDependencyReader(Pat("dummy"));

        Assert.Empty(await reader.GetFamilyDependenciesAsync(id, "1.0.0", [missing], ["Fabrikam.*"], Ct));
        Assert.Empty(await reader.GetFamilyDependenciesAsync(id, "9.9.9", [feed.Path], ["Fabrikam.*"], Ct));
    }

    [Theory]
    [MemberData(nameof(Pats))]
    public async Task Workspace_nuget_config_restores_from_a_local_folder_feed(string? pat)
    {
        if (!DotnetAvailable())
        {
            Assert.Skip("dotnet is not available");
        }

        using var feed = new TestFeed();
        var id = TestFeed.UniqueId("Contoso.Restore");
        feed.Add(id, "1.0.0");

        var csproj = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="{id}" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """;

        var factory = new VerificationWorkspaceFactory(
            new ArchiveOnlyAdo(() => TestArchive.Of(("README.md", "x"), ("App/App.csproj", csproj))),
            Pat(pat),
            NullLogger<VerificationWorkspaceFactory>.Instance);

        using var workspace = await factory.CreateAsync(
            new ManagedRepository(Guid.NewGuid(), "local", "scenarios", "restore"),
            "commit-abc",
            [],
            new TargetingSettings(["Contoso.*"], feeds: [feed.Path]),
            Ct);

        var runner = new DotnetCliRunner(NullLogger<DotnetCliRunner>.Instance);
        var result = await runner.RunAsync("restore App/App.csproj", workspace.Root, TimeSpan.FromMinutes(3), Ct);

        Assert.True(result.Succeeded, result.Output);
        Assert.DoesNotContain("NU1301", result.Output);
    }

    private static bool DotnetAvailable()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });
            process!.WaitForExit(30_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed class ArchiveOnlyAdo(Func<Stream> archive) : IAzureDevOpsClient
    {
        public Task<bool> RepositoryExistsAsync(ManagedRepository r, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<RepositoryFile>> GetManifestsAsync(ManagedRepository r, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RepositoryFile>>([]);
        public Task<string?> GetBranchHeadAsync(ManagedRepository r, string b, CancellationToken ct = default)
            => Task.FromResult<string?>("commit-abc");
        public Task<Stream> GetRepositoryArchiveAsync(ManagedRepository r, string commitId, CancellationToken ct = default)
            => Task.FromResult(archive());
        public Task PushFilesAsync(ManagedRepository r, string b, string c, IReadOnlyList<FileChange> ch, string m, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<string> EnsurePullRequestAsync(ManagedRepository r, string s, string t, string ti, string d, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
