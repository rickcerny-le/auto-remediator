namespace AutoRemediator.Infrastructure.AzureDevOps;

/// <summary>
/// The JSON record <see cref="LocalRepositoryClient"/> keeps for a local "pull request", at
/// <c>{LocalPullRequestsRoot}/{repository}/{id}.json</c>. <see cref="BaseCommit"/> is the merge-base of the
/// target and source branches and <see cref="HeadCommit"/> the source branch head, so a diff reader can
/// show <c>BaseCommit..HeadCommit</c> without consulting the branches again.
/// </summary>
internal sealed record LocalPullRequestRecord(
    int Id,
    string Organization,
    string Project,
    string Repository,
    string SourceBranch,
    string TargetBranch,
    string BaseCommit,
    string HeadCommit,
    string Title,
    string Description,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Url);
