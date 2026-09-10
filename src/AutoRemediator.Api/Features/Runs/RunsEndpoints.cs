using AutoRemediator.Api.Features;
using AutoRemediator.Contracts;
using AutoRemediator.Contracts.Dtos;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Messaging;
using AutoRemediator.Infrastructure.Review;
using AutoRemediator.Infrastructure.Verification;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoRemediator.Api.Features.Runs;

/// <summary>Read-only remediation run history: global list (with status filter) and detail.</summary>
public sealed class RunsEndpoints : IFeatureEndpoint
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/runs").WithTags("Runs");

        group.MapGet("/", async (string? status, IRemediationRunStore store, CancellationToken ct) =>
        {
            var runs = (await store.ListAllAsync(ct)).OrderByDescending(r => r.StartedAtUtc).AsEnumerable();

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<RunStatus>(status, ignoreCase: true, out var parsed))
            {
                runs = runs.Where(r => r.Status == parsed);
            }

            return Results.Ok(runs.Select(ToSummary));
        });

        group.MapGet("/{id:guid}", async (Guid id, IRemediationRunStore store, CancellationToken ct) =>
        {
            var run = await store.GetAsync(id, ct);
            return run is null ? Results.NotFound() : Results.Ok(ToDetail(run));
        });

        group.MapGet("/{id:guid}/verification-log", async (
            Guid id, IRemediationRunStore store, IVerificationLogStore logs, CancellationToken ct) =>
        {
            var run = await store.GetAsync(id, ct);
            return await ArtifactAsync(run?.Verification?.LogReference, logs, ct);
        });

        group.MapGet("/{id:guid}/remediation-transcript", async (
            Guid id, IRemediationRunStore store, IVerificationLogStore logs, CancellationToken ct) =>
        {
            var run = await store.GetAsync(id, ct);
            return await ArtifactAsync(run?.RemediationTranscriptReference, logs, ct);
        });

        group.MapGet("/{id:guid}/proposal", async (
            Guid id, IRemediationRunStore store, IChangeProposalStore proposals, CancellationToken ct) =>
        {
            var run = await store.GetAsync(id, ct);
            if (run?.ProposalReference is not { } reference)
            {
                return Results.NotFound();
            }

            var proposal = await proposals.ReadAsync(reference, ct);
            return proposal is null ? Results.NotFound() : Results.Ok(ToProposal(run, proposal));
        });

        group.MapPost("/{id:guid}/review/{command}", async (
            Guid id, string command, IRemediationRunStore store, IMessagePublisher publisher, TimeProvider timeProvider, CancellationToken ct) =>
        {
            if (!Enum.TryParse<ReviewCommand>(command, ignoreCase: true, out var reviewCommand))
            {
                return Results.BadRequest();
            }

            var run = await store.GetAsync(id, ct);
            if (run is null)
            {
                return Results.NotFound();
            }

            if (run.Status != RunStatus.AwaitingReview)
            {
                // Advisory, not authoritative — the worker re-checks when it dequeues (Decision 7).
                return Results.Conflict(new { status = run.Status.ToString(), message = $"The run is {run.Status}, not AwaitingReview." });
            }

            await publisher.PublishAsync(
                RemediationQueues.ReviewCommands,
                new ReviewCommandRequested(id, reviewCommand, timeProvider.GetUtcNow()),
                ct);

            return Results.Accepted(value: new { runId = id, command = command.ToLowerInvariant(), accepted = true });
        });
    }

    /// <summary>Serves a stored run artifact as plain text, or not-found when there is none.</summary>
    private static async Task<IResult> ArtifactAsync(string? reference, IVerificationLogStore logs, CancellationToken ct)
    {
        if (reference is null)
        {
            return Results.NotFound();
        }

        var content = await logs.ReadAsync(reference, ct);
        return content is null ? Results.NotFound() : Results.Text(content, "text/plain");
    }

    private static RunSummaryDto ToSummary(RemediationRun r) =>
        new(r.Id, r.RepositorySlug, r.Status.ToString(), r.StartedAtUtc, r.FinishedAtUtc, r.Updates.Count, r.PullRequestUrl);

    private static RunDetailDto ToDetail(RemediationRun r) =>
        new(r.Id, r.RepositorySlug, r.Status.ToString(), r.StartedAtUtc, r.FinishedAtUtc, r.PullRequestUrl, r.Error,
            r.Updates.Select(u => new RunUpdateDto(u.PackageId, u.FromVersion, u.ToVersion, u.Kind.ToString(), u.BeyondPolicy)).ToList(),
            ToVerification(r),
            ToRemediation(r),
            r.ProposalReference is null ? null : $"/api/runs/{r.Id}/proposal",
            r.ReviewNote);

    private static ChangeProposalDto ToProposal(RemediationRun r, ChangeProposal p) =>
        new(
            p.RunId,
            r.RepositorySlug,
            p.BaseCommitId,
            p.VerifiedAtUtc,
            p.Attempts,
            p.TranscriptReference is null ? null : $"/api/runs/{r.Id}/remediation-transcript",
            r.ReviewNote,
            r.ReviewCommandCount,
            p.Files.Select(f => new ProposedFileDto(f.Path, f.OriginalContent, f.NewContent, f.Origin.ToString())).ToList(),
            p.ProvokingDiagnostics.Select(d => new RunDiagnosticDto(d.Code, d.Message, d.Path, d.Line, d.Column)).ToList());

    private static RunRemediationDto? ToRemediation(RemediationRun r)
        => r.RemediationAttempts is not { } attempts
            ? null
            : new RunRemediationDto(
                attempts,
                r.RemediationTranscriptReference is null ? null : $"/api/runs/{r.Id}/remediation-transcript");

    private static RunVerificationDto? ToVerification(RemediationRun r)
    {
        if (r.Verification is not { } v)
        {
            return null;
        }

        return new RunVerificationDto(
            v.Classification.ToString(),
            v.SkipReason,
            v.Diagnostics.Select(d => new RunDiagnosticDto(d.Code, d.Message, d.Path, d.Line, d.Column)).ToList(),
            v.LogReference is null ? null : $"/api/runs/{r.Id}/verification-log");
    }
}
