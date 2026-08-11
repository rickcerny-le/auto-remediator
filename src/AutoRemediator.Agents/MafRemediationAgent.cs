using System.Text;
using System.Text.Json;
using AutoRemediator.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Agents;

/// <summary>
/// Repairs compile and API breaks by asking a chat model to rewrite the affected files.
///
/// The chat client is injected from an Aspire connection reference, so the same agent runs against a
/// local model in development and the deployed Foundry account in Azure. When no model is
/// configured the client is absent and the agent reports itself unavailable rather than failing —
/// the model is a soft dependency.
/// </summary>
internal sealed class MafRemediationAgent(
    IOptions<AgentsOptions> options,
    ILogger<MafRemediationAgent> logger,
    IChatClient? chatClient = null) : IRemediationAgent
{
    private readonly AgentsOptions _options = options.Value;

    /// <summary>How much of a file to show the model. Whole files keep edits coherent; very large ones are skipped.</summary>
    private const int MaxFileCharacters = 60_000;

    public bool IsAvailable => chatClient is not null;

    public async Task<RemediationProposal> ProposeAsync(
        RemediationAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        if (chatClient is null)
        {
            return RemediationProposal.None("no model is configured, so no repair was attempted");
        }

        var sources = await ReadAffectedFilesAsync(attempt, cancellationToken);
        if (sources.Count == 0)
        {
            // Diagnostics with no readable file give the model nothing to work from.
            return RemediationProposal.None("none of the diagnostics referred to a readable source file");
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, BuildUserPrompt(attempt, sources)),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.AttemptTimeout);

        ChatResponse response;
        try
        {
            response = await chatClient.GetResponseAsync(messages, cancellationToken: timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Attempt {Attempt}: the model did not respond within {Timeout}.", attempt.AttemptNumber, _options.AttemptTimeout);
            return RemediationProposal.None($"the model did not respond within {_options.AttemptTimeout}");
        }

        var tokens = (int)(response.Usage?.TotalTokenCount ?? 0);
        var edits = ParseEdits(response.Text, sources.Keys, logger);

        logger.LogInformation(
            "Attempt {Attempt}: the model proposed {Count} edit(s) using {Tokens} token(s).",
            attempt.AttemptNumber, edits.Count, tokens);

        return new RemediationProposal(
            edits,
            tokens,
            edits.Count == 0 ? "the model proposed no usable edits" : $"proposed {edits.Count} edit(s)");
    }

    private const string SystemPrompt = """
        You repair C# source that stopped compiling after a NuGet package was upgraded. The package
        versions are already decided and are not yours to change.

        Rules:
        - Fix only what the diagnostics report. Do not refactor, reformat, or improve anything else.
        - Adapt call sites to the new API surface. Never work around a break by deleting behaviour.
        - Never edit project files, Directory.Packages.props, or lock files. Those changes are rejected.
        - If you cannot fix a diagnostic, omit that file rather than guessing.

        Reply with JSON only, in exactly this shape, with the complete new content of each file:

        {"edits":[{"path":"src/Foo/Bar.cs","content":"<entire file>"}]}

        Reply with {"edits":[]} if you cannot help.
        """;

    /// <summary>Reads every distinct file a diagnostic points at, skipping unreadable or oversized ones.</summary>
    private async Task<Dictionary<string, string>> ReadAffectedFilesAsync(
        RemediationAttempt attempt,
        CancellationToken cancellationToken)
    {
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in attempt.Diagnostics.Select(d => d.Path).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
        {
            if (sources.ContainsKey(path!))
            {
                continue;
            }

            var content = await attempt.Source.ReadAsync(path!, cancellationToken);
            if (content is null)
            {
                logger.LogDebug("Attempt {Attempt}: {Path} could not be read.", attempt.AttemptNumber, path);
                continue;
            }

            if (content.Length > MaxFileCharacters)
            {
                logger.LogDebug("Attempt {Attempt}: {Path} is too large to include.", attempt.AttemptNumber, path);
                continue;
            }

            sources[path!] = content;
        }

        return sources;
    }

    private static string BuildUserPrompt(RemediationAttempt attempt, Dictionary<string, string> sources)
    {
        var prompt = new StringBuilder();

        if (attempt.AttemptNumber > 1)
        {
            prompt.AppendLine(
                $"Attempt {attempt.AttemptNumber}. Previous attempts did not fix the build; the diagnostics below are current.");
            prompt.AppendLine();
        }

        prompt.AppendLine("Compiler diagnostics:");
        foreach (var d in attempt.Diagnostics)
        {
            var where = d.Path is null
                ? string.Empty
                : $" at {d.Path}{(d.Line is null ? string.Empty : $":{d.Line}")}{(d.Column is null ? string.Empty : $":{d.Column}")}";
            prompt.AppendLine($"- {d.Code}{where}: {d.Message}");
        }

        prompt.AppendLine();
        prompt.AppendLine("Files:");
        foreach (var (path, content) in sources)
        {
            prompt.AppendLine($"--- {path}");
            prompt.AppendLine(content);
            prompt.AppendLine($"--- end {path}");
        }

        return prompt.ToString();
    }

    /// <summary>
    /// Parses the model's JSON reply. A malformed reply yields no edits rather than an exception —
    /// a bad response is an unproductive attempt, not a run failure. Edits for files that were not
    /// offered are dropped here; the caller enforces the full safety boundary regardless.
    /// </summary>
    private static IReadOnlyList<ProposedEdit> ParseEdits(
        string? text,
        IEnumerable<string> offeredPaths,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var json = ExtractJsonObject(text);
        if (json is null)
        {
            logger.LogWarning("The model's reply contained no JSON object.");
            return [];
        }

        var offered = new HashSet<string>(offeredPaths, StringComparer.OrdinalIgnoreCase);
        var edits = new List<ProposedEdit>();

        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("edits", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning("The model's reply had no 'edits' array.");
                return [];
            }

            foreach (var element in array.EnumerateArray())
            {
                var path = element.TryGetProperty("path", out var p) ? p.GetString() : null;
                var content = element.TryGetProperty("content", out var c) ? c.GetString() : null;

                if (string.IsNullOrWhiteSpace(path) || content is null)
                {
                    continue;
                }

                var normalized = path.Replace('\\', '/').TrimStart('/');

                if (!offered.Contains(normalized))
                {
                    logger.LogWarning("Dropping a proposed edit to {Path}, which was not offered to the model.", normalized);
                    continue;
                }

                edits.Add(new ProposedEdit(normalized, content));
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "The model's reply was not valid JSON.");
            return [];
        }

        return edits;
    }

    /// <summary>Finds the outermost JSON object, tolerating prose or code fences around it.</summary>
    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }
}
