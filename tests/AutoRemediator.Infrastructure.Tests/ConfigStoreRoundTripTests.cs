using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
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
