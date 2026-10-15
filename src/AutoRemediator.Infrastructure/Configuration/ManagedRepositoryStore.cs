using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Storage;
using Azure;
using Azure.Data.Tables;

namespace AutoRemediator.Infrastructure.Configuration;

/// <summary>Persistence for the configured managed repositories.</summary>
public interface IManagedRepositoryStore
{
    Task<IReadOnlyList<ManagedRepository>> ListAsync(CancellationToken cancellationToken = default);
    Task<ManagedRepository?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpsertAsync(ManagedRepository repository, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class ManagedRepositoryEntity : ITableEntity
{
    public const string Partition = "repo";

    public string PartitionKey { get; set; } = Partition;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Organization { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string TargetBranch { get; set; } = "main";

    // Per-repository settings. All nullable: rows are written with TableUpdateMode.Replace and
    // legacy rows lack these columns, so null means inherit/unset.
    public bool? InheritGlobal { get; set; }
    public string? PatternsJson { get; set; }
    public string? ExcludesJson { get; set; }
    public string? FeedsJson { get; set; }
    public string? IgnoreJson { get; set; }
    public string? Strategy { get; set; }
    public bool? AllowPrerelease { get; set; }
    public string? Schedule { get; set; }

    public static ManagedRepositoryEntity FromDomain(ManagedRepository r)
    {
        var s = r.Settings;
        return new()
        {
            RowKey = r.Id.ToString(),
            Organization = r.Organization,
            Project = r.Project,
            Name = r.Name,
            Enabled = r.Enabled,
            TargetBranch = r.TargetBranch,
            // Null is the default (true), so an inherit-everything repository matches a legacy row.
            InheritGlobal = s.InheritGlobal ? null : false,
            PatternsJson = JsonLists.SerializeOrNull(s.Patterns),
            ExcludesJson = JsonLists.SerializeOrNull(s.Excludes),
            FeedsJson = JsonLists.SerializeOrNull(s.Feeds),
            IgnoreJson = JsonLists.SerializeOrNull(s.Ignore),
            Strategy = s.Strategy?.ToString(),
            AllowPrerelease = s.AllowPrerelease,
            Schedule = s.Schedule,
        };
    }

    public ManagedRepository ToDomain()
    {
        var settings = new RepositorySettings(
            InheritGlobal ?? true,
            JsonLists.Deserialize(PatternsJson),
            JsonLists.Deserialize(ExcludesJson),
            JsonLists.Deserialize(FeedsJson),
            JsonLists.Deserialize(IgnoreJson),
            Enum.TryParse<UpdateStrategy>(Strategy, ignoreCase: true, out var strategy) ? strategy : null,
            AllowPrerelease,
            Schedule);
        return new(Guid.Parse(RowKey), Organization, Project, Name, Enabled, TargetBranch, settings);
    }
}

internal sealed class TableManagedRepositoryStore(ITableStore tableStore) : IManagedRepositoryStore
{
    private const string TableName = "repositories";

    public async Task<IReadOnlyList<ManagedRepository>> ListAsync(CancellationToken cancellationToken = default)
    {
        var table = await tableStore.GetTableAsync(TableName, cancellationToken);
        var results = new List<ManagedRepository>();

        await foreach (var entity in table.QueryAsync<ManagedRepositoryEntity>(
            e => e.PartitionKey == ManagedRepositoryEntity.Partition, cancellationToken: cancellationToken))
        {
            results.Add(entity.ToDomain());
        }

        return results;
    }

    public async Task<ManagedRepository?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var table = await tableStore.GetTableAsync(TableName, cancellationToken);
        try
        {
            var response = await table.GetEntityAsync<ManagedRepositoryEntity>(
                ManagedRepositoryEntity.Partition, id.ToString(), cancellationToken: cancellationToken);
            return response.Value.ToDomain();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task UpsertAsync(ManagedRepository repository, CancellationToken cancellationToken = default)
    {
        var table = await tableStore.GetTableAsync(TableName, cancellationToken);
        await table.UpsertEntityAsync(ManagedRepositoryEntity.FromDomain(repository), TableUpdateMode.Replace, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var table = await tableStore.GetTableAsync(TableName, cancellationToken);
        await table.DeleteEntityAsync(ManagedRepositoryEntity.Partition, id.ToString(), cancellationToken: cancellationToken);
    }
}
