using AutoRemediator.Api.Features;
using AutoRemediator.Contracts.Dtos;
using AutoRemediator.Infrastructure.Analysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AutoRemediator.Api.Features.DependencyMap;

/// <summary>Returns the outdated-dependency map across configured repositories.</summary>
public sealed class DependencyMapEndpoint : IFeatureEndpoint
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/dependency-map", async (IDependencyMapService service, CancellationToken ct) =>
            {
                var map = await service.BuildAsync(ct);
                var dto = new DependencyMapDto(
                    map.Entries
                        .Select(e => new DependencyMapEntryDto(
                            e.RepositorySlug, e.PackageId, e.CurrentVersion, e.LatestVersion, e.Status.ToString()))
                        .ToList());
                return Results.Ok(dto);
            })
            .WithName("GetDependencyMap")
            .WithTags("DependencyMap");
    }
}
