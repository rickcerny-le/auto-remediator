using AutoRemediator.Infrastructure.Verification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Infrastructure.Tests;

/// <summary>Round-trips a verification log through blob storage; skips when Azurite is not running.</summary>
public class VerificationLogStoreTests
{
    private static ServiceProvider BuildProvider()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] = "Endpoint=sb://localhost/;SharedAccessKeyName=k;SharedAccessKey=a2V5a2V5a2V5a2V5";
        builder.AddInfrastructure();
        return builder.Services.BuildServiceProvider();
    }

    [Fact]
    public async Task Log_round_trips_through_blob_storage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IVerificationLogStore>();

        var runId = Guid.NewGuid();
        var content = $"=== dotnet restore ==={Environment.NewLine}Restore succeeded.";

        string? reference;
        try
        {
            reference = await store.StoreAsync(runId, content, ct);
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            Assert.Skip($"Azurite storage emulator not reachable: {ex.GetType().Name}");
            return;
        }

        if (reference is null)
        {
            Assert.Skip("Azurite storage emulator not reachable (upload returned no reference).");
            return;
        }

        Assert.Contains(runId.ToString(), reference);
        Assert.Equal(content, await store.ReadAsync(reference, ct));
    }

    [Fact]
    public async Task An_unknown_reference_reads_as_null()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<IVerificationLogStore>();

        // Force the container to exist so a missing blob is distinguishable from a missing emulator.
        if (await store.StoreAsync(Guid.NewGuid(), "probe", ct) is null)
        {
            Assert.Skip("Azurite storage emulator not reachable.");
            return;
        }

        Assert.Null(await store.ReadAsync($"{Guid.NewGuid()}/verification.log", ct));
    }
}
