using System.Text;
using System.Text.Json;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace AutoRemediator.Infrastructure.Review;

/// <summary>
/// Stores the <see cref="ChangeProposal"/> payload as a run artifact, one blob per run at
/// <c>{runId}/proposal.json</c> in the same container as verification logs and transcripts.
/// </summary>
/// <remarks>
/// Unlike <see cref="Verification.IVerificationLogStore"/>, which swallows a storage failure because
/// losing a log must not change the verdict it describes, this store lets a storage failure
/// propagate. A run must not rest in <see cref="RunStatus.AwaitingReview"/> claiming a proposal it
/// could not actually write (FR-008); the caller's existing failure handling turns the exception
/// into a <see cref="RunStatus.Failed"/> run instead.
/// </remarks>
public interface IChangeProposalStore
{
    /// <summary>Writes the proposal and returns its blob reference. Throws if the write fails.</summary>
    Task<string> StoreAsync(ChangeProposal proposal, CancellationToken cancellationToken = default);

    /// <summary>Reads back a stored proposal, or null when the reference is unknown.</summary>
    Task<ChangeProposal?> ReadAsync(string reference, CancellationToken cancellationToken = default);
}

internal sealed class ChangeProposalStore(IBlobStore blobs, ILogger<ChangeProposalStore> logger) : IChangeProposalStore
{
    public const string ContainerName = "run-artifacts";

    public async Task<string> StoreAsync(ChangeProposal proposal, CancellationToken cancellationToken = default)
    {
        var reference = $"{proposal.RunId}/proposal.json";

        var container = await blobs.GetContainerAsync(ContainerName, cancellationToken);
        var blob = container.GetBlobClient(reference);

        var json = JsonSerializer.Serialize(proposal);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true, cancellationToken);

        return reference;
    }

    public async Task<ChangeProposal?> ReadAsync(string reference, CancellationToken cancellationToken = default)
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
            return JsonSerializer.Deserialize<ChangeProposal>(result.Value.Content.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the change proposal at {Reference}.", reference);
            return null;
        }
    }
}
