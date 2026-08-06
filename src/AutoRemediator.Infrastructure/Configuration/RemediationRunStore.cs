using System.Text.Json;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Storage;
using Azure;
using Azure.Data.Tables;

namespace AutoRemediator.Infrastructure.Configuration;

/// <summary>Persistence for remediation run history, partitioned by repository.</summary>
public interface IRemediationRunStore
{
    Task SaveAsync(RemediationRun run, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemediationRun>> ListByRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemediationRun>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<RemediationRun?> GetAsync(Guid runId, CancellationToken cancellationToken = default);
}

internal sealed class RemediationRunEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // repositoryId
    public string RowKey { get; set; } = string.Empty;       // runId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string RepositorySlug { get; set; } = string.Empty;
    public string Status { get; set; } = nameof(RunStatus.Reading);
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public string UpdatesJson { get; set; } = "[]";
    public string? PullRequestUrl { get; set; }
    public string? Error { get; set; }

    // Verification is stored flat rather than as one blob so the classification stays queryable.
    public string? VerificationStatus { get; set; }
    public string? VerificationSkipReason { get; set; }
    public string? VerificationDiagnosticsJson { get; set; }
    public string? VerificationLogReference { get; set; }

    public static RemediationRunEntity FromDomain(RemediationRun run) => new()
    {
        PartitionKey = run.RepositoryId.ToString(),
        RowKey = run.Id.ToString(),
        RepositorySlug = run.RepositorySlug,
        Status = run.Status.ToString(),
        StartedAtUtc = run.StartedAtUtc,
        FinishedAtUtc = run.FinishedAtUtc,
        UpdatesJson = JsonSerializer.Serialize(run.Updates),
        PullRequestUrl = run.PullRequestUrl,
        Error = run.Error,
        VerificationStatus = run.Verification?.Classification.ToString(),
        VerificationSkipReason = run.Verification?.SkipReason,
        VerificationDiagnosticsJson = run.Verification is { Diagnostics.Count: > 0 } v
            ? JsonSerializer.Serialize(v.Diagnostics)
            : null,
        VerificationLogReference = run.Verification?.LogReference,
    };

    public RemediationRun ToDomain()
    {
        var updates = JsonSerializer.Deserialize<List<DependencyUpdate>>(UpdatesJson) ?? [];
        var status = Enum.TryParse<RunStatus>(Status, out var s) ? s : RunStatus.Reading;
        return RemediationRun.Restore(
            Guid.Parse(RowKey), Guid.Parse(PartitionKey), RepositorySlug, status,
            StartedAtUtc, FinishedAtUtc, updates, PullRequestUrl, Error, ToVerification());
    }

    private VerificationOutcome? ToVerification()
    {
        if (!Enum.TryParse<VerificationClassification>(VerificationStatus, out var classification))
        {
            return null;
        }

        var diagnostics = VerificationDiagnosticsJson is null
            ? []
            : JsonSerializer.Deserialize<List<VerificationDiagnostic>>(VerificationDiagnosticsJson) ?? [];

        return new VerificationOutcome(classification, VerificationSkipReason, diagnostics, VerificationLogReference);
    }
}

internal sealed class TableRemediationRunStore(ITableStore tableStore) : IRemediationRunStore
{
    private const string TableName = "runs";

    public async Task SaveAsync(RemediationRun run, CancellationToken cancellationToken = default)
    {
        var table = await tableStore.GetTableAsync(TableName, cancellationToken);
        await table.UpsertEntityAsync(RemediationRunEntity.FromDomain(run), TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<IReadOnlyList<RemediationRun>> ListByRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        var table = await tableStore.GetTableAsync(TableName, cancellationToken);
        var results = new List<RemediationRun>();

        await foreach (var entity in table.QueryAsync<RemediationRunEntity>(
            e => e.PartitionKey == repositoryId.ToString(), cancellationToken: cancellationToken))
        {
            results.Add(entity.ToDomain());
        }

        return results;
    }

    public async Task<IReadOnlyList<RemediationRun>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var table = await tableStore.GetTableAsync(TableName, cancellationToken);
        var results = new List<RemediationRun>();

        await foreach (var entity in table.QueryAsync<RemediationRunEntity>(cancellationToken: cancellationToken))
        {
            results.Add(entity.ToDomain());
        }

        return results;
    }

    public async Task<RemediationRun?> GetAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var table = await tableStore.GetTableAsync(TableName, cancellationToken);

        await foreach (var entity in table.QueryAsync<RemediationRunEntity>(
            e => e.RowKey == runId.ToString(), cancellationToken: cancellationToken))
        {
            return entity.ToDomain();
        }

        return null;
    }
}
