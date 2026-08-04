using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Agents;

/// <summary>
/// Registers the Microsoft Agent Framework agents. No network call is made at
/// registration time; the Foundry client is created lazily in a later change.
/// </summary>
public static class AgentsExtensions
{
    public static IHostApplicationBuilder AddAgents(this IHostApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<AgentsOptions>()
            .Bind(builder.Configuration.GetSection(AgentsOptions.SectionName));

        builder.Services.AddSingleton<IRemediationAgent, MafRemediationAgent>();

        return builder;
    }
}
