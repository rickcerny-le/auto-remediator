using AutoRemediator.Infrastructure;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Messaging;
using AutoRemediator.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Infrastructure.Tests;

public class InfrastructureExtensionsTests
{
    [Fact]
    public async Task AddInfrastructure_registers_all_expected_services()
    {
        var builder = Host.CreateApplicationBuilder();
        // Fake connection strings — construction of the Azure clients is offline (no network).
        builder.Configuration["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] =
            "Endpoint=sb://localhost/;SharedAccessKeyName=key;SharedAccessKey=a2V5a2V5a2V5a2V5a2V5a2V5";

        builder.AddInfrastructure();

        await using var provider = builder.Services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<ITableStore>());
        Assert.NotNull(provider.GetService<IBlobStore>());
        Assert.NotNull(provider.GetService<IMessagePublisher>());
        Assert.NotNull(provider.GetService<IMessageConsumer>());
        Assert.NotNull(provider.GetService<IAzureDevOpsClient>());
    }

    [Fact]
    public async Task AddInfrastructure_resolves_with_endpoints_and_managed_identity()
    {
        var builder = Host.CreateApplicationBuilder();
        // Endpoint-style values (as injected by the Azure deployment) + the
        // user-assigned identity client id. Client construction is offline.
        builder.Configuration["ConnectionStrings:tables"] = "https://examplestg.table.core.windows.net/";
        builder.Configuration["ConnectionStrings:blobs"] = "https://examplestg.blob.core.windows.net/";
        builder.Configuration["ConnectionStrings:servicebus"] = "example-ns.servicebus.windows.net";
        builder.Configuration["AZURE_CLIENT_ID"] = "00000000-0000-0000-0000-000000000000";

        builder.AddInfrastructure();

        await using var provider = builder.Services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<ITableStore>());
        Assert.NotNull(provider.GetService<IBlobStore>());
        Assert.NotNull(provider.GetService<IMessagePublisher>());
        Assert.NotNull(provider.GetService<IMessageConsumer>());
        Assert.NotNull(provider.GetService<IAzureDevOpsClient>());
    }
}
