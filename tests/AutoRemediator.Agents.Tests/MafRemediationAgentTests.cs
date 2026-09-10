using AutoRemediator.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Agents.Tests;

public class MafRemediationAgentTests
{
    private const string BrokenSource = """
        public static class Program
        {
            public static void Main() => new Client().SubmitAsync();
        }
        """;

    private static readonly VerificationDiagnostic Diagnostic =
        new("CS1061", "'Client' has no member 'SubmitAsync'", "src/App/Program.cs", 3, 42);

    private static MafRemediationAgent Agent(IChatClient? client, AgentsOptions? options = null) =>
        new(Options.Create(options ?? new AgentsOptions()),
            NullLogger<MafRemediationAgent>.Instance,
            client);

    private static RemediationAttempt Attempt(
        int number = 1,
        IReadOnlyList<VerificationDiagnostic>? diagnostics = null,
        IReadOnlyDictionary<string, string>? sources = null)
        => new(number,
            diagnostics ?? [Diagnostic],
            new FakeSource(sources ?? new Dictionary<string, string> { ["src/App/Program.cs"] = BrokenSource }));

    // ---- Availability ---------------------------------------------------------------

    [Fact]
    public async Task Without_a_chat_client_the_agent_is_unavailable_and_proposes_nothing()
    {
        var agent = Agent(client: null);

        Assert.False(agent.IsAvailable);

        var proposal = await agent.ProposeAsync(Attempt(), TestContext.Current.CancellationToken);

        Assert.False(proposal.HasEdits);
        Assert.Contains("no model is configured", proposal.Summary);
    }

    [Fact]
    public void With_a_chat_client_the_agent_is_available()
    {
        Assert.True(Agent(new FakeChatClient("""{"edits":[]}""")).IsAvailable);
    }

    // ---- Prompt -----------------------------------------------------------------------

    [Fact]
    public async Task The_prompt_carries_the_diagnostics_and_the_source()
    {
        var client = new FakeChatClient("""{"edits":[]}""");

        await Agent(client).ProposeAsync(Attempt(), TestContext.Current.CancellationToken);

        var prompt = client.LastUserPrompt!;
        Assert.Contains("CS1061", prompt);
        Assert.Contains("src/App/Program.cs:3:42", prompt);
        Assert.Contains("'Client' has no member 'SubmitAsync'", prompt);
        Assert.Contains("SubmitAsync()", prompt);
    }

    [Fact]
    public async Task The_system_prompt_forbids_touching_manifests()
    {
        var client = new FakeChatClient("""{"edits":[]}""");

        await Agent(client).ProposeAsync(Attempt(), TestContext.Current.CancellationToken);

        Assert.Contains("Directory.Packages.props", client.LastSystemPrompt);
        Assert.Contains("lock file", client.LastSystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_later_attempt_tells_the_model_the_previous_one_failed()
    {
        var client = new FakeChatClient("""{"edits":[]}""");

        await Agent(client).ProposeAsync(Attempt(number: 3), TestContext.Current.CancellationToken);

        Assert.Contains("Attempt 3", client.LastUserPrompt);
    }

    [Fact]
    public async Task Diagnostics_without_a_readable_file_produce_no_call()
    {
        var client = new FakeChatClient("""{"edits":[]}""");

        var proposal = await Agent(client).ProposeAsync(
            Attempt(sources: new Dictionary<string, string>()), TestContext.Current.CancellationToken);

        Assert.False(proposal.HasEdits);
        Assert.Contains("readable source file", proposal.Summary);
        Assert.Equal(0, client.Calls);
    }

    // ---- Response parsing -------------------------------------------------------------

    [Fact]
    public async Task A_well_formed_reply_becomes_a_proposed_edit()
    {
        var client = new FakeChatClient("""
            {"edits":[{"path":"src/App/Program.cs","content":"public static class Program { public static void Main() { } }"}]}
            """);

        var proposal = await Agent(client).ProposeAsync(Attempt(), TestContext.Current.CancellationToken);

        var edit = Assert.Single(proposal.Edits);
        Assert.Equal("src/App/Program.cs", edit.Path);
        Assert.Contains("Main() { }", edit.NewContent);
    }

    [Fact]
    public async Task Prose_and_code_fences_around_the_json_are_tolerated()
    {
        var client = new FakeChatClient("""
            Here is the fix:
            ```json
            {"edits":[{"path":"src/App/Program.cs","content":"fixed"}]}
            ```
            Hope that helps!
            """);

        var proposal = await Agent(client).ProposeAsync(Attempt(), TestContext.Current.CancellationToken);

        Assert.Equal("fixed", Assert.Single(proposal.Edits).NewContent);
    }

    [Fact]
    public async Task Backslashed_paths_are_normalized()
    {
        var client = new FakeChatClient("""
            {"edits":[{"path":"src\\App\\Program.cs","content":"fixed"}]}
            """);

        var proposal = await Agent(client).ProposeAsync(Attempt(), TestContext.Current.CancellationToken);

        Assert.Equal("src/App/Program.cs", Assert.Single(proposal.Edits).Path);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("{ this is { broken json ]")]
    [InlineData("""{"something":"else"}""")]
    [InlineData("""{"edits":"not an array"}""")]
    [InlineData("""{"edits":[{"path":"src/App/Program.cs"}]}""")]
    [InlineData("""{"edits":[{"content":"orphaned"}]}""")]
    public async Task A_malformed_reply_yields_no_edits_rather_than_throwing(string reply)
    {
        var proposal = await Agent(new FakeChatClient(reply))
            .ProposeAsync(Attempt(), TestContext.Current.CancellationToken);

        Assert.False(proposal.HasEdits);
    }

    [Fact]
    public async Task An_edit_to_a_file_never_offered_is_dropped()
    {
        // Belt and braces — the caller enforces the full safety boundary, but the agent should not
        // pass on an edit to a file the model was never shown.
        var client = new FakeChatClient("""
            {"edits":[{"path":"Directory.Packages.props","content":"<Project />"}]}
            """);

        var proposal = await Agent(client).ProposeAsync(Attempt(), TestContext.Current.CancellationToken);

        Assert.False(proposal.HasEdits);
    }

    // ---- Budget and timeout -----------------------------------------------------------

    [Fact]
    public async Task Token_usage_is_reported_so_the_caller_can_enforce_a_budget()
    {
        var client = new FakeChatClient("""{"edits":[]}""") { TotalTokens = 4321 };

        var proposal = await Agent(client).ProposeAsync(Attempt(), TestContext.Current.CancellationToken);

        Assert.Equal(4321, proposal.TokensUsed);
    }

    [Fact]
    public async Task A_model_that_never_responds_ends_the_attempt_without_failing_the_run()
    {
        var client = new FakeChatClient("""{"edits":[]}""") { Delay = TimeSpan.FromSeconds(30) };
        var options = new AgentsOptions { AttemptTimeout = TimeSpan.FromMilliseconds(50) };

        var proposal = await Agent(client, options).ProposeAsync(Attempt(), TestContext.Current.CancellationToken);

        Assert.False(proposal.HasEdits);
        Assert.Contains("did not respond", proposal.Summary);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var client = new FakeChatClient("""{"edits":[]}""") { Delay = TimeSpan.FromSeconds(30) };
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Agent(client).ProposeAsync(Attempt(), cancellation.Token));
    }

    // ---- Fakes ------------------------------------------------------------------------

    private sealed class FakeSource(IReadOnlyDictionary<string, string> files) : ISourceReader
    {
        public Task<string?> ReadAsync(string path, CancellationToken ct = default)
            => Task.FromResult(files.TryGetValue(path, out var content) ? content : null);
    }

    private sealed class FakeChatClient(string reply) : IChatClient
    {
        public int Calls { get; private set; }
        public string? LastSystemPrompt { get; private set; }
        public string? LastUserPrompt { get; private set; }
        public long TotalTokens { get; set; }
        public TimeSpan Delay { get; set; }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var list = messages.ToList();
            LastSystemPrompt = list.FirstOrDefault(m => m.Role == ChatRole.System)?.Text;
            LastUserPrompt = list.FirstOrDefault(m => m.Role == ChatRole.User)?.Text;

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))
            {
                Usage = new UsageDetails { TotalTokenCount = TotalTokens },
            };
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("the agent does not stream");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
