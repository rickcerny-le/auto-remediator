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
        var repo = new ManagedRepository(Guid.NewGuid(), "orion180", "platform", "web-api");
        var settings = new TargetingSettings(["Orion180.*"], feeds: ["https://feed"]);

        var latest = new Dictionary<string, string?>
        {
            ["Orion180.Core"] = "2.5.0", // outdated (current 1.0.0)
            ["Orion180.Data"] = "2.0.0", // up-to-date (current 2.0.0)
        };

        await using var provider = BuildProvider(
            new FakeRepoStore([repo]),
            new FakeSettingsStore(settings),
            new FakeAdo(Manifest),
            new FakeResolver(latest));

        var map = await provider.GetRequiredService<IDependencyMapService>().BuildAsync(TestContext.Current.CancellationToken);

        // Newtonsoft.Json is not matched by Orion180.*
        Assert.Equal(2, map.Entries.Count);

        var core = Assert.Single(map.Entries, e => e.PackageId == "Orion180.Core");
        Assert.Equal("1.0.0", core.CurrentVersion);
        Assert.Equal("2.5.0", core.LatestVersion);
        Assert.Equal(DependencyStatus.Outdated, core.Status);

        var data = Assert.Single(map.Entries, e => e.PackageId == "Orion180.Data");
        Assert.Equal(DependencyStatus.UpToDate, data.Status);
    }

    [Fact]
    public async Task Unknown_latest_yields_unknown_status()
    {
        var repo = new ManagedRepository(Guid.NewGuid(), "orion180", "platform", "web-api");
        var settings = new TargetingSettings(["Orion180.*"], feeds: ["https://feed"]);

        await using var provider = BuildProvider(
            new FakeRepoStore([repo]),
            new FakeSettingsStore(settings),
            new FakeAdo(Manifest),
            new FakeResolver(new Dictionary<string, string?>())); // resolver returns null for everything

        var map = await provider.GetRequiredService<IDependencyMapService>().BuildAsync(TestContext.Current.CancellationToken);

        Assert.All(map.Entries, e => Assert.Equal(DependencyStatus.Unknown, e.Status));
    }

    private static ServiceProvider BuildProvider(
        IManagedRepositoryStore repos,
        ITargetingSettingsStore settings,
        IAzureDevOpsClient ado,
        IFeedVersionResolver resolver)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] =
            "Endpoint=sb://localhost/;SharedAccessKeyName=key;SharedAccessKey=a2V5a2V5a2V5a2V5a2V5a2V5";
        builder.AddInfrastructure();

        builder.Services.RemoveAll<IManagedRepositoryStore>();
        builder.Services.RemoveAll<ITargetingSettingsStore>();
        builder.Services.RemoveAll<IAzureDevOpsClient>();
        builder.Services.RemoveAll<IFeedVersionResolver>();
        builder.Services.AddSingleton(repos);
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(ado);
        builder.Services.AddSingleton(resolver);

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

        // Write ops are unused by dependency-map analysis.
        public Task<string?> GetBranchHeadAsync(ManagedRepository r, string branch, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task PushFilesAsync(ManagedRepository r, string branch, string baseCommitId, IReadOnlyList<FileChange> changes, string message, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> EnsurePullRequestAsync(ManagedRepository r, string s, string t, string title, string desc, CancellationToken ct = default) => Task.FromResult("");
    }

    private sealed class FakeResolver(IReadOnlyDictionary<string, string?> latest) : IFeedVersionResolver
    {
        public Task<string?> GetLatestVersionAsync(string packageId, IReadOnlyList<string> feeds, bool allowPrerelease, CancellationToken ct = default)
            => Task.FromResult(latest.TryGetValue(packageId, out var v) ? v : null);
    }
}
