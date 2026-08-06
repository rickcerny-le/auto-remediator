using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Feeds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Infrastructure.Tests;

public class DependencyMapServiceTests
{
    private const string Manifest = """
        <Project>
          <ItemGroup>
            <PackageVersion Include="Orion180.Core" Version="1.0.0" />
            <PackageVersion Include="Orion180.Data" Version="2.0.0" />
            <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
          </ItemGroup>
        </Project>
        """;

    [Fact]
    public async Task Build_maps_matched_packages_with_status()
    {
        var settings = new TargetingSettings(["Orion180.*"], feeds: ["https://feed"], policy: new UpdatePolicy(UpdateStrategy.Major, []));
        var versions = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Orion180.Core"] = ["1.0.0", "2.5.0"], // outdated
            ["Orion180.Data"] = ["2.0.0"],          // up-to-date
        };

        await using var provider = BuildProvider(settings, versions);
        var map = await provider.GetRequiredService<IDependencyMapService>().BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, map.Entries.Count); // Newtonsoft.Json not matched

        var core = Assert.Single(map.Entries, e => e.PackageId == "Orion180.Core");
        Assert.Equal("1.0.0", core.CurrentVersion);
        Assert.Equal("2.5.0", core.LatestVersion);
        Assert.Equal(DependencyStatus.Outdated, core.Status);

        var data = Assert.Single(map.Entries, e => e.PackageId == "Orion180.Data");
        Assert.Equal(DependencyStatus.UpToDate, data.Status);
    }

    [Fact]
    public async Task Ignored_package_is_held_and_shown_as_ignored()
    {
        var settings = new TargetingSettings(
            ["Orion180.*"], feeds: ["https://feed"],
            policy: new UpdatePolicy(UpdateStrategy.Major, ["Orion180.Data"]));
        var versions = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Orion180.Core"] = ["1.0.0", "2.5.0"],
            ["Orion180.Data"] = ["2.0.0", "3.0.0"], // would be outdated, but ignored
        };

        await using var provider = BuildProvider(settings, versions);
        var map = await provider.GetRequiredService<IDependencyMapService>().BuildAsync(TestContext.Current.CancellationToken);

        var data = Assert.Single(map.Entries, e => e.PackageId == "Orion180.Data");
        Assert.Equal(DependencyStatus.Ignored, data.Status);
        Assert.Null(data.LatestVersion);

        var core = Assert.Single(map.Entries, e => e.PackageId == "Orion180.Core");
        Assert.Equal(DependencyStatus.Outdated, core.Status);
    }

    [Fact]
    public async Task No_versions_from_feed_yields_unknown_status()
    {
        var settings = new TargetingSettings(["Orion180.*"], feeds: ["https://feed"]);

        await using var provider = BuildProvider(settings, new Dictionary<string, IReadOnlyList<string>>());
        var map = await provider.GetRequiredService<IDependencyMapService>().BuildAsync(TestContext.Current.CancellationToken);

        Assert.All(map.Entries, e => Assert.Equal(DependencyStatus.Unknown, e.Status));
    }

    private static ServiceProvider BuildProvider(TargetingSettings settings, IReadOnlyDictionary<string, IReadOnlyList<string>> versions)
    {
        var repo = new ManagedRepository(Guid.NewGuid(), "orion180", "platform", "web-api");

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=k;SharedAccessKey=a2V5a2V5a2V5a2V5";
        builder.AddInfrastructure();

        builder.Services.RemoveAll<IManagedRepositoryStore>();
        builder.Services.RemoveAll<ITargetingSettingsStore>();
        builder.Services.RemoveAll<IAzureDevOpsClient>();
        builder.Services.RemoveAll<IFeedVersionResolver>();
        builder.Services.AddSingleton<IManagedRepositoryStore>(new FakeRepoStore([repo]));
        builder.Services.AddSingleton<ITargetingSettingsStore>(new FakeSettingsStore(settings));
        builder.Services.AddSingleton<IAzureDevOpsClient>(new FakeAdo(Manifest));
        builder.Services.AddSingleton<IFeedVersionResolver>(new FakeResolver(versions));

        return builder.Services.BuildServiceProvider();
    }

    private sealed class FakeRepoStore(IReadOnlyList<ManagedRepository> repos) : IManagedRepositoryStore
    {
        public Task<IReadOnlyList<ManagedRepository>> ListAsync(CancellationToken ct = default) => Task.FromResult(repos);
        public Task<ManagedRepository?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(repos.FirstOrDefault(r => r.Id == id));
        public Task UpsertAsync(ManagedRepository r, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeSettingsStore(TargetingSettings settings) : ITargetingSettingsStore
    {
        public Task<TargetingSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(settings);
        public Task SetAsync(TargetingSettings s, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeAdo(string manifest) : IAzureDevOpsClient
    {
        public Task<bool> RepositoryExistsAsync(ManagedRepository r, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<RepositoryFile>> GetManifestsAsync(ManagedRepository r, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RepositoryFile>>([new RepositoryFile("/Directory.Packages.props", manifest)]);
        public Task<string?> GetBranchHeadAsync(ManagedRepository r, string branch, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<Stream> GetRepositoryArchiveAsync(ManagedRepository r, string commitId, CancellationToken ct = default)
            => throw new NotSupportedException("the dependency map never verifies");
        public Task PushFilesAsync(ManagedRepository r, string branch, string baseCommitId, IReadOnlyList<FileChange> changes, string message, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> EnsurePullRequestAsync(ManagedRepository r, string s, string t, string title, string desc, CancellationToken ct = default) => Task.FromResult("");
    }

    private sealed class FakeResolver(IReadOnlyDictionary<string, IReadOnlyList<string>> versions) : IFeedVersionResolver
    {
        public Task<IReadOnlyList<string>> GetVersionsAsync(string packageId, IReadOnlyList<string> feeds, bool allowPrerelease, CancellationToken ct = default)
            => Task.FromResult(versions.TryGetValue(packageId, out var v) ? v : []);
    }
}
