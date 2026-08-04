using Azure.Data.Tables;

namespace AutoRemediator.Infrastructure.Storage;

/// <summary>
/// Thin accessor for Azure Table Storage tables (repo config, run status).
/// </summary>
public interface ITableStore
{
    /// <summary>Returns a client for the named table, creating it if missing.</summary>
    Task<TableClient> GetTableAsync(string tableName, CancellationToken cancellationToken = default);
}

internal sealed class TableStore(TableServiceClient serviceClient) : ITableStore
{
    public async Task<TableClient> GetTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var client = serviceClient.GetTableClient(tableName);
        await client.CreateIfNotExistsAsync(cancellationToken);
        return client;
    }
}
