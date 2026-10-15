using AutoRemediator.Infrastructure.Storage;
using Azure;
using Azure.Data.Tables;

namespace AutoRemediator.Infrastructure.Scheduling;

/// <summary>The scheduler's bookkeeping for one repository.</summary>
public sealed record ScheduleState(Guid RepositoryId, DateTimeOffset? LastScheduledAtUtc, string? ETag);

/// <summary>
/// Per-repository schedule state, kept in its own table so the API's full-replace repository
/// upsert can never clobber it and so ETags can arbitrate overlapping scheduler ticks.
/// </summary>
public interface IScheduleStateStore
{
    Task<ScheduleState?> GetAsync(Guid repositoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically sets <c>LastScheduledAtUtc = now</c>. <paramref name="current"/> is the state that
    /// was read (null when there was none): a missing row is inserted, an existing one is updated
    /// with If-Match. Returns false when another tick got there first (409/412).
    /// </summary>
    Task<bool> TryClaimAsync(
        Guid repositoryId, ScheduleState? current, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Best effort, unconditional: restores <paramref name="previous"/> after a failed publish.</summary>
    Task RevertAsync(Guid repositoryId, DateTimeOffset? previous, CancellationToken cancellationToken = default);
}

internal sealed class ScheduleStateEntity : ITableEntity
{
    public const string Partition = "schedule";

    public string PartitionKey { get; set; } = Partition;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public DateTimeOffset? LastScheduledAtUtc { get; set; }
}

internal sealed class TableScheduleStateStore(ITableStore tableStore) : IScheduleStateStore
{
    private const string TableName = "schedulestate";

    public async Task<ScheduleState?> GetAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        var table = await tableStore.GetTableAsync(TableName, cancellationToken);
        try
        {
            var response = await table.GetEntityAsync<ScheduleStateEntity>(
                ScheduleStateEntity.Partition, repositoryId.ToString(), cancellationToken: cancellationToken);
            var entity = response.Value;
            return new ScheduleState(repositoryId, entity.LastScheduledAtUtc, entity.ETag.ToString());
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> TryClaimAsync(
        Guid repositoryId, ScheduleState? current, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var table = await tableStore.GetTableAsync(TableName, cancellationToken);
        var entity = new ScheduleStateEntity { RowKey = repositoryId.ToString(), LastScheduledAtUtc = now };

        try
        {
            if (current is null)
            {
                await table.AddEntityAsync(entity, cancellationToken);
            }
            else
            {
                await table.UpdateEntityAsync(
                    entity, new ETag(current.ETag ?? "*"), TableUpdateMode.Replace, cancellationToken);
            }

            return true;
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            return false;
        }
    }

    public async Task RevertAsync(Guid repositoryId, DateTimeOffset? previous, CancellationToken cancellationToken = default)
    {
        var table = await tableStore.GetTableAsync(TableName, cancellationToken);
        await table.UpsertEntityAsync(
            new ScheduleStateEntity { RowKey = repositoryId.ToString(), LastScheduledAtUtc = previous },
            TableUpdateMode.Replace,
            cancellationToken);
    }
}
