using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Infrastructure.Tests;

/// <summary>Round-trips a run through the Table store; skips when Azurite is not running.</summary>
public class RemediationRunStoreRoundTripTests
{
    [Fact]
    public async Task Run_round_trips_and_lists_by_repository()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=k;SharedAccessKey=a2V5a2V5a2V5a2V5";
        builder.AddInfrastructure();
        await using var provider = builder.Services.BuildServiceProvider();

        var store = provider.GetRequiredService<IRemediationRunStore>();
        var repoId = Guid.NewGuid();
        var run = new RemediationRun(Guid.NewGuid(), repoId, "orion180/platform/web-api", DateTimeOffset.UtcNow);
        run.Completed([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")], "https://pr/1", DateTimeOffset.UtcNow);

        try
        {
            await store.SaveAsync(run, ct);
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            Assert.Skip($"Azurite storage emulator not reachable: {ex.GetType().Name}");
            return;
        }

        var runs = await store.ListByRepositoryAsync(repoId, ct);
        var saved = Assert.Single(runs);
        Assert.Equal(RunStatus.Completed, saved.Status);
        Assert.Equal("https://pr/1", saved.PullRequestUrl);
        Assert.Equal("Orion180.Core", Assert.Single(saved.Updates).PackageId);

        // Global list and by-id lookup (used by the runs UI).
        Assert.Contains(await store.ListAllAsync(ct), r => r.Id == run.Id);
        var byId = await store.GetAsync(run.Id, ct);
        Assert.NotNull(byId);
        Assert.Equal(RunStatus.Completed, byId!.Status);
    }
}
