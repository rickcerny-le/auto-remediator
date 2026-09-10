using AutoRemediator.Shared;

namespace AutoRemediator.Domain;

/// <summary>Lifecycle status of a remediation run.</summary>
public enum RunStatus
{
    Reading,
    Analyzing,
    Applying,
    Verifying,

    /// <summary>The AI loop is attempting to repair a compile break. Entered only when verification rejected the change with compiler diagnostics.</summary>
    Remediating,
    Pushing,
    CreatingPr,
    Completed,
    NoUpdates,

    /// <summary>
    /// Local verification positively rejected the computed change, so no pull request was opened.
    /// A result, not a crash — distinct from <see cref="Failed"/>.
    /// </summary>
    VerificationFailed,
    Failed,

    /// <summary>
    /// A verified, agent-repaired change is held for a person. Non-terminal and durable — the only
    /// status that is neither in-flight within a single worker call nor a terminal outcome.
    /// </summary>
    AwaitingReview,

    /// <summary>Terminal. A person rejected the proposal; nothing was pushed.</summary>
    Discarded,

    /// <summary>Terminal. A scheduled run was skipped because the repository already had an open proposal.</summary>
    SkippedHeld,
}

/// <summary>Why a package was bumped.</summary>
public enum UpdateKind
{
    /// <summary>A matched package bumped by policy.</summary>
    Matched,

    /// <summary>A declared sibling bumped by intra-family alignment.</summary>
    Collateral,
}

/// <summary>A single package version change applied by a run.</summary>
public sealed record DependencyUpdate(
    string PackageId,
    string FromVersion,
    string ToVersion,
    UpdateKind Kind = UpdateKind.Matched,
    bool BeyondPolicy = false);

/// <summary>
/// A remediation run for one repository: bumps matched outdated packages and opens a
/// pull request. Tracks status, the applied updates, the resulting PR, and any error.
/// </summary>
public sealed class RemediationRun : Entity<Guid>
{
    private readonly List<DependencyUpdate> _updates = [];

    public RemediationRun(Guid id, Guid repositoryId, string repositorySlug, DateTimeOffset startedAtUtc)
        : base(id)
    {
        RepositoryId = repositoryId;
        RepositorySlug = Guard.AgainstNullOrWhiteSpace(repositorySlug);
        StartedAtUtc = startedAtUtc;
        Status = RunStatus.Reading;
    }

    public Guid RepositoryId { get; }
    public string RepositorySlug { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset? FinishedAtUtc { get; private set; }
    public RunStatus Status { get; private set; }
    public IReadOnlyList<DependencyUpdate> Updates => _updates;
    public string? PullRequestUrl { get; private set; }
    public string? Error { get; private set; }

    /// <summary>The local verification result, or null when verification was not reached.</summary>
    public VerificationOutcome? Verification { get; private set; }

    /// <summary>
    /// How many AI repair attempts were made, or null when the loop never ran. Zero is
    /// meaningful and distinct from null: the loop was entered but produced no attempt, for
    /// example because the token budget was already exhausted.
    /// </summary>
    public int? RemediationAttempts { get; private set; }

    /// <summary>Blob reference for the AI transcript, or null when the loop never ran or it could not be stored.</summary>
    public string? RemediationTranscriptReference { get; private set; }

    /// <summary>Blob reference for the proposal payload. Non-null exactly while <see cref="Status"/> is <see cref="RunStatus.AwaitingReview"/>, and retained afterwards.</summary>
    public string? ProposalReference { get; private set; }

    /// <summary>Why a proposal is still waiting after a command. Distinct from <see cref="Error"/>, which means the run failed.</summary>
    public string? ReviewNote { get; private set; }

    /// <summary>How many review commands have been executed against this run.</summary>
    public int ReviewCommandCount { get; private set; }

    public void Advance(RunStatus status) => Status = status;

    /// <summary>
    /// Records which packages this run bumped, without ending the run. A held run needs this list
    /// for its pull request once approved, and a rebuild or retry needs it to reconstruct the
    /// update plan — exactly as <see cref="Completed"/> and <see cref="VerificationFailed"/> already
    /// record it for the mechanical path.
    /// </summary>
    public void RecordUpdates(IEnumerable<DependencyUpdate> updates)
    {
        _updates.Clear();
        _updates.AddRange(updates);
    }

    /// <summary>The loop contributed edits and the result verified: rest for a person instead of pushing.</summary>
    public void AwaitingReview(string proposalReference, DateTimeOffset at)
    {
        ProposalReference = Guard.AgainstNullOrWhiteSpace(proposalReference);
        Status = RunStatus.AwaitingReview;
    }

    /// <summary>Rebuild or retry succeeded: the proposal is replaced and the wait continues.</summary>
    public void ProposalReplaced(string proposalReference, DateTimeOffset at)
    {
        RequireAwaitingReview();
        ProposalReference = Guard.AgainstNullOrWhiteSpace(proposalReference);
        ReviewNote = null;
        ReviewCommandCount++;
    }

    /// <summary>
    /// A command could not complete the proposal's disposition — staleness, a failed
    /// re-verification, an unavailable model. The run stays <see cref="RunStatus.AwaitingReview"/>;
    /// this is not a failure (FR-022).
    /// </summary>
    public void ReviewBlocked(string note)
    {
        RequireAwaitingReview();
        ReviewNote = Guard.AgainstNullOrWhiteSpace(note);
        ReviewCommandCount++;
    }

    /// <summary>A person rejected the proposal. Terminal; nothing was pushed.</summary>
    public void Discarded(DateTimeOffset at)
    {
        RequireAwaitingReview();
        ReviewCommandCount++;
        Status = RunStatus.Discarded;
        FinishedAtUtc = at;
    }

    /// <summary>A scheduled run was skipped because the repository already had an open proposal.</summary>
    public void SkippedHeld(DateTimeOffset at)
    {
        Status = RunStatus.SkippedHeld;
        FinishedAtUtc = at;
    }

    private void RequireAwaitingReview()
    {
        if (Status != RunStatus.AwaitingReview)
        {
            throw new InvalidOperationException(
                $"A review command requires the run to be AwaitingReview, but it is {Status}.");
        }
    }

    /// <summary>Records the local verification result. Does not itself end the run.</summary>
    public void Verified(VerificationOutcome outcome) => Verification = Guard.AgainstNull(outcome);

    /// <summary>Records what the AI repair loop did. Does not itself end the run.</summary>
    public void Remediated(int attempts, string? transcriptReference)
    {
        RemediationAttempts = attempts >= 0
            ? attempts
            : throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "Attempts cannot be negative.");
        RemediationTranscriptReference = transcriptReference;
    }

    public void Completed(IEnumerable<DependencyUpdate> updates, string pullRequestUrl, DateTimeOffset finishedAtUtc)
    {
        _updates.Clear();
        _updates.AddRange(updates);
        PullRequestUrl = Guard.AgainstNullOrWhiteSpace(pullRequestUrl);
        Status = RunStatus.Completed;
        FinishedAtUtc = finishedAtUtc;
    }

    /// <summary>
    /// Ends the run because verification rejected the computed change. The attempted updates are
    /// recorded for diagnosis, but no pull request exists and none will be opened.
    /// </summary>
    public void VerificationFailed(
        IEnumerable<DependencyUpdate> attemptedUpdates,
        VerificationOutcome outcome,
        DateTimeOffset finishedAtUtc)
    {
        _updates.Clear();
        _updates.AddRange(attemptedUpdates);
        Verification = Guard.AgainstNull(outcome);
        Status = RunStatus.VerificationFailed;
        FinishedAtUtc = finishedAtUtc;
    }

    public void NoUpdates(DateTimeOffset finishedAtUtc)
    {
        Status = RunStatus.NoUpdates;
        FinishedAtUtc = finishedAtUtc;
    }

    public void Failed(string error, DateTimeOffset finishedAtUtc)
    {
        Error = error;
        Status = RunStatus.Failed;
        FinishedAtUtc = finishedAtUtc;
    }

    /// <summary>Rehydrates a run from persistence.</summary>
    public static RemediationRun Restore(
        Guid id,
        Guid repositoryId,
        string repositorySlug,
        RunStatus status,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? finishedAtUtc,
        IEnumerable<DependencyUpdate> updates,
        string? pullRequestUrl,
        string? error,
        VerificationOutcome? verification = null,
        int? remediationAttempts = null,
        string? remediationTranscriptReference = null,
        string? proposalReference = null,
        string? reviewNote = null,
        int reviewCommandCount = 0)
    {
        var run = new RemediationRun(id, repositoryId, repositorySlug, startedAtUtc)
        {
            Status = status,
            FinishedAtUtc = finishedAtUtc,
            PullRequestUrl = pullRequestUrl,
            Error = error,
            Verification = verification,
            RemediationAttempts = remediationAttempts,
            RemediationTranscriptReference = remediationTranscriptReference,
            ProposalReference = proposalReference,
            ReviewNote = reviewNote,
            ReviewCommandCount = reviewCommandCount,
        };
        run._updates.AddRange(updates);
        return run;
    }
}
