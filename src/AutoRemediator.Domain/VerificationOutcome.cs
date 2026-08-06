using AutoRemediator.Shared;

namespace AutoRemediator.Domain;

/// <summary>How local verification (restore → build) judged a computed change.</summary>
public enum VerificationClassification
{
    /// <summary>Restore and build both succeeded.</summary>
    Verified,

    /// <summary>Restore or build was rejected by identified dependency/compilation diagnostics.</summary>
    DependencyFailure,

    /// <summary>Verification could not run, or its result cannot be trusted.</summary>
    Skipped,
}

/// <summary>
/// A single diagnostic parsed from restore or build output. <see cref="Path"/> is relative to the
/// repository root so it stays meaningful outside the temporary verification directory.
/// </summary>
public sealed record VerificationDiagnostic(
    string Code,
    string Message,
    string? Path = null,
    int? Line = null,
    int? Column = null);

/// <summary>
/// The result of a local verification attempt. <see cref="Diagnostics"/> is deliberately bounded —
/// a compile cascade is highly repetitive and the run record must stay within Table Storage limits;
/// the complete output always lives at <see cref="LogReference"/> in blob storage.
/// </summary>
public sealed record VerificationOutcome(
    VerificationClassification Classification,
    string? SkipReason = null,
    IReadOnlyList<VerificationDiagnostic>? Diagnostics = null,
    string? LogReference = null)
{
    /// <summary>How many diagnostics are retained on the run record.</summary>
    public const int MaxDiagnostics = 25;

    public IReadOnlyList<VerificationDiagnostic> Diagnostics { get; init; } = Diagnostics ?? [];

    public bool IsVerified => Classification == VerificationClassification.Verified;

    /// <summary>True when verification positively rejected the change, so nothing may be pushed.</summary>
    public bool Rejected => Classification == VerificationClassification.DependencyFailure;

    /// <summary>True when the change may proceed to a pull request (verified, or skipped and degraded).</summary>
    public bool MayPush => !Rejected;

    public static VerificationOutcome Verified(string? logReference = null)
        => new(VerificationClassification.Verified, LogReference: logReference);

    public static VerificationOutcome DependencyFailure(
        IReadOnlyList<VerificationDiagnostic> diagnostics, string? logReference = null)
        => new(VerificationClassification.DependencyFailure, Diagnostics: Bound(diagnostics), LogReference: logReference);

    public static VerificationOutcome Skipped(string reason, string? logReference = null)
        => new(VerificationClassification.Skipped, Guard.AgainstNullOrWhiteSpace(reason), LogReference: logReference);

    /// <summary>Keeps the first <see cref="MaxDiagnostics"/> distinct diagnostics.</summary>
    private static IReadOnlyList<VerificationDiagnostic> Bound(IReadOnlyList<VerificationDiagnostic> diagnostics)
        => diagnostics.Distinct().Take(MaxDiagnostics).ToList();
}
