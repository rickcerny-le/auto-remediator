using Microsoft.AspNetCore.Routing;

namespace AutoRemediator.Api.Features;

/// <summary>
/// Marker for a vertical-slice endpoint. Each feature implements this to map its
/// own routes; endpoints resolve their handlers directly from DI (no mediator).
/// </summary>
public interface IFeatureEndpoint
{
    static abstract void Map(IEndpointRouteBuilder routes);
}
