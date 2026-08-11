using AutoRemediator.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Agents.Tests;

public class AgentsExtensionsTests
{
    [Fact]
    public void AddAgents_registers_remediation_agent_without_network_call()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddAgents();

        using var provider = builder.Services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IRemediationAgent>());
    }

    [Fact]
    public void Loop_bounds_bind_from_configuration()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Agents:MaxAttempts"] = "5";
        builder.Configuration["Agents:TokenBudget"] = "250000";
        builder.Configuration["Agents:AttemptTimeout"] = "00:01:30";

        builder.AddAgents();

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AgentsOptions>>().Value;

        Assert.Equal(5, options.MaxAttempts);
        Assert.Equal(250_000, options.TokenBudget);
        Assert.Equal(TimeSpan.FromMinutes(1.5), options.AttemptTimeout);
    }

    [Fact]
    public void Loop_bounds_have_defaults()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddAgents();

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AgentsOptions>>().Value;

        Assert.Equal(3, options.MaxAttempts);
        Assert.True(options.TokenBudget > 0);
        Assert.True(options.AttemptTimeout > TimeSpan.Zero);
    }

    [Fact]
    public void A_chat_client_is_registered_when_a_model_connection_is_configured()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:chat"] =
            "Endpoint=https://example-foundry.services.ai.azure.com/;Key=abc123;DeploymentName=phi-4";

        builder.AddAgents();

        using var provider = builder.Services.BuildServiceProvider();

        // Resolving the client must not make a network call.
        Assert.NotNull(provider.GetService<IChatClient>());
    }

    [Fact]
    public void Registration_succeeds_without_a_model_connection()
    {
        // The model is a soft dependency: the worker must still start and run mechanical
        // bumps when no model is configured, with only AI repair unavailable.
        var builder = Host.CreateApplicationBuilder();

        builder.AddAgents();

        using var provider = builder.Services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IRemediationAgent>());
        Assert.Null(provider.GetService<IChatClient>());
    }
}
