using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Scheduling;
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

internal sealed class MemoryScheduleStateStore : IScheduleStateStore
{
    private readonly Dictionary<Guid, DateTimeOffset?> _rows = new();

    public void Seed(Guid id, DateTimeOffset last) => _rows[id] = last;

    public Task<ScheduleState?> GetAsync(Guid repositoryId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ScheduleState?>(_rows.TryGetValue(repositoryId, out var last) ? new(repositoryId, last, "1") : null);

    public Task<bool> TryClaimAsync(
        Guid repositoryId, ScheduleState? current, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        _rows[repositoryId] = now;
        return Task.FromResult(true);
    }

    public Task RevertAsync(Guid repositoryId, DateTimeOffset? previous, CancellationToken cancellationToken = default)
    {
        _rows[repositoryId] = previous;
        return Task.CompletedTask;
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal static class ApiFactoryExtensions
{
    /// <summary>A client over in-memory repository and global-settings stores (the real effective-settings provider runs).</summary>
    public static HttpClient CreateClient(
        this ApiFactory factory,
        MemoryRepositoryStore repos,
        MemorySettingsStore settings,
        MemoryScheduleStateStore? scheduleState = null,
        TimeProvider? time = null) =>
        factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IManagedRepositoryStore>();
                services.RemoveAll<ITargetingSettingsStore>();
                services.RemoveAll<IScheduleStateStore>();
                services.AddSingleton<IManagedRepositoryStore>(repos);
                services.AddSingleton<ITargetingSettingsStore>(settings);
                services.AddSingleton<IScheduleStateStore>(scheduleState ?? new MemoryScheduleStateStore());
                if (time is not null)
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton(time);
                }
            })).CreateClient();
}
