using AutoRemediator.Agents;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Remediation;
using AutoRemediator.Infrastructure.Verification;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Infrastructure.Tests;

/// <summary>
/// The whole loop against a real local model: real extraction, real `dotnet restore`/`build`, the
/// real agent over a real chat client, and the real loop deciding when to stop. Only the Azure
/// DevOps archive call is faked, because the tree comes from a zip either way.
///
/// Skips unless <c>AUTOREMEDIATOR_CHAT_CONNECTION</c> names a reachable model.
///
/// Asserts loop **mechanics**, never a repair. A small local development model may well fail to fix
/// the break; that is expected and is not a defect in this system. Whether it repaired is reported
/// to the test output so a human can see it.
/// </summary>
public class RemediationLoopLocalModelTests(ITestOutputHelper output)
{
    private const string ConnectionVariable = "AUTOREMEDIATOR_CHAT_CONNECTION";

    private static readonly ManagedRepository Repo = new(Guid.NewGuid(), "orion180", "platform", "web-api");

    private static TargetingSettings Settings => new(["Orion180.*"], feeds: []);

    private const string Csproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>disable</Nullable>
          </PropertyGroup>
        </Project>
        """;

    /// <summary>A break of exactly the shape a package upgrade causes: a renamed method.</summary>
    private const string BrokenProgram = """
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
            public string SayHello(string name)
            {
                return "Hello, " + name + "!";
            }
        }
        """;

    [Fact]
    public async Task The_loop_runs_end_to_end_against_a_real_local_model()
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connection))
        {
            Assert.Skip($"Set {ConnectionVariable} to a reachable model connection to run this test.");
            return;
        }

        var ct = TestContext.Current.CancellationToken;

        // Real agent over a real chat client.
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Configuration["ConnectionStrings:chat"] = connection;
        hostBuilder.AddAgents();
        await using var provider = hostBuilder.Services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IChatClient>());
        var agent = provider.GetRequiredService<IRemediationAgent>();
        Assert.True(agent.IsAvailable);

        // Real verification over a synthetic repository whose build is broken.
        var workspaces = new VerificationWorkspaceFactory(
            new ArchiveAdo(() => TestArchive.Of(
                ("src/App/App.csproj", Csproj),
                ("src/App/Program.cs", BrokenProgram))),
            Options.Create(new AzureDevOpsOptions { Pat = null }),
            NullLogger<VerificationWorkspaceFactory>.Instance);

        var verification = new VerificationService(
            workspaces,
            new DotnetCliRunner(NullLogger<DotnetCliRunner>.Instance),
            new NullLogs(),
            NullLogger<VerificationService>.Instance);

        using var session = await verification.OpenAsync(
            Guid.NewGuid(), Repo, "commit-abc", new RepositoryUpdatePlan([], []), Settings, ct);

        var first = await session.VerifyAsync(ct);

        // The premise: the tree really does not compile, with a real compiler diagnostic.
        Assert.Equal(VerificationClassification.DependencyFailure, first.Outcome.Classification);
        Assert.Contains(first.Outcome.Diagnostics, d => d.Code.StartsWith("CS", StringComparison.Ordinal));

        var loop = new RemediationLoop(
            agent,
            Options.Create(new RemediationLoopOptions { MaxAttempts = 2 }),
            NullLogger<RemediationLoop>.Instance);

        var result = await loop.RunAsync(Guid.NewGuid(), session, first, ct);

        output.WriteLine($"repaired: {result.Repaired}; attempts: {result.Attempts}");
        output.WriteLine(result.Transcript);

        // Mechanics, not effectiveness.
        Assert.InRange(result.Attempts, 1, 2);
        Assert.Contains("# AI remediation for run", result.Transcript);
        Assert.Contains("## Attempt 1", result.Transcript);
        Assert.Contains(first.Outcome.Diagnostics[0].Code, result.Transcript);

        if (result.Repaired)
        {
            // If it did repair, the change must be real: verified, and offering the edited source.
            Assert.Equal(VerificationClassification.Verified, result.Result.Outcome.Classification);
            var edits = await session.Workspace!.AppliedEditChangesAsync(ct);
            Assert.NotEmpty(edits);
            Assert.All(edits, e => Assert.StartsWith("/src/App/", e.Path));
        }
        else
        {
            // Otherwise the floor holds: still rejected, and nothing is offered for a commit.
            Assert.NotEqual(VerificationClassification.Verified, result.Result.Outcome.Classification);
        }
    }

    private sealed class ArchiveAdo(Func<Stream> archive) : IAzureDevOpsClient
    {
        public Task<bool> RepositoryExistsAsync(ManagedRepository r, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<RepositoryFile>> GetManifestsAsync(ManagedRepository r, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RepositoryFile>>([]);
        public Task<string?> GetBranchHeadAsync(ManagedRepository r, string b, CancellationToken ct = default)
            => Task.FromResult<string?>("commit-abc");
        public Task<Stream> GetRepositoryArchiveAsync(ManagedRepository r, string commitId, CancellationToken ct = default)
            => Task.FromResult(archive());
        public Task PushFilesAsync(ManagedRepository r, string b, string c, IReadOnlyList<FileChange> ch, string m, CancellationToken ct = default)
            => throw new NotSupportedException("the loop never pushes");
        public Task<string> EnsurePullRequestAsync(ManagedRepository r, string s, string t, string ti, string d, CancellationToken ct = default)
            => throw new NotSupportedException("the loop never opens a pull request");
    }

    private sealed class NullLogs : IVerificationLogStore
    {
        public Task<string?> StoreAsync(Guid runId, string content, string name = "verification.log", CancellationToken ct = default)
            => Task.FromResult<string?>($"{runId}/{name}");

        public Task<string?> ReadAsync(string reference, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }
}
