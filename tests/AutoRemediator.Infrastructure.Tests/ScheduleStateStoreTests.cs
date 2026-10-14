using AutoRemediator.Infrastructure.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Infrastructure.Tests;

/// <summary>
/// Exercises the schedule-state claim against the Azurite emulator. Skips when Azurite is not
/// reachable so a plain `dotnet test` stays green.
/// </summary>
public class ScheduleStateStoreTests
{
    [Fact]
    public async Task Claims_insert_then_respect_the_etag()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IScheduleStateStore>();
        var id = Guid.NewGuid();
        var t1 = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);

        ScheduleState? initial;
        try
        {
            initial = await store.GetAsync(id, ct);
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            Assert.Skip($"Azurite storage emulator not reachable: {ex.GetType().Name}");
            return;
        }

        Assert.Null(initial);

        // First claim inserts; a second insert for the same row (stale "no row" view) loses.
        Assert.True(await store.TryClaimAsync(id, null, t1, ct));
        Assert.False(await store.TryClaimAsync(id, null, t1.AddMinutes(1), ct));

        var first = await store.GetAsync(id, ct);
        Assert.NotNull(first);
        Assert.Equal(t1, first!.LastScheduledAtUtc);

        // A claim with the fresh ETag succeeds; replaying the now-stale ETag fails.
        Assert.True(await store.TryClaimAsync(id, first, t1.AddMinutes(15), ct));
        Assert.False(await store.TryClaimAsync(id, first, t1.AddMinutes(30), ct));

        var second = await store.GetAsync(id, ct);
        Assert.Equal(t1.AddMinutes(15), second!.LastScheduledAtUtc);

        await store.RevertAsync(id, t1, ct);
        Assert.Equal(t1, (await store.GetAsync(id, ct))!.LastScheduledAtUtc);
    }

    private static ServiceProvider BuildProvider()
    {
        var builder = Host.CreateApplicationBuilder();
        // AZURITE_TABLES_CONNECTION lets the test target an emulator on a non-default port (the Aspire AppHost
        // publishes Azurite on a dynamic one).
        builder.Configuration["ConnectionStrings:tables"] =
            Environment.GetEnvironmentVariable("AZURITE_TABLES_CONNECTION") ?? "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] =
            "Endpoint=sb://localhost/;SharedAccessKeyName=key;SharedAccessKey=a2V5a2V5a2V5a2V5a2V5a2V5";
        builder.AddInfrastructure();
        return builder.Services.BuildServiceProvider();
    }
}
