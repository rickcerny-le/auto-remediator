using AutoRemediator.Api.Features;
using AutoRemediator.Contracts.Dtos;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
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
    }

    private static RunSummaryDto ToSummary(RemediationRun r) =>
        new(r.Id, r.RepositorySlug, r.Status.ToString(), r.StartedAtUtc, r.FinishedAtUtc, r.Updates.Count, r.PullRequestUrl);

    private static RunDetailDto ToDetail(RemediationRun r) =>
        new(r.Id, r.RepositorySlug, r.Status.ToString(), r.StartedAtUtc, r.FinishedAtUtc, r.PullRequestUrl, r.Error,
            r.Updates.Select(u => new RunUpdateDto(u.PackageId, u.FromVersion, u.ToVersion, u.Kind.ToString(), u.BeyondPolicy)).ToList());
}
