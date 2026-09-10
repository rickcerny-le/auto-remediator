using AutoRemediator.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Agents.Tests;

/// <summary>
/// Exercises the agent against a **real** model, to prove the pieces a fake chat client cannot:
/// that the registration wired from a connection string produces a working client, and that a real
/// model's reply survives our parser.
///
/// Skips unless <c>AUTOREMEDIATOR_CHAT_CONNECTION</c> names a reachable model, so the suite stays
/// runnable without Foundry Local. Deliberately asserts **mechanics, not repair quality** — a local
/// development model is far weaker at this task than a deployed one, and asserting a fix would be
/// asserting a property of the model rather than of this system.
/// </summary>
public class LocalModelAgentTests
{
    private const string ConnectionVariable = "AUTOREMEDIATOR_CHAT_CONNECTION";

    private static readonly VerificationDiagnostic Diagnostic = new(
        "CS1061",
        "'Greeter' does not contain a definition for 'Greet' and no accessible extension method 'Greet' accepting a first argument of type 'Greeter' could be found",
        "src/App/Program.cs",
        5,
        27);

    private const string BrokenSource = """
        public static class Program
        {
            public static void Main()
            {
                var greeter = new Greeter();
                System.Console.WriteLine(greeter.Greet("world"));
            }
        }

        public sealed class Greeter
        {
            // The upgraded package renamed Greet to SayHello.
            public string SayHello(string name) => $"Hello, {name}!";
        }
        """;

    private static IRemediationAgent? ResolveAgent(out ServiceProvider? provider)
    {
        provider = null;

        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connection))
        {
            return null;
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:chat"] = connection;
        builder.AddAgents();

        provider = builder.Services.BuildServiceProvider();
        return provider.GetRequiredService<IRemediationAgent>();
    }

    [Fact]
    public async Task The_real_model_returns_something_the_parser_accepts()
    {
        var agent = ResolveAgent(out var provider);
        using (provider)
        {
            if (agent is null)
            {
                Assert.Skip($"Set {ConnectionVariable} to a reachable model connection to run this test.");
                return;
            }

            Assert.True(agent.IsAvailable);

            var attempt = new RemediationAttempt(
                1,
                [Diagnostic],
                new InMemorySource(new Dictionary<string, string> { ["src/App/Program.cs"] = BrokenSource }));

            var proposal = await agent.ProposeAsync(attempt, TestContext.Current.CancellationToken);

            // Mechanics only: the call completed, usage was reported, and whatever came back either
            // parsed into edits for the file we offered or was rejected cleanly.
            Assert.NotNull(proposal);
            Assert.True(proposal.TokensUsed >= 0);
            Assert.All(proposal.Edits, e => Assert.Equal("src/App/Program.cs", e.Path));
            Assert.All(proposal.Edits, e => Assert.False(string.IsNullOrWhiteSpace(e.NewContent)));
        }
    }

    [Fact]
    public async Task The_real_model_call_reports_token_usage()
    {
        var agent = ResolveAgent(out var provider);
        using (provider)
        {
            if (agent is null)
            {
                Assert.Skip($"Set {ConnectionVariable} to a reachable model connection to run this test.");
                return;
            }

            var attempt = new RemediationAttempt(
                1,
                [Diagnostic],
                new InMemorySource(new Dictionary<string, string> { ["src/App/Program.cs"] = BrokenSource }));

            var proposal = await agent.ProposeAsync(attempt, TestContext.Current.CancellationToken);

            // Without usage the run-level token budget cannot be enforced against a real model.
            Assert.True(proposal.TokensUsed > 0, "the model reported no token usage, so the budget cannot be enforced");
        }
    }

    private sealed class InMemorySource(IReadOnlyDictionary<string, string> files) : ISourceReader
    {
        public Task<string?> ReadAsync(string path, CancellationToken ct = default)
            => Task.FromResult(files.TryGetValue(path, out var content) ? content : null);
    }
}
