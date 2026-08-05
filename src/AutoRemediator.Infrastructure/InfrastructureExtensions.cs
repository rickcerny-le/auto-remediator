using System.Net.Http.Headers;
using System.Text;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Feeds;
using AutoRemediator.Infrastructure.Messaging;
using AutoRemediator.Infrastructure.Remediation;
using AutoRemediator.Infrastructure.Storage;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Infrastructure;

/// <summary>
/// Single entry point that registers all infrastructure clients and abstractions.
///
/// Clients are built in dual mode from the same configuration keys:
/// - a service <b>endpoint</b> (an http(s) URI for Storage, or a bare namespace FQDN
///   for Service Bus) is paired with a <see cref="DefaultAzureCredential"/> (Managed
///   Identity), as injected by the Azure deployment;
/// - a <b>connection string</b> (the local Azurite / Service Bus emulator values
///   injected by Aspire) uses the connection-string constructor.
///
/// The user-assigned identity is selected via the <c>AZURE_CLIENT_ID</c> value.
/// </summary>
public static class InfrastructureExtensions
{
    public const string TablesConnectionName = "tables";
    public const string BlobsConnectionName = "blobs";
    public const string ServiceBusConnectionName = "servicebus";

    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var credential = CreateCredential(config);

        // Azure Storage — Azurite emulator (connection string) locally, real
        // accounts (endpoint + managed identity) in Azure.
        builder.Services.AddSingleton(_ =>
            CreateTableServiceClient(RequireConnectionString(config, TablesConnectionName), credential));
        builder.Services.AddSingleton(_ =>
            CreateBlobServiceClient(RequireConnectionString(config, BlobsConnectionName), credential));

        // Azure Service Bus — emulator (connection string) locally, namespace FQDN
        // + managed identity in Azure.
        builder.Services.AddSingleton(_ =>
            CreateServiceBusClient(RequireConnectionString(config, ServiceBusConnectionName), credential));

        builder.Services.AddSingleton<ITableStore, TableStore>();
        builder.Services.AddSingleton<IBlobStore, BlobStore>();
        builder.Services.AddSingleton<IMessagePublisher, ServiceBusMessagePublisher>();
        builder.Services.AddSingleton<IMessageConsumer, ServiceBusMessageConsumer>();

        // Configuration + run-history stores.
        builder.Services.AddSingleton<IManagedRepositoryStore, TableManagedRepositoryStore>();
        builder.Services.AddSingleton<ITargetingSettingsStore, TableTargetingSettingsStore>();
        builder.Services.AddSingleton<IRemediationRunStore, TableRemediationRunStore>();

        // Azure DevOps connectivity (read-only REST) + PAT Basic auth.
        builder.Services.AddOptions<AzureDevOpsOptions>()
            .Bind(config.GetSection(AzureDevOpsOptions.SectionName));
        builder.Services.AddHttpClient<IAzureDevOpsClient, AzureDevOpsClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<AzureDevOpsOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.OrganizationUrl))
            {
                http.BaseAddress = new Uri(options.OrganizationUrl.TrimEnd('/') + "/");
            }

            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + (options.Pat ?? string.Empty)));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        });

        // Feed version resolution + analysis + planning + remediation.
        builder.Services.AddSingleton<IFeedVersionResolver, FeedVersionResolver>();
        builder.Services.AddScoped<IRepositoryAnalyzer, RepositoryAnalyzer>();
        builder.Services.AddScoped<IDependencyMapService, DependencyMapService>();
        builder.Services.AddScoped<IUpdatePlanner, UpdatePlanner>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IRemediationRunner, RemediationRunner>();

        return builder;
    }

    /// <summary>
    /// Builds a credential for endpoint-based clients, pinned to the user-assigned
    /// identity via <c>AZURE_CLIENT_ID</c> when present.
    /// </summary>
    private static TokenCredential CreateCredential(IConfiguration config)
    {
        var options = new DefaultAzureCredentialOptions();

        var clientId = config["AZURE_CLIENT_ID"];
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            options.ManagedIdentityClientId = clientId;
        }

        return new DefaultAzureCredential(options);
    }

    private static TableServiceClient CreateTableServiceClient(string value, TokenCredential credential)
        => IsHttpEndpoint(value)
            ? new TableServiceClient(new Uri(value), credential)
            : new TableServiceClient(value);

    private static BlobServiceClient CreateBlobServiceClient(string value, TokenCredential credential)
        => IsHttpEndpoint(value)
            ? new BlobServiceClient(new Uri(value), credential)
            : new BlobServiceClient(value);

    private static ServiceBusClient CreateServiceBusClient(string value, TokenCredential credential)
        => IsConnectionString(value)
            ? new ServiceBusClient(value)
            : new ServiceBusClient(value, credential);

    /// <summary>True when the value is an absolute http/https URI (a Storage service endpoint).</summary>
    private static bool IsHttpEndpoint(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>True when the value carries connection-string markers (vs a bare namespace FQDN).</summary>
    private static bool IsConnectionString(string value)
        => value.Contains("Endpoint=", StringComparison.OrdinalIgnoreCase)
           || value.Contains("SharedAccessKey", StringComparison.OrdinalIgnoreCase)
           || value.Contains("SharedAccessSignature", StringComparison.OrdinalIgnoreCase);

    private static string RequireConnectionString(IConfiguration config, string name)
        => config.GetConnectionString(name)
           ?? throw new InvalidOperationException(
               $"Connection value '{name}' was not configured. It is normally supplied by the Aspire AppHost (connection string) or the Azure deployment (endpoint).");
}
