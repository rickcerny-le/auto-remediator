using AutoRemediator.Api.Features;
using AutoRemediator.Contracts.Dtos;
using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Remediation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoRemediator.Api.Features.Repositories;

/// <summary>CRUD for managed repositories, backed directly by the configuration store.</summary>
public sealed class RepositoriesEndpoints : IFeatureEndpoint
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/repositories").WithTags("Repositories");

        group.MapGet("/", async (IManagedRepositoryStore store, CancellationToken ct) =>
            Results.Ok((await store.ListAsync(ct)).Select(ToDto)));

        group.MapPost("/", async (ManagedRepositoryInput input, IManagedRepositoryStore store, CancellationToken ct) =>
        {
            var repo = new ManagedRepository(Guid.NewGuid(), input.Organization, input.Project, input.Name, input.Enabled, input.TargetBranch);
            await store.UpsertAsync(repo, ct);
            return Results.Created($"/api/repositories/{repo.Id}", ToDto(repo));
        });

        group.MapPut("/{id:guid}", async (Guid id, ManagedRepositoryInput input, IManagedRepositoryStore store, CancellationToken ct) =>
        {
            if (await store.GetAsync(id, ct) is null)
            {
                return Results.NotFound();
            }

            var updated = new ManagedRepository(id, input.Organization, input.Project, input.Name, input.Enabled, input.TargetBranch);
            await store.UpsertAsync(updated, ct);
            return Results.Ok(ToDto(updated));
        });

        // Run now (PRD 6.12). Allowed on a disabled repository: disabled means "not scheduled", not "off".
        // Refused while the repository is held for review. Advisory, like the review endpoint's held check.
        group.MapPost("/{id:guid}/run", async (
            Guid id, IManagedRepositoryStore store, IRemediationRunRequester requester, CancellationToken ct) =>
        {
            var repo = await store.GetAsync(id, ct);
            if (repo is null)
            {
                return Results.NotFound();
            }

            return await requester.RequestAsync(repo, RemediationTrigger.Manual, ct) switch
            {
                RunRequestResult.Held held => Results.Json(
                    new
                    {
                        reason = "held",
                        heldRunId = held.HeldRunId,
                        message = "This repository has a proposal waiting for review. Resolve it before running again.",
                    },
                    statusCode: StatusCodes.Status409Conflict),
                RunRequestResult.Enqueued enqueued => Results.Accepted(
                    value: new { runId = enqueued.RunId, enabled = repo.Enabled }),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            };
        });

        group.MapDelete("/{id:guid}", async (Guid id, IManagedRepositoryStore store, CancellationToken ct) =>
        {
            await store.DeleteAsync(id, ct);
            return Results.NoContent();
        });
    }

    private static ManagedRepositoryDto ToDto(ManagedRepository r) =>
        new(r.Id, r.Organization, r.Project, r.Name, r.Enabled, r.TargetBranch);
}
