using System.Text.Json.Serialization;

namespace AutoRemediator.Contracts.Messages;

/// <summary>The four dispositions a person can give a held proposal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReviewCommand>))]
public enum ReviewCommand
{
    /// <summary>Push the stored change set and open or refresh the pull request.</summary>
    Approve,

    /// <summary>Materialize at the current branch head, replay every edit, re-verify, and replace the proposal.</summary>
    Rebuild,

    /// <summary>Materialize at the stored base commit, replay only the manifest edits, and run the loop again.</summary>
    Retry,

    /// <summary>Reject the proposal. Terminal; nothing is pushed.</summary>
    Discard,
}

/// <summary>
/// Message published by the review API and consumed by <c>ReviewCommandWorker</c>. Carries no
/// payload beyond the run id and the command — everything the handler needs is loaded from
/// storage by run id, so a stale message cannot act on a stale change set (see contracts/review-messages.md).
/// </summary>
/// <param name="RunId">The run whose proposal the command acts on.</param>
/// <param name="Command">Which of the four dispositions to execute.</param>
/// <param name="RequestedAtUtc">When the command was requested.</param>
public sealed record ReviewCommandRequested(
    Guid RunId,
    ReviewCommand Command,
    DateTimeOffset RequestedAtUtc);
