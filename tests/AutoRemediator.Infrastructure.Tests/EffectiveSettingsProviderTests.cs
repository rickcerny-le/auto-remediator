using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;

namespace AutoRemediator.Infrastructure.Tests;

public class EffectiveSettingsProviderTests
{
    private static readonly TargetingSettings Global = new(
        ["Contoso.*"],
        excludes: ["Contoso.Legacy.*"],
        feeds: ["https://global-feed"],
        policy: new UpdatePolicy(UpdateStrategy.Minor, ["Contoso.Pinned"], AllowPrerelease: false),
        defaultSchedule: "0 */6 * * *");

    private static ManagedRepository Repo(string name, RepositorySettings? settings = null)
        => new(Guid.NewGuid(), "contoso", "platform", name, true, "main", settings);

    [Fact]
    public async Task Inheriting_repository_gets_the_global_settings()
    {
        var store = new InMemorySettingsStore { Current = Global };
        var provider = new EffectiveSettingsProvider(store);

        var effective = await provider.GetAsync(Repo("inherits"), TestContext.Current.CancellationToken);

        Assert.Equal(["Contoso.*"], effective.Targeting.Patterns);
        Assert.Equal(["Contoso.Legacy.*"], effective.Targeting.Excludes);
        Assert.Equal(["https://global-feed"], effective.Targeting.Feeds);
        Assert.Equal(UpdateStrategy.Minor, effective.Targeting.Policy.Strategy);
        Assert.Equal("0 */6 * * *", effective.Schedule.Value);
    }

    [Fact]
    public async Task Repository_overrides_are_layered_over_the_global_settings()
    {
        var store = new InMemorySettingsStore { Current = Global };
        var provider = new EffectiveSettingsProvider(store);
        var repo = Repo("overrides", new RepositorySettings(
            patterns: ["Fabrikam.*"], strategy: UpdateStrategy.Major, allowPrerelease: true, schedule: "0 3 * * *"));

        var effective = await provider.GetAsync(repo, TestContext.Current.CancellationToken);

        Assert.Equal(["Contoso.*", "Fabrikam.*"], effective.Targeting.Patterns);
        Assert.Equal(UpdateStrategy.Major, effective.Targeting.Policy.Strategy);
        Assert.True(effective.Targeting.Policy.AllowPrerelease);
        Assert.Equal("0 3 * * *", effective.Schedule.Value);
    }

    [Fact]
    public async Task Provider_reads_changed_global_settings_on_each_call()
    {
        var store = new InMemorySettingsStore { Current = Global };
        var provider = new EffectiveSettingsProvider(store);
        var repo = Repo("fresh");
        var ct = TestContext.Current.CancellationToken;

        await provider.GetAsync(repo, ct);
        store.Current = new TargetingSettings(["Northwind.*"]);
        var effective = await provider.GetAsync(repo, ct);

        Assert.Equal(["Northwind.*"], effective.Targeting.Patterns);
        Assert.Equal(BuiltInDefaults.Schedule, effective.Schedule.Value);
    }

    [Fact]
    public async Task Batch_overload_reads_global_settings_once_and_keys_by_id()
    {
        var store = new InMemorySettingsStore { Current = Global };
        var provider = new EffectiveSettingsProvider(store);
        var inherits = Repo("inherits");
        var overrides = Repo("overrides", new RepositorySettings(inheritGlobal: false, patterns: ["AdventureWorks.*"]));

        var result = await provider.GetAsync([inherits, overrides], TestContext.Current.CancellationToken);

        Assert.Equal(1, store.GetCount);
        Assert.Equal(2, result.Count);
        Assert.Equal(["Contoso.*"], result[inherits.Id].Targeting.Patterns);
        Assert.Equal(["AdventureWorks.*"], result[overrides.Id].Targeting.Patterns);
    }

    [Fact]
    public async Task Batch_overload_with_no_repositories_returns_empty()
    {
        var provider = new EffectiveSettingsProvider(new InMemorySettingsStore { Current = Global });

        var result = await provider.GetAsync(Array.Empty<ManagedRepository>(), TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }
}
