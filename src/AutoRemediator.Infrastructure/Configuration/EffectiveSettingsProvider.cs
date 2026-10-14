using AutoRemediator.Domain;

namespace AutoRemediator.Infrastructure.Configuration;

/// <summary>
/// The single place that produces the settings that apply to a repository: the global settings
/// from <see cref="ITargetingSettingsStore"/> layered with the repository's own settings.
/// </summary>
public interface IEffectiveSettingsProvider
{
    Task<EffectiveSettings> GetAsync(ManagedRepository repository, CancellationToken cancellationToken = default);

    /// <summary>Resolves many repositories, reading the global settings once. Keyed by repository id.</summary>
    Task<IReadOnlyDictionary<Guid, EffectiveSettings>> GetAsync(
        IEnumerable<ManagedRepository> repositories, CancellationToken cancellationToken = default);
}

internal sealed class EffectiveSettingsProvider(ITargetingSettingsStore settingsStore) : IEffectiveSettingsProvider
{
    public async Task<EffectiveSettings> GetAsync(ManagedRepository repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var global = await settingsStore.GetAsync(cancellationToken);
        return EffectiveSettingsResolver.Resolve(global, repository.Settings);
    }

    public async Task<IReadOnlyDictionary<Guid, EffectiveSettings>> GetAsync(
        IEnumerable<ManagedRepository> repositories, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        var global = await settingsStore.GetAsync(cancellationToken);
        var result = new Dictionary<Guid, EffectiveSettings>();
        foreach (var repository in repositories)
        {
            result[repository.Id] = EffectiveSettingsResolver.Resolve(global, repository.Settings);
        }

        return result;
    }
}
