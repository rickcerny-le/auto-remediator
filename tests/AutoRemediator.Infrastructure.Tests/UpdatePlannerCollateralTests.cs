using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Alignment;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.Feeds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NuGet.Versioning;

namespace AutoRemediator.Infrastructure.Tests;

public class UpdatePlannerCollateralTests
{
    private const string Manifest = """
        <Project><ItemGroup>
          <PackageVersion Include="Contoso.Core" Version="1.4.0" />
          <PackageVersion Include="Contoso.Common" Version="1.4.0" />
        </ItemGroup></Project>
        """;

    [Fact]
    public async Task Plan_includes_primary_and_collateral_and_edits_both_manifests()
    {
        // Core is outdated (→1.5.0). Core 1.5.0 requires Common >= 1.5.0; Common alone is up to date.
        var analysis = new RepositoryAnalysis(
        [
            new AnalyzedManifest("/Directory.Packages.props", Manifest,
            [
                new AnalyzedPackage("Contoso.Core", "1.4.0", "1.5.0", DependencyStatus.Outdated),
                new AnalyzedPackage("Contoso.Common", "1.4.0", null, DependencyStatus.UpToDate),
            ]),
        ]);
        var deps = new Dictionary<string, IReadOnlyList<FamilyDependency>>
        {
            ["Contoso.Core/1.5.0"] = [new FamilyDependency("Contoso.Common", VersionRange.Parse("[1.5.0, )"))],
        };
        var versions = new Dictionary<string, IReadOnlyList<string>> { ["Contoso.Common"] = ["1.4.0", "1.5.0"] };

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=k;SharedAccessKey=a2V5a2V5a2V5a2V5";
        builder.AddInfrastructure();
        builder.Services.RemoveAll<IRepositoryAnalyzer>();
        builder.Services.RemoveAll<IPackageDependencyReader>();
        builder.Services.RemoveAll<IFeedVersionResolver>();
        builder.Services.AddSingleton<IRepositoryAnalyzer>(new FakeAnalyzer(analysis));
        builder.Services.AddSingleton<IPackageDependencyReader>(new FakeReader(deps));
        builder.Services.AddSingleton<IFeedVersionResolver>(new FakeResolver(versions));

        await using var provider = builder.Services.BuildServiceProvider();
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api");
        var plan = await provider.GetRequiredService<IUpdatePlanner>()
            .PlanAsync(repo, new TargetingSettings(["Contoso.*"], feeds: ["https://feed"]), TestContext.Current.CancellationToken);

        var core = Assert.Single(plan.Updates, u => u.PackageId == "Contoso.Core");
        Assert.Equal(UpdateKind.Matched, core.Kind);
        Assert.Equal("1.5.0", core.ToVersion);

        var common = Assert.Single(plan.Updates, u => u.PackageId == "Contoso.Common");
        Assert.Equal(UpdateKind.Collateral, common.Kind);
        Assert.Equal("1.5.0", common.ToVersion);

        var changed = Assert.Single(plan.ChangedManifests);
        Assert.Contains("""Include="Contoso.Core" Version="1.5.0" """, changed.NewContent);
        Assert.Contains("""Include="Contoso.Common" Version="1.5.0" """, changed.NewContent);
    }

    private sealed class FakeAnalyzer(RepositoryAnalysis analysis) : IRepositoryAnalyzer
    {
        public Task<RepositoryAnalysis> AnalyzeAsync(ManagedRepository r, TargetingSettings s, CancellationToken ct = default) => Task.FromResult(analysis);
    }

    private sealed class FakeReader(IReadOnlyDictionary<string, IReadOnlyList<FamilyDependency>> byIdVersion) : IPackageDependencyReader
    {
        public Task<IReadOnlyList<FamilyDependency>> GetFamilyDependenciesAsync(string packageId, string version, IReadOnlyList<string> feeds, IReadOnlyList<string> patterns, CancellationToken ct = default)
            => Task.FromResult(byIdVersion.TryGetValue($"{packageId}/{version}", out var d) ? d : []);
    }

    private sealed class FakeResolver(IReadOnlyDictionary<string, IReadOnlyList<string>> versions) : IFeedVersionResolver
    {
        public Task<IReadOnlyList<string>> GetVersionsAsync(string packageId, IReadOnlyList<string> feeds, bool allowPrerelease, CancellationToken ct = default)
            => Task.FromResult(versions.TryGetValue(packageId, out var v) ? v : []);
    }
}
