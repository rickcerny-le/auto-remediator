using System.Text.Json;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Storage;
using Azure;
using Azure.Data.Tables;

namespace AutoRemediator.Infrastructure.Configuration;

/// <summary>Persistence for the singleton global targeting settings.</summary>
public interface ITargetingSettingsStore
{
    Task<TargetingSettings> GetAsync(CancellationToken cancellationToken = default);
    Task SetAsync(TargetingSettings settings, CancellationToken cancellationToken = default);
}

internal sealed class TargetingSettingsEntity : ITableEntity
{
    public const string Partition = "settings";
    public const string Row = "global";

    public string PartitionKey { get; set; } = Partition;
    public string RowKey { get; set; } = Row;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Collections serialized as JSON columns.
    public string PatternsJson { get; set; } = "[]";
    public string ExcludesJson { get; set; } = "[]";
    public string FeedsJson { get; set; } = "[]";
    public string IgnoreJson { get; set; } = "[]";
    public string Strategy { get; set; } = nameof(UpdateStrategy.Minor);
    public bool AllowPrerelease { get; set; }
}

internal sealed class TableTargetingSettingsStore(ITableStore tableStore) : ITargetingSettingsStore
{
    private const string TableName = "config";

    public async Task<TargetingSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var table = await tableStore.GetTableAsync(TableName, cancellationToken);
        try
        {
            var response = await table.GetEntityAsync<TargetingSettingsEntity>(
                TargetingSettingsEntity.Partition, TargetingSettingsEntity.Row, cancellationToken: cancellationToken);
            var e = response.Value;

            var strategy = Enum.TryParse<UpdateStrategy>(e.Strategy, ignoreCase: true, out var s) ? s : UpdateStrategy.Minor;
            var policy = new UpdatePolicy(strategy, Deserialize(e.IgnoreJson), e.AllowPrerelease);

            return new TargetingSettings(
                Deserialize(e.PatternsJson),
                Deserialize(e.ExcludesJson),
                Deserialize(e.FeedsJson),
                policy);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return TargetingSettings.Empty;
        }
    }

    public async Task SetAsync(TargetingSettings settings, CancellationToken cancellationToken = default)
    {
        var table = await tableStore.GetTableAsync(TableName, cancellationToken);
        var entity = new TargetingSettingsEntity
        {
            PatternsJson = Serialize(settings.Patterns),
            ExcludesJson = Serialize(settings.Excludes),
            FeedsJson = Serialize(settings.Feeds),
            IgnoreJson = Serialize(settings.Policy.Ignore),
            Strategy = settings.Policy.Strategy.ToString(),
            AllowPrerelease = settings.Policy.AllowPrerelease,
        };
        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    private static string Serialize(IReadOnlyList<string> values) => JsonSerializer.Serialize(values);

    private static List<string> Deserialize(string json)
        => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];
}
