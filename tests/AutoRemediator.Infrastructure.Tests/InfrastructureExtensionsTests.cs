using AutoRemediator.Infrastructure;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Messaging;
using AutoRemediator.Infrastructure.Storage;
using Azure.Data.Tables;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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

    [Fact]
    public async Task Azure_clients_come_from_the_Aspire_integrations()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:tables"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] = "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] =
            "Endpoint=sb://localhost/;SharedAccessKeyName=key;SharedAccessKey=a2V5a2V5a2V5a2V5a2V5a2V5";

        builder.AddInfrastructure();

        await using var provider = builder.Services.BuildServiceProvider();

        // The clients themselves resolve...
        Assert.NotNull(provider.GetService<TableServiceClient>());
        Assert.NotNull(provider.GetService<BlobServiceClient>());
        Assert.NotNull(provider.GetService<ServiceBusClient>());

        // ...and each integration contributed a health check, which the hand-rolled
        // registrations never did. This is the point of using them.
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        Assert.Contains(registrations, r => r.Name.Contains("table", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(registrations, r => r.Name.Contains("blob", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(registrations, r => r.Name.Contains("servicebus", StringComparison.OrdinalIgnoreCase)
                                            || r.Name.Contains("azure_service_bus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_missing_connection_value_is_reported_with_its_name()
    {
        var builder = Host.CreateApplicationBuilder();
        // No ConnectionStrings at all.

        var ex = Record.Exception(() => builder.AddInfrastructure());

        // Aspire validates the connection value; whether it throws at registration or on
        // resolution, the failure must name the missing connection so it is diagnosable.
        if (ex is null)
        {
            await using var provider = builder.Services.BuildServiceProvider();
            ex = Record.Exception(() => provider.GetService<TableServiceClient>());
        }

        Assert.NotNull(ex);
        Assert.Contains("tables", ex!.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
