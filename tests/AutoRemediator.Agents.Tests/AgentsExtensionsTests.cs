using AutoRemediator.Agents;
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
        builder.Configuration["Agents:FoundryEndpoint"] = "https://example-foundry.services.ai.azure.com";
        builder.Configuration["Agents:ModelDeploymentName"] = "gpt-4o";

        builder.AddAgents();

        using var provider = builder.Services.BuildServiceProvider();

        var agent = provider.GetService<IRemediationAgent>();
        Assert.NotNull(agent);

        var options = provider.GetRequiredService<IOptions<AgentsOptions>>().Value;
        Assert.Equal("https://example-foundry.services.ai.azure.com", options.FoundryEndpoint);
        Assert.Equal("gpt-4o", options.ModelDeploymentName);
    }
}
