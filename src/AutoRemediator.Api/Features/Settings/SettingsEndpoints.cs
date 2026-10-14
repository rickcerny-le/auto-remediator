using AutoRemediator.Api.Features;
using AutoRemediator.Api.Features.Repositories;
using AutoRemediator.Contracts.Dtos;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoRemediator.Api.Features.Settings;

/// <summary>Get/update the global targeting settings (patterns, excludes, feeds, policy).</summary>
public sealed class SettingsEndpoints : IFeatureEndpoint
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/settings").WithTags("Settings");

        group.MapGet("/", async (ITargetingSettingsStore store, CancellationToken ct) =>
            Results.Ok(ToDto(await store.GetAsync(ct))));

        group.MapPut("/", async (TargetingSettingsDto dto, ITargetingSettingsStore store, CancellationToken ct) =>
        {
            if (SettingsValidation.Validate(dto) is { Count: > 0 } errors)
            {
                return Results.ValidationProblem(errors);
            }

            var settings = FromDto(dto);
            await store.SetAsync(settings, ct);
            return Results.Ok(ToDto(settings));
        });
    }

    private static TargetingSettingsDto ToDto(TargetingSettings s) =>
        new(s.Patterns, s.Excludes, s.Feeds, s.Policy.Strategy.ToString(), s.Policy.Ignore, s.Policy.AllowPrerelease, s.DefaultSchedule);

    private static TargetingSettings FromDto(TargetingSettingsDto d)
    {
        // Validated by SettingsValidation before this runs.
        SettingsValidation.TryParseStrategy(d.Strategy, out var strategy);
        return new TargetingSettings(
            d.Patterns, d.Excludes, d.Feeds, new UpdatePolicy(strategy, d.Ignore, d.AllowPrerelease), d.DefaultSchedule);
    }
}
