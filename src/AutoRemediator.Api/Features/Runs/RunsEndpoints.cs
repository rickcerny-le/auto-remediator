using AutoRemediator.Api.Features;
using AutoRemediator.Contracts.Dtos;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
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
            ToRemediation(r));

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
