using System.Net;
using System.Net.Http.Json;
using AutoRemediator.Contracts.Dtos;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoRemediator.Api.Tests;

public class RepositoriesEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient CreateClient(InMemoryRepositoryStore store) =>
        factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IManagedRepositoryStore>();
                services.AddSingleton<IManagedRepositoryStore>(store);
            })).CreateClient();

    [Fact]
    public async Task Get_returns_configured_repositories()
    {
        var store = new InMemoryRepositoryStore();
        store.Seed(new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api"));
        var client = CreateClient(store);

        var repos = await client.GetFromJsonAsync<List<ManagedRepositoryDto>>("/api/repositories", TestContext.Current.CancellationToken);

        Assert.NotNull(repos);
        Assert.Single(repos);
        Assert.Equal("contoso/platform/web-api", $"{repos[0].Organization}/{repos[0].Project}/{repos[0].Name}");
    }

    [Fact]
    public async Task Post_creates_a_repository()
    {
        var store = new InMemoryRepositoryStore();
        var client = CreateClient(store);

        var response = await client.PostAsJsonAsync(
            "/api/repositories",
            new ManagedRepositoryInput("contoso", "platform", "worker-jobs"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single(await store.ListAsync(TestContext.Current.CancellationToken));
    }

    private sealed class InMemoryRepositoryStore : IManagedRepositoryStore
    {
        private readonly Dictionary<Guid, ManagedRepository> _items = new();

        public void Seed(ManagedRepository r) => _items[r.Id] = r;

        public Task<IReadOnlyList<ManagedRepository>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ManagedRepository>>(_items.Values.ToList());

        public Task<ManagedRepository?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_items.GetValueOrDefault(id));

        public Task UpsertAsync(ManagedRepository r, CancellationToken ct = default)
        {
            _items[r.Id] = r;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            _items.Remove(id);
            return Task.CompletedTask;
        }
    }
}
