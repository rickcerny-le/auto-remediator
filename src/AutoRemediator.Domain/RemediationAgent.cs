using AutoRemediator.Shared;

namespace AutoRemediator.Domain;

/// <summary>
/// A file the agent proposes to rewrite. <see cref="Path"/> is repository-relative; whether the
/// edit is permitted is decided by the caller, not the agent.
/// </summary>
public sealed record ProposedEdit(string Path, string NewContent);

/// <summary>
/// Read-only access to the working tree the agent is reasoning about. Deliberately narrow: the
/// agent needs to read the source a diagnostic points at and nothing more, and must not know how
/// the tree was materialized.
/// </summary>
public interface ISourceReader
{
    /// <summary>Returns the file's current content, or null when it is not in the tree.</summary>
    Task<string?> ReadAsync(string repositoryRelativePath, CancellationToken cancellationToken = default);
}

/// <summary>One repair attempt's input: what is broken, and how to read the code that broke.</summary>
public sealed class RemediationAttempt
{
    public RemediationAttempt(
        int attemptNumber,
        IReadOnlyList<VerificationDiagnostic> diagnostics,
        ISourceReader source)
    {
        AttemptNumber = attemptNumber > 0
            ? attemptNumber
            : throw new ArgumentOutOfRangeException(nameof(attemptNumber), attemptNumber, "Attempts are numbered from one.");
        Diagnostics = Guard.AgainstNull(diagnostics);
        Source = Guard.AgainstNull(source);
    }

    /// <summary>One-based; the agent may use it to vary its approach across attempts.</summary>
    public int AttemptNumber { get; }

    /// <summary>The diagnostics from the most recent verification, not from an earlier attempt.</summary>
    public IReadOnlyList<VerificationDiagnostic> Diagnostics { get; }

    public ISourceReader Source { get; }
}

/// <summary>
/// What one attempt produced. <see cref="TokensUsed"/> lets the caller enforce a run-level budget
/// without the agent knowing what the budget is.
/// </summary>
public sealed record RemediationProposal(
    IReadOnlyList<ProposedEdit> Edits,
    int TokensUsed = 0,
    string? Summary = null)
{
    public bool HasEdits => Edits.Count > 0;

    /// <summary>An attempt that produced nothing — no model available, no parseable edits, or nothing to do.</summary>
    public static RemediationProposal None(string? summary = null) => new([], 0, summary);
}

/// <summary>
/// Repairs compile and API breaks introduced by a dependency update by proposing source edits.
///
/// The contract lives in the domain rather than beside the agent implementation: run orchestration
/// has to call it, and the implementation carries the agent framework, so a contract owned by the
/// implementation would force project references in both directions and drag that framework into
/// the orchestration's dependency closure.
/// </summary>
public interface IRemediationAgent
{
    /// <summary>
    /// False when no model is configured. The model is a soft dependency: the caller records that
    /// remediation was unavailable and proceeds, rather than the loop pretending to have tried.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Proposes edits for the attempt's diagnostics. Returns no edits rather than throwing when it cannot help.</summary>
    Task<RemediationProposal> ProposeAsync(RemediationAttempt attempt, CancellationToken cancellationToken = default);
}
