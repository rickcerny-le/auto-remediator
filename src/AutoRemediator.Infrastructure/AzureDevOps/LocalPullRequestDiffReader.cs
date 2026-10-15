using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Infrastructure.AzureDevOps;

/// <summary>
/// Computes a local pull request's diff from the record's base and head commits with the <c>git</c> binary.
/// Read-only: it never changes a repository or a record. Every failure (bad URL, bad record, missing commit)
/// gives null rather than an exception.
/// </summary>
internal sealed partial class LocalPullRequestDiffReader : IPullRequestDiffReader
{
    internal const int MaxSideBytes = 256 * 1024;
    internal const int MaxFiles = 200;
    private const int BinarySniffBytes = 8 * 1024;

    private static readonly JsonSerializerOptions RecordJson = new(JsonSerializerDefaults.Web);

    private readonly GitCli _git = new();
    private readonly string _root;
    private readonly string _pullRequestsRoot;
    private readonly ILogger<LocalPullRequestDiffReader> _logger;

    public LocalPullRequestDiffReader(IOptions<AzureDevOpsOptions> options, ILogger<LocalPullRequestDiffReader> logger)
    {
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.LocalRoot))
        {
            throw new InvalidOperationException("AzureDevOps:LocalRoot must be set when AzureDevOps:Mode is Local.");
        }

        _root = Path.GetFullPath(value.LocalRoot);
        _pullRequestsRoot = Path.GetFullPath(value.EffectiveLocalPullRequestsRoot!);
        _logger = logger;
    }

    public bool IsAvailable => true;

    public async Task<PullRequestDiff?> GetDiffAsync(string pullRequestUrl, CancellationToken cancellationToken)
    {
        var record = await ReadRecordAsync(pullRequestUrl, cancellationToken);
        if (record is null)
        {
            return null;
        }

        // Deserialization can leave non-nullable members null when the record is malformed.
        string? baseCommit = record.BaseCommit;
        string? headCommit = record.HeadCommit;
        if (baseCommit is null || headCommit is null
            || !CommitIdPattern().IsMatch(baseCommit) || !CommitIdPattern().IsMatch(headCommit))
        {
            return null;
        }

        var repoPath = await ResolveRepositoryAsync(record.Repository, cancellationToken);
        if (repoPath is null)
        {
            return null;
        }

        var verify = await Task.WhenAll(
            CommitExistsAsync(repoPath, baseCommit, cancellationToken),
            CommitExistsAsync(repoPath, headCommit, cancellationToken));
        if (verify.Any(exists => !exists))
        {
            _logger.LogWarning(
                "Pull request {Url} refers to commit(s) that no longer exist in {Repository}; the repository may have been regenerated.",
                pullRequestUrl,
                record.Repository);
            return null;
        }

        var listing = await _git.RunAsync(
            repoPath,
            ["diff", "--name-status", "-z", "--no-renames", baseCommit, headCommit],
            cancellationToken);
        if (!listing.Succeeded)
        {
            _logger.LogWarning("git diff failed for pull request {Url}: {Error}", pullRequestUrl, listing.Error.Trim());
            return null;
        }

        var entries = ParseNameStatus(listing.Text)
            .OrderBy(e => e.Path, StringComparer.Ordinal)
            .Take(MaxFiles)
            .ToList();

        var files = new List<PullRequestDiffFile>(entries.Count);
        foreach (var (kind, path) in entries)
        {
            var before = kind == PullRequestChangeKind.Added ? null : await ShowAsync(repoPath, baseCommit, path, cancellationToken);
            var after = kind == PullRequestChangeKind.Deleted ? null : await ShowAsync(repoPath, headCommit, path, cancellationToken);
            files.Add(ToFile(kind, path, before, after));
        }

        return new PullRequestDiff(record.SourceBranch, record.TargetBranch, baseCommit, headCommit, files);
    }

    private static PullRequestDiffFile ToFile(PullRequestChangeKind kind, string path, byte[]? before, byte[]? after)
    {
        var rooted = "/" + path;
        if (IsBinary(before) || IsBinary(after))
        {
            return new PullRequestDiffFile(rooted, kind, null, null, IsBinary: true, IsTruncated: false);
        }

        var truncated = false;
        return new PullRequestDiffFile(rooted, kind, Decode(before, ref truncated), Decode(after, ref truncated), false, truncated);
    }

    private static bool IsBinary(byte[]? bytes)
        => bytes is not null && Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, BinarySniffBytes)) >= 0;

    private static string? Decode(byte[]? bytes, ref bool truncated)
    {
        if (bytes is null)
        {
            return null;
        }

        if (bytes.Length > MaxSideBytes)
        {
            truncated = true;
            return Encoding.UTF8.GetString(bytes, 0, MaxSideBytes);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static IEnumerable<(PullRequestChangeKind Kind, string Path)> ParseNameStatus(string output)
    {
        // With -z and --no-renames the stream is "<status>\0<path>\0" repeated.
        var parts = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var kind = parts[i].Trim() switch
            {
                "A" => PullRequestChangeKind.Added,
                "D" => PullRequestChangeKind.Deleted,
                _ => PullRequestChangeKind.Modified,
            };
            yield return (kind, parts[i + 1]);
        }
    }

    private async Task<byte[]?> ShowAsync(string repoPath, string commit, string path, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(repoPath, ["show", $"{commit}:{path}"], cancellationToken);
        return result.Succeeded ? result.Bytes : null;
    }

    private async Task<bool> CommitExistsAsync(string repoPath, string commit, CancellationToken cancellationToken)
        => (await _git.RunAsync(repoPath, ["cat-file", "-e", commit + "^{commit}"], cancellationToken)).Succeeded;

    private async Task<LocalPullRequestRecord?> ReadRecordAsync(string pullRequestUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(pullRequestUrl, UriKind.Absolute, out var uri) || !uri.IsFile)
        {
            return null;
        }

        string path;
        try
        {
            path = Path.GetFullPath(uri.LocalPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var prefix = _pullRequestsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<LocalPullRequestRecord>(stream, RecordJson, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Ignoring unreadable local pull request record {File}.", path);
            return null;
        }
    }

    /// <summary>The repository folder when the name is safe and the folder is its own git repo; otherwise null.</summary>
    private async Task<string?> ResolveRepositoryAsync(string? name, CancellationToken cancellationToken)
    {
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

        var printed = gitDir.Text.Trim();
        if (printed is ".git" or ".")
        {
            return path;
        }

        var absolute = Path.GetFullPath(Path.Combine(path, printed));
        return absolute.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    [GeneratedRegex("^[0-9a-fA-F]{40}([0-9a-fA-F]{24})?$")]
    private static partial Regex CommitIdPattern();
}
