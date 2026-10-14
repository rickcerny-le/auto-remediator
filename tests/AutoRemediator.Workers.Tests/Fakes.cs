using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Messaging;
using Microsoft.Extensions.Hosting;

namespace AutoRemediator.Workers.Tests;

internal sealed class FakeManagedRepositoryStore(IReadOnlyList<ManagedRepository> repositories) : IManagedRepositoryStore
{
    public Task<IReadOnlyList<ManagedRepository>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(repositories);

    public Task<ManagedRepository?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(repositories.FirstOrDefault(r => r.Id == id));

    public Task UpsertAsync(ManagedRepository repository, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed record PublishedMessage(string Queue, object Message);

internal sealed class RecordingPublisher : IMessagePublisher
{
    public List<PublishedMessage> Messages { get; } = [];

    public Task PublishAsync<T>(string queue, T message, CancellationToken cancellationToken = default)
    {
        Messages.Add(new PublishedMessage(queue, message!));
        return Task.CompletedTask;
    }
}

/// <summary>Delivers exactly one message body to the handler, then signals completion.</summary>
internal sealed class SingleMessageConsumer(string body) : IMessageConsumer
{
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Completed => _completed.Task;

    public async Task RunAsync(string queue, Func<string, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        await handler(body, cancellationToken);
        _completed.TrySetResult();
    }
}

internal sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
{
    private readonly CancellationTokenSource _stopping = new();
    public bool StopRequested { get; private set; }

    public CancellationToken ApplicationStarted => CancellationToken.None;
    public CancellationToken ApplicationStopping => _stopping.Token;
    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void StopApplication()
    {
        StopRequested = true;
        _stopping.Cancel();
    }
}

/// <summary>Publishes like <see cref="RecordingPublisher"/> but throws for the named repositories.</summary>
internal sealed class FailingPublisher(params string[] failFor) : IMessagePublisher
{
    public List<PublishedMessage> Messages { get; } = [];

    public Task PublishAsync<T>(string queue, T message, CancellationToken cancellationToken = default)
    {
        if (message is AutoRemediator.Contracts.Messages.RemediationRunRequested r && failFor.Contains(r.RepositoryName))
        {
            throw new InvalidOperationException("Service Bus unavailable.");
        }

        lock (Messages)
        {
            Messages.Add(new PublishedMessage(queue, message!));
        }

        return Task.CompletedTask;
    }
}

/// <summary>Resolves effective settings with the real resolver over a fixed global baseline.</summary>
internal sealed class FakeEffectiveSettingsProvider(TargetingSettings? global = null) : AutoRemediator.Infrastructure.Configuration.IEffectiveSettingsProvider
{
    private readonly TargetingSettings _global = global ?? TargetingSettings.Empty;

    public Task<EffectiveSettings> GetAsync(ManagedRepository repository, CancellationToken cancellationToken = default)
        => Task.FromResult(EffectiveSettingsResolver.Resolve(_global, repository.Settings));

    public Task<IReadOnlyDictionary<Guid, EffectiveSettings>> GetAsync(
        IEnumerable<ManagedRepository> repositories, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, EffectiveSettings>>(
            repositories.ToDictionary(r => r.Id, r => EffectiveSettingsResolver.Resolve(_global, r.Settings)));
}

/// <summary>In-memory schedule state with real ETag semantics and test hooks.</summary>
internal sealed class InMemoryScheduleStateStore : AutoRemediator.Infrastructure.Scheduling.IScheduleStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, (DateTimeOffset? Last, int Version)> _rows = [];
    private int _versions;

    /// <summary>When set, every claim reports a conflict.</summary>
    public bool RejectClaims { get; set; }

    /// <summary>When set, <see cref="GetAsync"/> waits here so concurrent ticks both read before either claims.</summary>
    public Barrier? ReadBarrier { get; set; }

    public int ClaimCount { get; private set; }
    public List<Guid> Reads { get; } = [];

    public void Seed(Guid id, DateTimeOffset last)
    {
        lock (_gate)
        {
            _rows[id] = (last, ++_versions);
        }
    }

    public DateTimeOffset? LastFor(Guid id)
    {
        lock (_gate)
        {
            return _rows.TryGetValue(id, out var row) ? row.Last : null;
        }
    }

    public bool HasRow(Guid id)
    {
        lock (_gate)
        {
            return _rows.ContainsKey(id);
        }
    }

    public Task<AutoRemediator.Infrastructure.Scheduling.ScheduleState?> GetAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        AutoRemediator.Infrastructure.Scheduling.ScheduleState? state = null;
        lock (_gate)
        {
            Reads.Add(repositoryId);
            if (_rows.TryGetValue(repositoryId, out var row))
            {
                state = new(repositoryId, row.Last, row.Version.ToString());
            }
        }

        ReadBarrier?.SignalAndWait(TimeSpan.FromSeconds(10), cancellationToken);
        return Task.FromResult(state);
    }

    public Task<bool> TryClaimAsync(
        Guid repositoryId, AutoRemediator.Infrastructure.Scheduling.ScheduleState? current, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ClaimCount++;
            if (RejectClaims)
            {
                return Task.FromResult(false);
            }

            var exists = _rows.TryGetValue(repositoryId, out var row);
            if (current is null ? exists : !exists || row.Version.ToString() != current.ETag)
            {
                return Task.FromResult(false);
            }

            _rows[repositoryId] = (now, ++_versions);
            return Task.FromResult(true);
        }
    }

    public Task RevertAsync(Guid repositoryId, DateTimeOffset? previous, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _rows[repositoryId] = (previous, ++_versions);
        }

        return Task.CompletedTask;
    }
}
