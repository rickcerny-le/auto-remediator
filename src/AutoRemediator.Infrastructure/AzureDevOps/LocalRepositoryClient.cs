using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoRemediator.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Infrastructure.AzureDevOps;

/// <summary>
/// Local stand-in for <see cref="AzureDevOpsClient"/>, selected with <c>AzureDevOps:Mode=Local</c>. Each
/// managed repository maps to a plain git repository at <c>{LocalRoot}/{Name}</c> (organization and project
/// are ignored). Uses the <c>git</c> binary and never touches the source repository's working tree, index
/// or checked-out branch: pushes are built in a throwaway worktree and published by moving a ref.
/// </summary>
internal sealed partial class LocalRepositoryClient : IAzureDevOpsClient
{
    private static readonly JsonSerializerOptions RecordJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly GitCli _git = new();
    private readonly SemaphoreSlim _pullRequestLock = new(1, 1);
    private readonly string _root;
    private readonly string _pullRequestsRoot;
    private readonly TimeProvider _time;
    private readonly ILogger<LocalRepositoryClient> _logger;

    public LocalRepositoryClient(
        IOptions<AzureDevOpsOptions> options,
        TimeProvider time,
        ILogger<LocalRepositoryClient> logger)
    {
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.LocalRoot))
        {
            throw new InvalidOperationException("AzureDevOps:LocalRoot must be set when AzureDevOps:Mode is Local.");
        }

        _root = Path.GetFullPath(value.LocalRoot);
        _pullRequestsRoot = Path.GetFullPath(value.EffectiveLocalPullRequestsRoot!);
        _time = time;
        _logger = logger;
    }

    public async Task<bool> RepositoryExistsAsync(ManagedRepository repository, CancellationToken cancellationToken = default)
        => await ResolveRepositoryAsync(repository, cancellationToken) is not null;

    public async Task<IReadOnlyList<RepositoryFile>> GetManifestsAsync(ManagedRepository repository, CancellationToken cancellationToken = default)
    {
        var repoPath = await ResolveRepositoryAsync(repository, cancellationToken);
        if (repoPath is null)
        {
            return [];
        }

        var reference = BranchRef(repository.TargetBranch);
        var listing = await _git.RunAsync(repoPath, ["ls-tree", "-r", "-z", "--name-only", reference], cancellationToken);
        if (!listing.Succeeded)
        {
            // Missing branch reads as "no manifests", exactly as the Azure DevOps client does.
            return [];
        }

        var files = new List<RepositoryFile>();
        foreach (var path in listing.Text.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(IsManifest))
        {
            var show = await _git.RunAsync(repoPath, ["show", $"{reference}:{path}"], cancellationToken);
            if (show.Succeeded)
            {
                files.Add(new RepositoryFile("/" + path, show.Text));
            }
        }

        return files;
    }

    public async Task<string?> GetBranchHeadAsync(ManagedRepository repository, string branch, CancellationToken cancellationToken = default)
    {
        var repoPath = await ResolveRepositoryAsync(repository, cancellationToken);
        return repoPath is null ? null : await BranchHeadAsync(repoPath, branch, cancellationToken);
    }

    public async Task<Stream> GetRepositoryArchiveAsync(ManagedRepository repository, string commitId, CancellationToken cancellationToken = default)
    {
        var repoPath = await ResolveRepositoryAsync(repository, cancellationToken)
            ?? throw new InvalidOperationException($"Local repository '{repository.Name}' does not exist.");
        RequireCommitId(commitId);

        var result = await _git.RunAsync(repoPath, ["archive", "--format=zip", commitId], cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"git archive failed: {result.Error.Trim()}");
        }

        return new MemoryStream(result.Bytes, writable: false);
    }

    public async Task PushFilesAsync(
        ManagedRepository repository,
        string branch,
        string baseCommitId,
        IReadOnlyList<FileChange> changes,
        string message,
        CancellationToken cancellationToken = default)
    {
        var repoPath = await ResolveRepositoryAsync(repository, cancellationToken)
            ?? throw new InvalidOperationException($"Local repository '{repository.Name}' does not exist.");
        RequireCommitId(baseCommitId);

        var worktree = Path.Combine(Path.GetTempPath(), "autoremediator-wt-" + Guid.NewGuid().ToString("N"));
        try
        {
            await _git.RequireAsync(repoPath, ["worktree", "add", "--detach", worktree, baseCommitId], cancellationToken);

            var worktreeRoot = Path.GetFullPath(worktree);
            foreach (var change in changes)
            {
                var relative = change.Path.TrimStart('/', '\\');
                var target = Path.GetFullPath(Path.Combine(worktreeRoot, relative));
                var inside = target.StartsWith(worktreeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                var firstSegment = relative.Split('/', '\\')[0];
                if (!inside || firstSegment.Equals(".git", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Refusing to write outside the repository: {change.Path}");
                }

                if (!File.Exists(target))
                {
                    throw new PushRejectedException("GitItemNotFoundException", $"File no longer exists upstream: {change.Path}");
                }

                await File.WriteAllTextAsync(target, change.Content, new UTF8Encoding(false), cancellationToken);
            }

            await _git.RequireAsync(worktree, ["add", "-A"], cancellationToken);
            await _git.RequireAsync(
                worktree,
                [
                    "-c", "user.name=AutoRemediator",
                    "-c", "user.email=autoremediator@localhost",
                    "-c", "commit.gpgsign=false",
                    "commit", "--no-verify", "--allow-empty", "-m", message,
                ],
                cancellationToken);

            var newCommit = (await _git.RequireAsync(worktree, ["rev-parse", "HEAD"], cancellationToken)).Text.Trim();

            // update-ref with an old value is the compare-and-swap Azure DevOps performs for us: it fails when
            // the branch moved since baseCommitId was captured. An empty old value means "must not exist".
            var existing = await BranchHeadAsync(repoPath, branch, cancellationToken);
            var expectedOld = existing is null ? string.Empty : baseCommitId;
            var update = await _git.RunAsync(repoPath, ["update-ref", BranchRef(branch), newCommit, expectedOld], cancellationToken);
            if (!update.Succeeded)
            {
                throw new PushRejectedException("GitRefUpdateStaleException", $"Branch '{branch}' moved since it was read: {update.Error.Trim()}");
            }

            _logger.LogInformation("Pushed {Commit} to {Branch} in local repository {Repository}.", newCommit, branch, repository.Name);
        }
        finally
        {
            await CleanUpWorktreeAsync(repoPath, worktree);
        }
    }

    public async Task<string> EnsurePullRequestAsync(
        ManagedRepository repository,
        string sourceBranch,
        string targetBranch,
        string title,
        string description,
        CancellationToken cancellationToken = default)
    {
        var repoPath = await ResolveRepositoryAsync(repository, cancellationToken)
            ?? throw new InvalidOperationException($"Local repository '{repository.Name}' does not exist.");

        var head = await BranchHeadAsync(repoPath, sourceBranch, cancellationToken)
            ?? throw new InvalidOperationException($"Source branch '{sourceBranch}' does not exist in local repository '{repository.Name}'.");
        if (await BranchHeadAsync(repoPath, targetBranch, cancellationToken) is null)
        {
            throw new InvalidOperationException($"Target branch '{targetBranch}' does not exist in local repository '{repository.Name}'.");
        }

        var baseCommit = (await _git.RequireAsync(repoPath, ["merge-base", BranchRef(targetBranch), BranchRef(sourceBranch)], cancellationToken))
            .Text.Trim();

        var directory = Path.Combine(_pullRequestsRoot, repository.Name);
        await _pullRequestLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(directory);
            var now = _time.GetUtcNow();

            var records = new List<(string File, LocalPullRequestRecord Record)>();
            var maxId = 0;
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                if (int.TryParse(Path.GetFileNameWithoutExtension(file), out var fileId))
                {
                    maxId = Math.Max(maxId, fileId);
                }

                var record = await TryReadRecordAsync(file, cancellationToken);
                if (record is not null)
                {
                    maxId = Math.Max(maxId, record.Id);
                    records.Add((file, record));
                }
            }

            var existing = records.FirstOrDefault(r =>
                string.Equals(r.Record.Status, "active", StringComparison.OrdinalIgnoreCase)
                && r.Record.SourceBranch == sourceBranch
                && r.Record.TargetBranch == targetBranch);
            if (existing.File is not null)
            {
                // An ADO pull request always shows the branch's latest push, so refresh the commits only.
                var refreshed = existing.Record with { BaseCommit = baseCommit, HeadCommit = head, UpdatedAtUtc = now };
                await File.WriteAllTextAsync(existing.File, JsonSerializer.Serialize(refreshed, RecordJson), new UTF8Encoding(false), cancellationToken);
                return existing.Record.Url;
            }

            var id = maxId + 1;
            var path = Path.GetFullPath(Path.Combine(directory, id + ".json"));
            var created = new LocalPullRequestRecord(
                id,
                repository.Organization,
                repository.Project,
                repository.Name,
                sourceBranch,
                targetBranch,
                baseCommit,
                head,
                title,
                description,
                "active",
                now,
                now,
                new Uri(path).AbsoluteUri);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(created, RecordJson), new UTF8Encoding(false), cancellationToken);
            return created.Url;
        }
        finally
        {
            _pullRequestLock.Release();
        }
    }

    private async Task<LocalPullRequestRecord?> TryReadRecordAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(file);
            return await JsonSerializer.DeserializeAsync<LocalPullRequestRecord>(stream, RecordJson, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Ignoring unreadable local pull request record {File}.", file);
            return null;
        }
    }

    /// <summary>The repository folder when the name is safe and the folder is its own git repo; otherwise null.</summary>
    private async Task<string?> ResolveRepositoryAsync(ManagedRepository repository, CancellationToken cancellationToken)
    {
        var name = repository.Name;
        if (string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || name.Contains("..", StringComparison.Ordinal)
            || name.IndexOfAny(['/', '\\']) >= 0
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(_root, name));
        if (!path.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(path))
        {
            return null;
        }

        var gitDir = await _git.RunAsync(path, ["rev-parse", "--git-dir"], cancellationToken);
        if (!gitDir.Succeeded)
        {
            return null;
        }

        // From the top of a repository git prints ".git" (or "." when bare). Anything else means the folder
        // is not a repository itself and git found one further up, which must not count.
        var printed = gitDir.Text.Trim();
        if (printed is ".git" or ".")
        {
            return path;
        }

        var absolute = Path.GetFullPath(Path.Combine(path, printed));
        return absolute.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private async Task<string?> BranchHeadAsync(string repoPath, string branch, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(repoPath, ["rev-parse", "--verify", "--quiet", BranchRef(branch) + "^{commit}"], cancellationToken);
        return result.Succeeded ? result.Text.Trim() : null;
    }

    private async Task CleanUpWorktreeAsync(string repoPath, string worktree)
    {
        try
        {
            await _git.RunAsync(repoPath, ["worktree", "remove", "--force", worktree], CancellationToken.None);
            await _git.RunAsync(repoPath, ["worktree", "prune"], CancellationToken.None);
        }
        finally
        {
            try
            {
                if (Directory.Exists(worktree))
                {
                    Directory.Delete(worktree, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string BranchRef(string branch) => "refs/heads/" + branch;

    private static void RequireCommitId(string commitId)
    {
        if (!CommitIdPattern().IsMatch(commitId))
        {
            throw new InvalidOperationException($"'{commitId}' is not a full commit id.");
        }
    }

    private static bool IsManifest(string path)
    {
        var name = path.AsSpan(path.LastIndexOf('/') + 1);
        return name.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("^[0-9a-fA-F]{40}([0-9a-fA-F]{24})?$")]
    private static partial Regex CommitIdPattern();
}
