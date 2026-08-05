using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Alignment;
using AutoRemediator.Infrastructure.Feeds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NuGet.Versioning;

namespace AutoRemediator.Infrastructure.Tests;

public class DependencyAlignerTests
{
    private static FamilyDependency Dep(string id, string range) => new(id, VersionRange.Parse(range));

    [Fact]
    public async Task Adds_minimal_collateral_bump_when_range_unsatisfied()
    {
        // Core is being bumped to 1.5.0; Core 1.5.0 needs Common >= 1.5.0; repo pins Common 1.4.0.
        var deps = new Dictionary<string, IReadOnlyList<FamilyDependency>>
        {
            ["Orion180.Core/1.5.0"] = [Dep("Orion180.Common", "[1.5.0, )")],
        };
        var versions = new Dictionary<string, IReadOnlyList<string>> { ["Orion180.Common"] = ["1.4.0", "1.5.0", "1.6.0"] };

        var result = await Align(
            declaredCurrent: new() { ["Orion180.Core"] = "1.4.0", ["Orion180.Common"] = "1.4.0" },
            chosen: new() { ["Orion180.Core"] = "1.5.0", ["Orion180.Common"] = "1.4.0" },
            deps, versions, UpdateStrategy.Minor);

        Assert.Equal("1.5.0", result.Chosen["Orion180.Common"]); // minimal satisfying, not 1.6.0
        Assert.DoesNotContain("Orion180.Common", result.BeyondPolicy);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public async Task No_collateral_when_family_already_resolves()
    {
        var deps = new Dictionary<string, IReadOnlyList<FamilyDependency>>
        {
            ["Orion180.Core/1.5.0"] = [Dep("Orion180.Common", "[1.0.0, )")],
        };

        var result = await Align(
            new() { ["Orion180.Core"] = "1.4.0", ["Orion180.Common"] = "1.4.0" },
            new() { ["Orion180.Core"] = "1.5.0", ["Orion180.Common"] = "1.4.0" },
            deps, new Dictionary<string, IReadOnlyList<string>>(), UpdateStrategy.Minor);

        Assert.Equal("1.4.0", result.Chosen["Orion180.Common"]);
    }

    [Fact]
    public async Task Undeclared_sibling_is_not_bumped()
    {
        var deps = new Dictionary<string, IReadOnlyList<FamilyDependency>>
        {
            ["Orion180.Core/1.5.0"] = [Dep("Orion180.Common", "[1.5.0, )")],
        };
        // Common is not declared (not in chosen) → transitive, left to NuGet.
        var result = await Align(
            new() { ["Orion180.Core"] = "1.4.0" },
            new() { ["Orion180.Core"] = "1.5.0" },
            deps, new Dictionary<string, IReadOnlyList<string>> { ["Orion180.Common"] = ["1.5.0"] }, UpdateStrategy.Minor);

        Assert.False(result.Chosen.ContainsKey("Orion180.Common"));
    }

    [Fact]
    public async Task Aligns_iteratively_across_a_chain()
    {
        var deps = new Dictionary<string, IReadOnlyList<FamilyDependency>>
        {
            ["Orion180.A/2.0.0"] = [Dep("Orion180.B", "[2.0.0, )")],
            ["Orion180.B/2.0.0"] = [Dep("Orion180.C", "[2.0.0, )")],
        };
        var versions = new Dictionary<string, IReadOnlyList<string>>
        {
            ["Orion180.B"] = ["1.0.0", "2.0.0"],
            ["Orion180.C"] = ["1.0.0", "2.0.0"],
        };

        var result = await Align(
            new() { ["Orion180.A"] = "1.0.0", ["Orion180.B"] = "1.0.0", ["Orion180.C"] = "1.0.0" },
            new() { ["Orion180.A"] = "2.0.0", ["Orion180.B"] = "1.0.0", ["Orion180.C"] = "1.0.0" },
            deps, versions, UpdateStrategy.Major);

        Assert.Equal("2.0.0", result.Chosen["Orion180.B"]);
        Assert.Equal("2.0.0", result.Chosen["Orion180.C"]);
    }

    [Fact]
    public async Task Escalates_beyond_policy_when_required()
    {
        var deps = new Dictionary<string, IReadOnlyList<FamilyDependency>>
        {
            ["Orion180.Core/1.5.0"] = [Dep("Orion180.Common", "[2.0.0, )")],
        };
        var versions = new Dictionary<string, IReadOnlyList<string>> { ["Orion180.Common"] = ["1.4.0", "2.0.0"] };

        var result = await Align(
            new() { ["Orion180.Core"] = "1.4.0", ["Orion180.Common"] = "1.4.0" },
            new() { ["Orion180.Core"] = "1.5.0", ["Orion180.Common"] = "1.4.0" },
            deps, versions, UpdateStrategy.Minor);

        Assert.Equal("2.0.0", result.Chosen["Orion180.Common"]);
        Assert.Contains("Orion180.Common", result.BeyondPolicy); // major jump under Minor
    }

    [Fact]
    public async Task Unresolvable_range_is_left_unbumped_and_recorded()
    {
        var deps = new Dictionary<string, IReadOnlyList<FamilyDependency>>
        {
            ["Orion180.Core/1.5.0"] = [Dep("Orion180.Common", "[3.0.0, )")],
        };
        var versions = new Dictionary<string, IReadOnlyList<string>> { ["Orion180.Common"] = ["1.4.0", "2.0.0"] };

        var result = await Align(
            new() { ["Orion180.Core"] = "1.4.0", ["Orion180.Common"] = "1.4.0" },
            new() { ["Orion180.Core"] = "1.5.0", ["Orion180.Common"] = "1.4.0" },
            deps, versions, UpdateStrategy.Major);

        Assert.Equal("1.4.0", result.Chosen["Orion180.Common"]);
        Assert.Contains("Orion180.Common", result.Unresolved);
    }

    private static async Task<AlignmentResult> Align(
        Dictionary<string, string> declaredCurrent,
        Dictionary<string, string> chosen,
        IReadOnlyDictionary<string, IReadOnlyList<FamilyDependency>> deps,
        IReadOnlyDictionary<string, IReadOnlyList<string>> versions,
        UpdateStrategy strategy)
    {
        var settings = new TargetingSettings(["Orion180.*"], feeds: ["https://feed"], policy: new UpdatePolicy(strategy, []));

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=k;SharedAccessKey=a2V5a2V5a2V5a2V5";
        builder.AddInfrastructure();
        builder.Services.RemoveAll<IPackageDependencyReader>();
        builder.Services.RemoveAll<IFeedVersionResolver>();
        builder.Services.AddSingleton<IPackageDependencyReader>(new FakeReader(deps));
        builder.Services.AddSingleton<IFeedVersionResolver>(new FakeResolver(versions));

        await using var provider = builder.Services.BuildServiceProvider();
        return await provider.GetRequiredService<IDependencyAligner>()
            .AlignAsync(declaredCurrent, chosen, settings, TestContext.Current.CancellationToken);
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
