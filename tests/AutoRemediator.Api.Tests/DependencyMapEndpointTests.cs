using System.Net.Http.Json;
using AutoRemediator.Contracts.Dtos;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoRemediator.Api.Tests;

public class DependencyMapEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Get_returns_the_dependency_map_as_dto()
    {
        var map = new DependencyMap(
        [
            new DependencyMapEntry("contoso/platform/web-api", "Contoso.Core", "1.0.0", "2.0.0", DependencyStatus.Outdated),
        ]);

        var client = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDependencyMapService>();
                services.AddSingleton<IDependencyMapService>(new FakeMapService(map));
            })).CreateClient();

        var dto = await client.GetFromJsonAsync<DependencyMapDto>("/api/dependency-map", TestContext.Current.CancellationToken);

        Assert.NotNull(dto);
        var entry = Assert.Single(dto.Entries);
        Assert.Equal("Contoso.Core", entry.PackageId);
        Assert.Equal("1.0.0", entry.CurrentVersion);
        Assert.Equal("2.0.0", entry.LatestVersion);
        Assert.Equal("Outdated", entry.Status);
    }

    private sealed class FakeMapService(DependencyMap map) : IDependencyMapService
    {
        public Task<DependencyMap> BuildAsync(CancellationToken ct = default) => Task.FromResult(map);
    }
}
