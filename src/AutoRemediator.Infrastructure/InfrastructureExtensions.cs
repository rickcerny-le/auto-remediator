using System.Net.Http.Headers;
using System.Text;
using AutoRemediator.Contracts;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Alignment;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Feeds;
using AutoRemediator.Infrastructure.Messaging;
using AutoRemediator.Infrastructure.Remediation;
using AutoRemediator.Infrastructure.Storage;
using AutoRemediator.Infrastructure.Verification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Infrastructure;

/// <summary>
/// Single entry point that registers all infrastructure clients and abstractions.
///
/// The Azure clients come from the Aspire client integrations, which resolve each
/// <c>ConnectionStrings:&lt;name&gt;</c> value in dual mode on our behalf — a connection string
/// (the local Azurite / Service Bus emulator values injected by the AppHost) or a service
/// endpoint paired with a credential (as injected by the Azure deployment) — and additionally
/// register health checks, tracing and metrics for each resource.
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

        // Credentials are deliberately left to the integrations' own DefaultAzureCredential.
        // Setting one explicitly forces the credential code path even when the connection value
        // is a connection string, which breaks the emulators: the Service Bus health check then
        // builds its own client from an empty FullyQualifiedNamespace and throws. The
        // user-assigned identity is still selected in Azure, because the deployment injects
        // AZURE_CLIENT_ID as a process environment variable and DefaultAzureCredential reads it.
        builder.AddAzureTableServiceClient(TablesConnectionName);
        builder.AddAzureBlobServiceClient(BlobsConnectionName);

        builder.AddAzureServiceBusClient(
            ServiceBusConnectionName,
            // Without a queue to probe, the integration's health check cannot verify
            // anything beyond client construction.
            settings => settings.HealthCheckQueueName = RemediationQueues.RemediationRuns);

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
        builder.Services.AddSingleton<IPackageDependencyReader, PackageDependencyReader>();
        builder.Services.AddScoped<IDependencyAligner, DependencyAligner>();
        builder.Services.AddScoped<IRepositoryAnalyzer, RepositoryAnalyzer>();
        builder.Services.AddScoped<IDependencyMapService, DependencyMapService>();
        builder.Services.AddScoped<IUpdatePlanner, UpdatePlanner>();

        // Local verification: materialize the tree, restore, then build.
        builder.Services.AddScoped<IVerificationWorkspaceFactory, VerificationWorkspaceFactory>();
        builder.Services.AddSingleton<IDotnetCliRunner, DotnetCliRunner>();
        builder.Services.AddSingleton<IVerificationLogStore, BlobVerificationLogStore>();
        builder.Services.AddScoped<IVerificationService, VerificationService>();

        // AI repair loop. The agent itself is registered by the Agents project; the loop only needs
        // the contract, so infrastructure stays free of the agent framework.
        builder.Services.AddOptions<RemediationLoopOptions>()
            .Bind(config.GetSection(RemediationLoopOptions.SectionName));
        builder.Services.AddScoped<IRemediationLoop, RemediationLoop>();

        // Fallback so infrastructure composes without the agents library at all. A host that calls
        // AddAgents() registers the real agent afterwards, which wins.
        builder.Services.TryAddScoped<IRemediationAgent, UnavailableRemediationAgent>();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IRemediationRunner, RemediationRunner>();

        return builder;
    }
}