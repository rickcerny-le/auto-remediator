using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Storage;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Infrastructure.Tests;

/// <summary>
/// Round-trips configuration through the Table stores against the Azurite emulator.
/// Skips when Azurite is not reachable so a plain `dotnet test` (no emulator) stays green;
/// runs when Azurite is up (e.g. the Aspire AppHost or a local azurite instance).
/// </summary>
public class ConfigStoreRoundTripTests
{
    [Fact]
    public async Task Repository_and_settings_round_trip()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = BuildProvider();
        await using var _ = provider;

        var repoStore = provider.GetRequiredService<IManagedRepositoryStore>();
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api", true, "main");

        try
        {
            await repoStore.UpsertAsync(repo, ct);
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            Assert.Skip($"Azurite storage emulator not reachable: {ex.GetType().Name}");
            return;
        }

        var fetched = await repoStore.GetAsync(repo.Id, ct);
        Assert.NotNull(fetched);
        Assert.Equal("contoso/platform/web-api", fetched!.Slug);
        Assert.Equal("main", fetched.TargetBranch);

        var settingsStore = provider.GetRequiredService<ITargetingSettingsStore>();
        await settingsStore.SetAsync(new TargetingSettings(["Contoso.*"], excludes: ["Contoso.Legacy.*"], feeds: ["https://feed"]), ct);
        var settings = await settingsStore.GetAsync(ct);
        Assert.Contains("Contoso.*", settings.Patterns);
        Assert.Contains("Contoso.Legacy.*", settings.Excludes);

        await repoStore.DeleteAsync(repo.Id, ct);
    }

    // ---- Entity-level tests: no Azurite needed. ----

    [Fact]
    public void Entity_inherit_everything_repository_writes_no_settings_columns()
    {
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api", true, "main");

        var e = ManagedRepositoryEntity.FromDomain(repo);

        Assert.Null(e.InheritGlobal);
        Assert.Null(e.PatternsJson);
        Assert.Null(e.ExcludesJson);
        Assert.Null(e.FeedsJson);
        Assert.Null(e.IgnoreJson);
        Assert.Null(e.Strategy);
        Assert.Null(e.AllowPrerelease);
        Assert.Null(e.Schedule);
    }

    [Fact]
    public void Entity_round_trips_every_per_repo_field()
    {
        var settings = new RepositorySettings(
            inheritGlobal: false,
            patterns: ["Contoso.*"],
            excludes: ["Contoso.Legacy.*"],
            feeds: ["https://feed/v3/index.json"],
            ignore: ["Contoso.Pinned"],
            strategy: UpdateStrategy.Major,
            allowPrerelease: false,
            schedule: "0 3 * * *");
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api", false, "develop", settings);

        var back = ManagedRepositoryEntity.FromDomain(repo).ToDomain();

        Assert.Equal(repo.Id, back.Id);
        Assert.False(back.Enabled);
        Assert.Equal("develop", back.TargetBranch);
        AssertSettingsEqual(settings, back.Settings);
    }

    [Fact]
    public void Entity_without_settings_columns_reads_as_inherit()
    {
        var e = new ManagedRepositoryEntity { RowKey = Guid.NewGuid().ToString(), Organization = "o", Project = "p", Name = "n", Enabled = true };

        var settings = e.ToDomain().Settings;

        Assert.True(settings.InheritGlobal);
        Assert.False(settings.HasOverrides);
        AssertSettingsEqual(RepositorySettings.Inherit, settings);
    }

    [Fact]
    public void Entity_unparseable_strategy_and_blank_lists_read_as_unset()
    {
        var e = new ManagedRepositoryEntity
        {
            RowKey = Guid.NewGuid().ToString(), Organization = "o", Project = "p", Name = "n",
            Strategy = "Bogus", PatternsJson = "  ", Schedule = "  ",
        };

        var settings = e.ToDomain().Settings;

        Assert.Null(settings.Strategy);
        Assert.Empty(settings.Patterns);
        Assert.Null(settings.Schedule);
    }

    [Fact]
    public void Global_settings_entity_default_schedule_is_nullable()
    {
        Assert.Null(new TargetingSettingsEntity().DefaultSchedule);
    }

    // ---- Azurite-backed tests (self-skipping). ----

    [Fact]
    public async Task Repository_with_every_per_repo_field_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var repoStore = provider.GetRequiredService<IManagedRepositoryStore>();
        var settings = new RepositorySettings(
            false, ["Contoso.*", "Fabrikam.*"], ["Contoso.Legacy.*"], ["https://feed"], ["Tailspin.Legacy.*"],
            UpdateStrategy.Patch, true, "*/30 * * * *");
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "all-fields", true, "main", settings);

        if (!await TryAsync(() => repoStore.UpsertAsync(repo, ct)))
        {
            return;
        }

        try
        {
            var fetched = await repoStore.GetAsync(repo.Id, ct);
            Assert.NotNull(fetched);
            AssertSettingsEqual(settings, fetched!.Settings);

            var listed = (await repoStore.ListAsync(ct)).Single(r => r.Id == repo.Id);
            AssertSettingsEqual(settings, listed.Settings);
        }
        finally
        {
            await repoStore.DeleteAsync(repo.Id, ct);
        }
    }

    [Fact]
    public async Task Inherit_everything_repository_writes_no_settings_columns_and_reads_back_as_inherit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var repoStore = provider.GetRequiredService<IManagedRepositoryStore>();
        var tables = provider.GetRequiredService<ITableStore>();
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "inherits", true, "main");

        if (!await TryAsync(() => repoStore.UpsertAsync(repo, ct)))
        {
            return;
        }

        try
        {
            var table = await tables.GetTableAsync("repositories", ct);
            var raw = (await table.GetEntityAsync<TableEntity>("repo", repo.Id.ToString(), cancellationToken: ct)).Value;
            foreach (var column in new[] { "InheritGlobal", "PatternsJson", "ExcludesJson", "FeedsJson", "IgnoreJson", "Strategy", "AllowPrerelease", "Schedule" })
            {
                Assert.True(!raw.ContainsKey(column) || raw[column] is null, $"{column} should not be stored.");
            }

            var fetched = await repoStore.GetAsync(repo.Id, ct);
            AssertSettingsEqual(RepositorySettings.Inherit, fetched!.Settings);
        }
        finally
        {
            await repoStore.DeleteAsync(repo.Id, ct);
        }
    }

    [Fact]
    public async Task Legacy_repository_row_loads_as_inherit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var repoStore = provider.GetRequiredService<IManagedRepositoryStore>();
        var tables = provider.GetRequiredService<ITableStore>();
        var id = Guid.NewGuid();
        var legacy = new TableEntity("repo", id.ToString())
        {
            ["Organization"] = "contoso",
            ["Project"] = "platform",
            ["Name"] = "legacy",
            ["Enabled"] = true,
            ["TargetBranch"] = "main",
        };

        if (!await TryAsync(async () =>
        {
            var table = await tables.GetTableAsync("repositories", ct);
            await table.UpsertEntityAsync(legacy, TableUpdateMode.Replace, ct);
        }))
        {
            return;
        }

        try
        {
            var fetched = await repoStore.GetAsync(id, ct);
            Assert.NotNull(fetched);
            Assert.Equal("contoso/platform/legacy", fetched!.Slug);
            Assert.True(fetched.Enabled);
            Assert.True(fetched.Settings.InheritGlobal);
            AssertSettingsEqual(RepositorySettings.Inherit, fetched.Settings);
        }
        finally
        {
            await repoStore.DeleteAsync(id, ct);
        }
    }

    [Fact]
    public async Task Global_settings_round_trip_with_and_without_default_schedule()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<ITargetingSettingsStore>();

        TargetingSettings? original = null;
        if (!await TryAsync(async () => original = await store.GetAsync(ct)))
        {
            return;
        }

        try
        {
            await store.SetAsync(new TargetingSettings(["Contoso.*"], defaultSchedule: "0 */6 * * *"), ct);
            Assert.Equal("0 */6 * * *", (await store.GetAsync(ct)).DefaultSchedule);

            await store.SetAsync(new TargetingSettings(["Contoso.*"]), ct);
            Assert.Null((await store.GetAsync(ct)).DefaultSchedule);
        }
        finally
        {
            await store.SetAsync(original!, ct);
        }
    }

    [Fact]
    public async Task Legacy_global_settings_row_has_no_default_schedule()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<ITargetingSettingsStore>();
        var tables = provider.GetRequiredService<ITableStore>();

        TargetingSettings? original = null;
        TableClient? table = null;
        if (!await TryAsync(async () =>
        {
            original = await store.GetAsync(ct);
            table = await tables.GetTableAsync("config", ct);
        }))
        {
            return;
        }

        try
        {
            var legacy = new TableEntity("settings", "global")
            {
                ["PatternsJson"] = "[\"Contoso.*\"]",
                ["ExcludesJson"] = "[]",
                ["FeedsJson"] = "[\"https://feed\"]",
                ["IgnoreJson"] = "[]",
                ["Strategy"] = "Patch",
                ["AllowPrerelease"] = false,
            };
            await table!.UpsertEntityAsync(legacy, TableUpdateMode.Replace, ct);

            var loaded = await store.GetAsync(ct);

            Assert.Null(loaded.DefaultSchedule);
            Assert.Equal(["Contoso.*"], loaded.Patterns);
            Assert.Equal(UpdateStrategy.Patch, loaded.Policy.Strategy);
        }
        finally
        {
            await store.SetAsync(original!, ct);
        }
    }

    private static async Task<bool> TryAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            Assert.Skip($"Azurite storage emulator not reachable: {ex.GetType().Name}");
            return false;
        }
    }

    private static void AssertSettingsEqual(RepositorySettings expected, RepositorySettings actual)
    {
        Assert.Equal(expected.InheritGlobal, actual.InheritGlobal);
        Assert.Equal(expected.Patterns, actual.Patterns);
        Assert.Equal(expected.Excludes, actual.Excludes);
        Assert.Equal(expected.Feeds, actual.Feeds);
        Assert.Equal(expected.Ignore, actual.Ignore);
        Assert.Equal(expected.Strategy, actual.Strategy);
        Assert.Equal(expected.AllowPrerelease, actual.AllowPrerelease);
        Assert.Equal(expected.Schedule, actual.Schedule);
    }

    private static ServiceProvider BuildProvider()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] =
            "Endpoint=sb://localhost/;SharedAccessKeyName=key;SharedAccessKey=a2V5a2V5a2V5a2V5a2V5a2V5";
        builder.AddInfrastructure();
        return builder.Services.BuildServiceProvider();
    }
}
