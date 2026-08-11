using AutoRemediator.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Agents;

/// <summary>
/// Registers the Microsoft Agent Framework remediation agent over a chat client supplied by
/// the Aspire AI Inference integration. The model connection comes from a connection
/// reference, so this registration is identical whether the model runs locally in
/// development or in Azure. No network call is made at registration time.
/// </summary>
public static class AgentsExtensions
{
    public static IHostApplicationBuilder AddAgents(this IHostApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<AgentsOptions>()
            .Bind(builder.Configuration.GetSection(AgentsOptions.SectionName));

        // Only register the chat client when a connection was supplied. The model is a soft
        // dependency: without it the system still bumps, verifies and opens pull requests, and
        // only AI repair of compile breaks is unavailable.
        if (HasChatConnection(builder.Configuration))
        {
            builder.AddAzureChatCompletionsClient(AgentsOptions.ChatConnectionName)
                .AddChatClient();
        }

        builder.Services.AddSingleton<IRemediationAgent, MafRemediationAgent>();

        return builder;
    }

    /// <summary>True when a model connection is configured for the chat client to resolve.</summary>
    private static bool HasChatConnection(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(configuration.GetConnectionString(AgentsOptions.ChatConnectionName));
}
