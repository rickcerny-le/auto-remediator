namespace AutoRemediator.Contracts.Dtos;

/// <summary>
/// API/UI-facing summary of an enrolled repository and its latest run state.
/// </summary>
/// <param name="Id">Repository id.</param>
/// <param name="Slug">Fully-qualified slug "org/project/repo".</param>
/// <param name="Enabled">Whether automated remediation is enabled.</param>
/// <param name="LastRunStatus">Human-readable status of the most recent run, if any.</param>
public sealed record RepositorySummary(
    Guid Id,
    string Slug,
    bool Enabled,
    string? LastRunStatus);
