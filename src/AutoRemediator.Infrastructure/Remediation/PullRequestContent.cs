using System.Text;
using AutoRemediator.Domain;

namespace AutoRemediator.Infrastructure.Remediation;

/// <summary>
/// Builds the commit message, title and description for a dependency-update pull request. Shared
/// by <see cref="RemediationRunner"/> (the mechanical path) and the review command handler's
/// approve path, so the two produce byte-identical descriptions for the parts they have in common
/// (FR-023).
/// </summary>
internal static class PullRequestContent
{
    public static string CommitMessage(int updateCount) => $"Update {updateCount} package(s) [AutoRemediator]";

    public static string Title(int updateCount) => $"Automated dependency updates ({updateCount} package(s))";

    /// <param name="updates">The packages changed by this run.</param>
    /// <param name="verification">The verification outcome. Ignored when <paramref name="provenance"/> is supplied.</param>
    /// <param name="remediationAttempts">How many AI repair attempts contributed, or null/zero when none did.</param>
    /// <param name="provenance">
    /// When the change came from an approved proposal, the moment it was verified and the commit it
    /// was verified against — stated as a fact about the past rather than implied to be current
    /// (FR-023, Decision 8). Null for the mechanical path, whose description is unchanged by this
    /// feature.
    /// </param>
    public static string Description(
        IReadOnlyList<DependencyUpdate> updates,
        VerificationOutcome verification,
        int? remediationAttempts,
        (DateTimeOffset VerifiedAtUtc, string BaseCommitId)? provenance = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Automated dependency update by **AutoRemediator**.");
        sb.AppendLine();
        sb.AppendLine("| Package | From | To | Kind |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var u in updates)
        {
            var kind = u.Kind == UpdateKind.Collateral ? "collateral" : "matched";
            if (u.BeyondPolicy)
            {
                kind += " ⚠ beyond policy";
            }

            sb.AppendLine($"| {u.PackageId} | {u.FromVersion} | {u.ToVersion} | {kind} |");
        }

        if (updates.Any(u => u.BeyondPolicy))
        {
            sb.AppendLine();
            sb.AppendLine("> ⚠ Some collateral bumps were escalated **beyond the update policy** to keep the dependency set consistent.");
        }

        sb.AppendLine();

        // A reviewer must never be left to assume a change was verified when it was not.
        if (provenance is { } p)
        {
            sb.AppendLine(
                $"✅ **Verified** — `dotnet restore` and `dotnet build` both succeeded against this change "
                + $"on {p.VerifiedAtUtc:yyyy-MM-dd} at commit `{ShortCommit(p.BaseCommitId)}`.");
        }
        else if (verification.IsVerified)
        {
            sb.AppendLine("✅ **Verified locally** — `dotnet restore` and `dotnet build` both succeeded against this change.");
        }
        else
        {
            sb.AppendLine($"⚠ **Not verified locally** — verification was skipped: {verification.SkipReason}");
        }

        // Disclosed rather than left to be inferred from the diff: a reviewer must know that a
        // model wrote source in here, and how many tries it took.
        if (remediationAttempts is > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"🤖 **Contains AI-authored source edits.** A compile break introduced by this update was "
                          + $"repaired by an AI agent over {remediationAttempts} attempt(s). Review the source changes "
                          + "with that in mind — the full transcript of what the agent was shown and what it proposed "
                          + "is attached to this run in AutoRemediator.");
        }

        sb.AppendLine();
        sb.AppendLine("This pull request is still validated by this repository's own CI, which additionally runs the tests.");

        return sb.ToString();
    }

    private static string ShortCommit(string commitId) => commitId.Length <= 8 ? commitId : commitId[..8];
}
