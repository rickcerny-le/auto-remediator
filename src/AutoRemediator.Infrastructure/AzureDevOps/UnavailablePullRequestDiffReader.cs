namespace AutoRemediator.Infrastructure.AzureDevOps;

/// <summary>The default reader: pull request diffs are only available in local repository mode.</summary>
internal sealed class UnavailablePullRequestDiffReader : IPullRequestDiffReader
{
    public bool IsAvailable => false;

    public Task<PullRequestDiff?> GetDiffAsync(string pullRequestUrl, CancellationToken cancellationToken)
        => Task.FromResult<PullRequestDiff?>(null);
}
