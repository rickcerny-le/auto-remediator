using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Infrastructure.Tests;

public class UpdatePlannerTests
{
    private const string Manifest = """
        <Project><ItemGroup>
          <PackageVersion Include="Orion180.Core" Version="1.0.0" />
          <PackageVersion Include="Orion180.Data" Version="2.0.0" />
        </ItemGroup></Project>
        """;

    [Fact]
    public async Task Plans_updates_and_edits_manifest_for_outdated_matched_packages()
    {
        var analysis = new RepositoryAnalysis(
        [
            new AnalyzedManifest("/Directory.Packages.props", Manifest,
            [
                new AnalyzedPackage("Orion180.Core", "1.0.0", "2.5.0", DependencyStatus.Outdated),
                new AnalyzedPackage("Orion180.Data", "2.0.0", "2.0.0", DependencyStatus.UpToDate),
            ]),
        ]);

        var plan = await PlanAsync(analysis);

        var update = Assert.Single(plan.Updates);
        Assert.Equal("Orion180.Core", update.PackageId);
        Assert.Equal("1.0.0", update.FromVersion);
        Assert.Equal("2.5.0", update.ToVersion);

        var changed = Assert.Single(plan.ChangedManifests);
        Assert.Contains("""Include="Orion180.Core" Version="2.5.0" """, changed.NewContent);
        Assert.Contains("""Include="Orion180.Data" Version="2.0.0" """, changed.NewContent); // untouched
        Assert.True(plan.HasChanges);
    }

    [Fact]
    public async Task Ignored_package_is_not_planned()
    {
        var analysis = new RepositoryAnalysis(
        [
            new AnalyzedManifest("/Directory.Packages.props", Manifest,
            [
                new AnalyzedPackage("Orion180.Core", "1.0.0", "2.0.0", DependencyStatus.Outdated),
                new AnalyzedPackage("Orion180.Data", "2.0.0", null, DependencyStatus.Ignored),
            ]),
        ]);

        var plan = await PlanAsync(analysis);

        var update = Assert.Single(plan.Updates);
        Assert.Equal("Orion180.Core", update.PackageId);
        // The ignored package's version is untouched in the edited manifest.
        Assert.Contains("""Include="Orion180.Data" Version="2.0.0" """, Assert.Single(plan.ChangedManifests).NewContent);
    }

    [Fact]
    public async Task Nothing_outdated_yields_empty_plan()
    {
        var analysis = new RepositoryAnalysis(
        [
            new AnalyzedManifest("/Directory.Packages.props", Manifest,
            [
                new AnalyzedPackage("Orion180.Core", "1.0.0", "1.0.0", DependencyStatus.UpToDate),
                new AnalyzedPackage("Orion180.Data", null, null, DependencyStatus.Unknown),
            ]),
        ]);

        var plan = await PlanAsync(analysis);

        Assert.Empty(plan.Updates);
        Assert.Empty(plan.ChangedManifests);
        Assert.False(plan.HasChanges);
    }

    private static async Task<RepositoryUpdatePlan> PlanAsync(RepositoryAnalysis analysis)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=k;SharedAccessKey=a2V5a2V5a2V5a2V5";
        builder.AddInfrastructure();
        builder.Services.RemoveAll<IRepositoryAnalyzer>();
        builder.Services.AddSingleton<IRepositoryAnalyzer>(new FakeAnalyzer(analysis));

        await using var provider = builder.Services.BuildServiceProvider();
        var repo = new ManagedRepository(Guid.NewGuid(), "orion180", "platform", "web-api");
        return await provider.GetRequiredService<IUpdatePlanner>().PlanAsync(repo, TargetingSettings.Empty, TestContext.Current.CancellationToken);
    }

    private sealed class FakeAnalyzer(RepositoryAnalysis analysis) : IRepositoryAnalyzer
    {
        public Task<RepositoryAnalysis> AnalyzeAsync(ManagedRepository repository, TargetingSettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult(analysis);
    }
}
