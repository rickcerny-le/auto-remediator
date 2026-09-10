namespace AutoRemediator.Contracts;

/// <summary>
/// Well-known messaging destination names shared by producers and consumers.
/// </summary>
public static class RemediationQueues
{
    /// <summary>Queue carrying <see cref="Messages.RemediationRunRequested"/> messages.</summary>
    public const string RemediationRuns = "remediation-runs";

    /// <summary>Queue carrying <see cref="Messages.ReviewCommandRequested"/> messages.</summary>
    public const string ReviewCommands = "review-commands";
}
