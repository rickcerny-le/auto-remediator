using AutoRemediator.Domain;

namespace AutoRemediator.Domain.Tests;

public class RemediationRunTests
{
    private static readonly DateTimeOffset Started = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Finished = Started.AddMinutes(3);

    private static RemediationRun NewRun() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "contoso/platform/web-api", Started);

    private static DependencyUpdate[] Updates() =>
        [new DependencyUpdate("Orion180.Core", "1.4.0", "1.5.0")];

    [Fact]
    public void New_run_starts_in_Reading_without_a_verification_outcome()
    {
        var run = NewRun();

        Assert.Equal(RunStatus.Reading, run.Status);
        Assert.Null(run.Verification);
    }

    [Fact]
    public void Verified_records_the_outcome_without_ending_the_run()
    {
        var run = NewRun();
        run.Advance(RunStatus.Verifying);

        run.Verified(VerificationOutcome.Verified("runs/abc/verify.log"));

        Assert.Equal(VerificationClassification.Verified, run.Verification?.Classification);
        Assert.Equal(RunStatus.Verifying, run.Status);
        Assert.Null(run.FinishedAtUtc);
    }

    [Fact]
    public void VerificationFailed_ends_the_run_with_no_pull_request()
    {
        var run = NewRun();
        var outcome = VerificationOutcome.DependencyFailure(
            [new VerificationDiagnostic("CS0117", "'Client' has no member 'SubmitAsync'", "src/Foo/Bar.cs", 42, 17)]);

        run.VerificationFailed(Updates(), outcome, Finished);

        Assert.Equal(RunStatus.VerificationFailed, run.Status);
        Assert.Equal(Finished, run.FinishedAtUtc);
        Assert.Null(run.PullRequestUrl);
        Assert.Single(run.Updates);
        Assert.Equal("CS0117", run.Verification?.Diagnostics.Single().Code);
    }

    [Fact]
    public void VerificationFailed_is_distinct_from_Failed()
    {
        var rejected = NewRun();
        rejected.VerificationFailed(Updates(), VerificationOutcome.DependencyFailure([]), Finished);

        var crashed = NewRun();
        crashed.Failed("the feed exploded", Finished);

        Assert.NotEqual(crashed.Status, rejected.Status);
        Assert.Equal(RunStatus.VerificationFailed, rejected.Status);
        Assert.Equal(RunStatus.Failed, crashed.Status);
        Assert.Null(rejected.Error);
        Assert.Equal("the feed exploded", crashed.Error);
    }

    [Fact]
    public void Completed_after_skipped_verification_still_records_the_pull_request()
    {
        var run = NewRun();
        run.Verified(VerificationOutcome.Skipped("the feed was unreachable"));
        run.Completed(Updates(), "https://dev.azure.com/pr/1", Finished);

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("https://dev.azure.com/pr/1", run.PullRequestUrl);
        Assert.Equal(VerificationClassification.Skipped, run.Verification?.Classification);
        Assert.Equal("the feed was unreachable", run.Verification?.SkipReason);
    }

    [Fact]
    public void Restore_round_trips_the_verification_outcome()
    {
        var outcome = VerificationOutcome.DependencyFailure(
            [new VerificationDiagnostic("NU1107", "Version conflict detected")], "runs/abc/restore.log");

        var run = RemediationRun.Restore(
            Guid.NewGuid(), Guid.NewGuid(), "contoso/platform/web-api", RunStatus.VerificationFailed,
            Started, Finished, Updates(), pullRequestUrl: null, error: null, verification: outcome);

        Assert.Equal(VerificationClassification.DependencyFailure, run.Verification?.Classification);
        Assert.Equal("runs/abc/restore.log", run.Verification?.LogReference);
        Assert.Equal("NU1107", run.Verification?.Diagnostics.Single().Code);
    }

    [Fact]
    public void Restore_without_a_verification_outcome_leaves_it_null()
    {
        var run = RemediationRun.Restore(
            Guid.NewGuid(), Guid.NewGuid(), "contoso/platform/web-api", RunStatus.NoUpdates,
            Started, Finished, [], pullRequestUrl: null, error: null);

        Assert.Null(run.Verification);
    }

    [Fact]
    public void Rejected_outcome_blocks_pushing_and_others_allow_it()
    {
        Assert.True(VerificationOutcome.DependencyFailure([]).Rejected);
        Assert.False(VerificationOutcome.DependencyFailure([]).MayPush);

        Assert.True(VerificationOutcome.Verified().MayPush);
        Assert.True(VerificationOutcome.Skipped("no SDK").MayPush);
        Assert.False(VerificationOutcome.Skipped("no SDK").IsVerified);
    }

    [Fact]
    public void Diagnostics_are_bounded_and_deduplicated_on_the_run_record()
    {
        var noisy = Enumerable.Range(0, VerificationOutcome.MaxDiagnostics * 4)
            .Select(i => new VerificationDiagnostic("CS0117", $"missing member {i}", "src/Foo.cs", i, 1))
            .Concat(Enumerable.Repeat(new VerificationDiagnostic("CS0246", "type not found"), 5))
            .ToList();

        var outcome = VerificationOutcome.DependencyFailure(noisy);

        Assert.Equal(VerificationOutcome.MaxDiagnostics, outcome.Diagnostics.Count);
        Assert.Equal(outcome.Diagnostics.Count, outcome.Diagnostics.Distinct().Count());
    }

    [Fact]
    public void Skipped_requires_a_reason()
    {
        Assert.Throws<ArgumentException>(() => VerificationOutcome.Skipped("  "));
    }

    // ---- AI remediation loop --------------------------------------------------------

    [Fact]
    public void A_run_that_never_remediated_has_no_attempts()
    {
        var run = NewRun();
        run.Verified(VerificationOutcome.Verified());

        Assert.Null(run.RemediationAttempts);
        Assert.Null(run.RemediationTranscriptReference);
    }

    [Fact]
    public void Remediated_records_attempts_without_ending_the_run()
    {
        var run = NewRun();
        run.Advance(RunStatus.Remediating);

        run.Remediated(attempts: 2, transcriptReference: "runs/abc/transcript.log");

        Assert.Equal(2, run.RemediationAttempts);
        Assert.Equal("runs/abc/transcript.log", run.RemediationTranscriptReference);
        Assert.Equal(RunStatus.Remediating, run.Status);
        Assert.Null(run.FinishedAtUtc);
    }

    [Fact]
    public void Zero_attempts_is_distinct_from_never_having_remediated()
    {
        // The loop was entered but produced no attempt — e.g. the budget was already spent.
        var entered = NewRun();
        entered.Remediated(attempts: 0, transcriptReference: null);

        var neverEntered = NewRun();

        Assert.Equal(0, entered.RemediationAttempts);
        Assert.Null(neverEntered.RemediationAttempts);
    }

    [Fact]
    public void A_repaired_run_completes_with_its_attempt_count()
    {
        var run = NewRun();
        run.Verified(VerificationOutcome.Verified("runs/abc/verify.log"));
        run.Remediated(attempts: 2, transcriptReference: "runs/abc/transcript.log");
        run.Completed(Updates(), "https://dev.azure.com/pr/1", Finished);

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(2, run.RemediationAttempts);
        Assert.Equal("https://dev.azure.com/pr/1", run.PullRequestUrl);
    }

    [Fact]
    public void An_exhausted_loop_still_lands_in_VerificationFailed()
    {
        var run = NewRun();
        var outcome = VerificationOutcome.DependencyFailure(
            [new VerificationDiagnostic("CS1061", "no member 'SubmitAsync'", "src/Foo.cs", 6, 16)]);

        run.Remediated(attempts: 3, transcriptReference: "runs/abc/transcript.log");
        run.VerificationFailed(Updates(), outcome, Finished);

        // The floor never drops below what the same rejection produces with no agent at all.
        Assert.Equal(RunStatus.VerificationFailed, run.Status);
        Assert.Null(run.PullRequestUrl);
        Assert.Null(run.Error);
        Assert.Equal(3, run.RemediationAttempts);
        Assert.Equal("CS1061", run.Verification?.Diagnostics.Single().Code);
    }

    [Fact]
    public void Negative_attempts_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewRun().Remediated(-1, null));
    }

    [Fact]
    public void Restore_round_trips_the_remediation_attempts()
    {
        var run = RemediationRun.Restore(
            Guid.NewGuid(), Guid.NewGuid(), "contoso/platform/web-api", RunStatus.Completed,
            Started, Finished, Updates(), "https://dev.azure.com/pr/1", error: null,
            verification: VerificationOutcome.Verified(),
            remediationAttempts: 2,
            remediationTranscriptReference: "runs/abc/transcript.log");

        Assert.Equal(2, run.RemediationAttempts);
        Assert.Equal("runs/abc/transcript.log", run.RemediationTranscriptReference);
    }
}
