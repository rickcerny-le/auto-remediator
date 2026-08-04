using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AutoRemediator.Api.Features;

/// <summary>
/// Convention-based wiring for vertical slices: every <c>*Handler</c> is registered
/// in DI, and every <see cref="IFeatureEndpoint"/> maps its own routes.
/// </summary>
public static class FeatureRegistration
{
    private static readonly Assembly FeaturesAssembly = typeof(FeatureRegistration).Assembly;

    public static IServiceCollection AddFeatureHandlers(this IServiceCollection services)
    {
        var handlers = FeaturesAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("Handler", StringComparison.Ordinal));

        foreach (var handler in handlers)
        {
            services.AddScoped(handler);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapFeatureEndpoints(this IEndpointRouteBuilder routes)
    {
        var endpoints = FeaturesAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IFeatureEndpoint).IsAssignableFrom(t));

        foreach (var endpoint in endpoints)
        {
            var map = endpoint.GetMethod(nameof(IFeatureEndpoint.Map), BindingFlags.Public | BindingFlags.Static);
            map?.Invoke(null, [routes]);
        }

        return routes;
    }
}
