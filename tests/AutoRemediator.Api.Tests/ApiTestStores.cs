using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoRemediator.Api.Tests;

internal sealed class MemoryRepositoryStore : IManagedRepositoryStore
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

internal sealed class MemorySettingsStore(TargetingSettings? initial = null) : ITargetingSettingsStore
{
    private TargetingSettings _settings = initial ?? TargetingSettings.Empty;

    public Task<TargetingSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(_settings);

    public Task SetAsync(TargetingSettings settings, CancellationToken ct = default)
    {
        _settings = settings;
        return Task.CompletedTask;
    }
}

internal static class ApiFactoryExtensions
{
    /// <summary>A client over in-memory repository and global-settings stores (the real effective-settings provider runs).</summary>
    public static HttpClient CreateClient(this ApiFactory factory, MemoryRepositoryStore repos, MemorySettingsStore settings) =>
        factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IManagedRepositoryStore>();
                services.RemoveAll<ITargetingSettingsStore>();
                services.AddSingleton<IManagedRepositoryStore>(repos);
                services.AddSingleton<ITargetingSettingsStore>(settings);
            })).CreateClient();
}
