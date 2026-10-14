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

        group.MapGet("/{id:guid}", async (Guid id, IManagedRepositoryStore store, CancellationToken ct) =>
            await store.GetAsync(id, ct) is { } repo ? Results.Ok(ToDto(repo)) : Results.NotFound());

        group.MapPost("/", async (ManagedRepositoryInput input, IManagedRepositoryStore store, CancellationToken ct) =>
        {
            if (input.Settings is not null && SettingsValidation.Validate(input.Settings) is { Count: > 0 } errors)
            {
                return Results.ValidationProblem(errors);
            }

            var settings = input.Settings is null ? RepositorySettings.Inherit : FromDto(input.Settings);
            var repo = new ManagedRepository(Guid.NewGuid(), input.Organization, input.Project, input.Name, input.Enabled, input.TargetBranch, settings);
            await store.UpsertAsync(repo, ct);
            return Results.Created($"/api/repositories/{repo.Id}", ToDto(repo));
        });

        group.MapPut("/{id:guid}", async (Guid id, ManagedRepositoryInput input, IManagedRepositoryStore store, CancellationToken ct) =>
        {
            var existing = await store.GetAsync(id, ct);
            if (existing is null)
            {
                return Results.NotFound();
            }

            // Null Settings keeps what is stored, so older callers do not wipe per-repository settings.
            if (input.Settings is not null && SettingsValidation.Validate(input.Settings) is { Count: > 0 } errors)
            {
                return Results.ValidationProblem(errors);
            }

            var settings = input.Settings is null ? existing.Settings : FromDto(input.Settings);
            var updated = new ManagedRepository(id, input.Organization, input.Project, input.Name, input.Enabled, input.TargetBranch, settings);
            await store.UpsertAsync(updated, ct);
            return Results.Ok(ToDto(updated));
        });

        group.MapGet("/{id:guid}/settings", async (Guid id, IManagedRepositoryStore store, CancellationToken ct) =>
            await store.GetAsync(id, ct) is { } repo ? Results.Ok(ToDto(repo.Settings)) : Results.NotFound());

        group.MapPut("/{id:guid}/settings", async (Guid id, RepositorySettingsDto dto, IManagedRepositoryStore store, CancellationToken ct) =>
        {
            var existing = await store.GetAsync(id, ct);
            if (existing is null)
            {
                return Results.NotFound();
            }

            if (SettingsValidation.Validate(dto) is { Count: > 0 } errors)
            {
                return Results.ValidationProblem(errors);
            }

            existing.SetSettings(FromDto(dto));
            await store.UpsertAsync(existing, ct);
            return Results.Ok(ToDto(existing.Settings));
        });

        group.MapGet("/{id:guid}/effective-settings", async (
            Guid id, IManagedRepositoryStore store, IEffectiveSettingsProvider provider, CancellationToken ct) =>
        {
            var repo = await store.GetAsync(id, ct);
            return repo is null ? Results.NotFound() : Results.Ok(ToEffectiveDto(repo.Id, await provider.GetAsync(repo, ct)));
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
        new(r.Id, r.Organization, r.Project, r.Name, r.Enabled, r.TargetBranch, ToDto(r.Settings));

    private static RepositorySettingsDto ToDto(RepositorySettings s) =>
        new(s.InheritGlobal, s.Patterns, s.Excludes, s.Feeds, s.Ignore, s.Strategy?.ToString(), s.AllowPrerelease, s.Schedule);

    private static RepositorySettings FromDto(RepositorySettingsDto d) =>
        new(
            d.InheritGlobal,
            d.Patterns,
            d.Excludes,
            d.Feeds,
            d.Ignore,
            SettingsValidation.TryParseStrategy(d.Strategy, out var strategy) ? strategy : null,
            d.AllowPrerelease,
            d.Schedule);

    private static EffectiveSettingsDto ToEffectiveDto(Guid repositoryId, EffectiveSettings e) =>
        new(
            repositoryId,
            e.InheritGlobal,
            Items(e.Patterns),
            Items(e.Excludes),
            Items(e.Feeds),
            Items(e.Ignore),
            new EffectiveValueDto(e.Strategy.Value.ToString(), e.Strategy.Source.ToString()),
            new EffectiveValueDto(e.AllowPrerelease.Value ? "true" : "false", e.AllowPrerelease.Source.ToString()),
            new EffectiveValueDto(e.Schedule.Value, e.Schedule.Source.ToString()),
            e.Warnings);

    private static EffectiveListItemDto[] Items(IReadOnlyList<EffectiveItem> items) =>
        items.Select(i => new EffectiveListItemDto(i.Value, i.Source.ToString())).ToArray();
}
