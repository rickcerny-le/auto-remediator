using System.Collections.Concurrent;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Review;
using AutoRemediator.Infrastructure.Verification;

namespace AutoRemediator.Infrastructure.Tests;

// Reusable in-memory stand-ins for the storage-backed services, for tests that compose the real
// pipeline through AddInfrastructure and replace only storage.

internal sealed class InMemoryRepositoryStore : IManagedRepositoryStore
{
    private readonly ConcurrentDictionary<Guid, ManagedRepository> _items = new();

    public Task<IReadOnlyList<ManagedRepository>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ManagedRepository>>(_items.Values.ToList());

    public Task<ManagedRepository?> GetAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_items.GetValueOrDefault(id));

    public Task UpsertAsync(ManagedRepository repository, CancellationToken ct = default)
    {
        _items[repository.Id] = repository;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _items.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}

internal sealed class InMemorySettingsStore : ITargetingSettingsStore
{
    public TargetingSettings Current { get; set; } = TargetingSettings.Empty;

    /// <summary>Number of times the settings were read (lets tests assert the global settings are loaded once).</summary>
    public int GetCount { get; private set; }

    public Task<TargetingSettings> GetAsync(CancellationToken ct = default)
    {
        GetCount++;
        return Task.FromResult(Current);
    }

    public Task SetAsync(TargetingSettings settings, CancellationToken ct = default)
    {
        Current = settings;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryRunStore : IRemediationRunStore
{
    private readonly ConcurrentDictionary<Guid, RemediationRun> _runs = new();

    public Task SaveAsync(RemediationRun run, CancellationToken ct = default)
    {
        _runs[run.Id] = run;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RemediationRun>> ListByRepositoryAsync(Guid repositoryId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RemediationRun>>(_runs.Values.Where(r => r.RepositoryId == repositoryId).ToList());

    public Task<IReadOnlyList<RemediationRun>> ListAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RemediationRun>>(_runs.Values.ToList());

    public Task<RemediationRun?> GetAsync(Guid runId, CancellationToken ct = default)
        => Task.FromResult(_runs.GetValueOrDefault(runId));

    public Task<RemediationRun?> FindAwaitingReviewAsync(Guid repositoryId, CancellationToken ct = default)
        => Task.FromResult(_runs.Values.FirstOrDefault(r => r.RepositoryId == repositoryId && r.Status == RunStatus.AwaitingReview));
}

internal sealed class InMemoryProposalStore : IChangeProposalStore
{
    private readonly ConcurrentDictionary<string, ChangeProposal> _proposals = new();

    public Task<string> StoreAsync(ChangeProposal proposal, CancellationToken ct = default)
    {
        var reference = $"{proposal.RunId}/proposal.json";
        _proposals[reference] = proposal;
        return Task.FromResult(reference);
    }

    public Task<ChangeProposal?> ReadAsync(string reference, CancellationToken ct = default)
        => Task.FromResult(_proposals.GetValueOrDefault(reference));
}

internal sealed class InMemoryLogStore : IVerificationLogStore
{
    private readonly ConcurrentDictionary<string, string> _logs = new();

    public Task<string?> StoreAsync(Guid runId, string content, string name = "verification.log", CancellationToken ct = default)
    {
        var reference = $"{runId}/{name}";
        _logs[reference] = content;
        return Task.FromResult<string?>(reference);
    }

    public Task<string?> ReadAsync(string reference, CancellationToken ct = default)
        => Task.FromResult(_logs.GetValueOrDefault(reference));
}
