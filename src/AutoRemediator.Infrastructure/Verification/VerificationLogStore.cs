using System.Text;
using AutoRemediator.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace AutoRemediator.Infrastructure.Verification;

/// <summary>
/// Stores the full restore/build output as a run artifact. The run record keeps only a bounded set of
/// diagnostics, so this is where the complete log lives.
/// </summary>
public interface IVerificationLogStore
{
    /// <summary>
    /// Writes an artifact for a run and returns its reference, or null if it could not be stored.
    /// <paramref name="name"/> distinguishes artifacts within a run — a run that verifies more than
    /// once must not have its earlier logs overwritten by later attempts.
    /// </summary>
    Task<string?> StoreAsync(
        Guid runId,
        string content,
        string name = "verification.log",
        CancellationToken cancellationToken = default);

    /// <summary>Reads back a stored log, or null when the reference is unknown.</summary>
    Task<string?> ReadAsync(string reference, CancellationToken cancellationToken = default);
}

internal sealed class BlobVerificationLogStore(IBlobStore blobs, ILogger<BlobVerificationLogStore> logger) : IVerificationLogStore
{
    public const string ContainerName = "run-artifacts";

    public async Task<string?> StoreAsync(
        Guid runId,
        string content,
        string name = "verification.log",
        CancellationToken cancellationToken = default)
    {
        var reference = $"{runId}/{name}";

        try
        {
            var container = await blobs.GetContainerAsync(ContainerName, cancellationToken);
            var blob = container.GetBlobClient(reference);

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            await blob.UploadAsync(stream, overwrite: true, cancellationToken);

            return reference;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Losing the log must not change the verdict the log describes.
            logger.LogWarning(ex, "Run {RunId}: could not store the verification log.", runId);
            return null;
        }
    }

    public async Task<string?> ReadAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var container = await blobs.GetContainerAsync(ContainerName, cancellationToken);
            var blob = container.GetBlobClient(reference);

            if (!await blob.ExistsAsync(cancellationToken))
            {
                return null;
            }

            var result = await blob.DownloadContentAsync(cancellationToken);
            return result.Value.Content.ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the verification log at {Reference}.", reference);
            return null;
        }
    }
}
