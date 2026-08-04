using Azure.Storage.Blobs;

namespace AutoRemediator.Infrastructure.Storage;

/// <summary>
/// Thin accessor for Azure Blob Storage containers (run artifacts, logs, diffs).
/// </summary>
public interface IBlobStore
{
    /// <summary>Returns a client for the named container, creating it if missing.</summary>
    Task<BlobContainerClient> GetContainerAsync(string containerName, CancellationToken cancellationToken = default);
}

internal sealed class BlobStore(BlobServiceClient serviceClient) : IBlobStore
{
    public async Task<BlobContainerClient> GetContainerAsync(string containerName, CancellationToken cancellationToken = default)
    {
        var client = serviceClient.GetBlobContainerClient(containerName);
        await client.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        return client;
    }
}
