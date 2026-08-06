using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoRemediator.Domain;

namespace AutoRemediator.Infrastructure.AzureDevOps;

/// <summary>
/// Read-only Azure DevOps REST client (api-version=7.1). The <see cref="HttpClient"/> is
/// configured with the organization base address and PAT Basic auth by the DI registration.
/// </summary>
internal sealed class AzureDevOpsClient(HttpClient httpClient) : IAzureDevOpsClient
{
    private const string ApiVersion = "api-version=7.1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> RepositoryExistsAsync(ManagedRepository repository, CancellationToken cancellationToken = default)
    {
        var url = $"{Uri.EscapeDataString(repository.Project)}/_apis/git/repositories/{Uri.EscapeDataString(repository.Name)}?{ApiVersion}";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        return response.StatusCode == HttpStatusCode.OK;
    }

    public async Task<IReadOnlyList<RepositoryFile>> GetManifestsAsync(ManagedRepository repository, CancellationToken cancellationToken = default)
    {
        var paths = await ListManifestPathsAsync(repository, cancellationToken);

        var files = new List<RepositoryFile>();
        foreach (var path in paths)
        {
            var content = await GetItemContentAsync(repository, path, cancellationToken);
            if (content is not null)
            {
                files.Add(new RepositoryFile(path, content));
            }
        }

        return files;
    }

    public async Task<string?> GetBranchHeadAsync(ManagedRepository repository, string branch, CancellationToken cancellationToken = default)
    {
        var url = $"{RepoBase(repository)}/refs?filter={Uri.EscapeDataString("heads/" + branch)}&{ApiVersion}";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<RefsResponse>(stream, JsonOptions, cancellationToken);
        var fullName = "refs/heads/" + branch;
        return payload?.Value?.FirstOrDefault(r => string.Equals(r.Name, fullName, StringComparison.OrdinalIgnoreCase))?.ObjectId;
    }

    public async Task<Stream> GetRepositoryArchiveAsync(
        ManagedRepository repository,
        string commitId,
        CancellationToken cancellationToken = default)
    {
        var url = $"{RepoBase(repository)}/items?scopePath=/&recursionLevel=Full" +
                  $"&versionDescriptor.version={Uri.EscapeDataString(commitId)}&versionDescriptor.versionType=commit" +
                  $"&$format=zip&download=true&{ApiVersion}";

        // Buffered rather than streamed: the response must be disposed with the request, and the
        // caller extracts from a seekable stream.
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var archive = new MemoryStream();
        await response.Content.CopyToAsync(archive, cancellationToken);
        archive.Position = 0;
        return archive;
    }

    public async Task PushFilesAsync(
        ManagedRepository repository,
        string branch,
        string baseCommitId,
        IReadOnlyList<FileChange> changes,
        string message,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            refUpdates = new[] { new { name = "refs/heads/" + branch, oldObjectId = baseCommitId } },
            commits = new[]
            {
                new
                {
                    comment = message,
                    changes = changes.Select(c => new
                    {
                        changeType = "edit",
                        item = new { path = c.Path },
                        newContent = new { content = c.Content, contentType = "rawtext" },
                    }).ToArray(),
                },
            },
        };

        using var response = await httpClient.PostAsJsonAsync($"{RepoBase(repository)}/pushes?{ApiVersion}", body, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> EnsurePullRequestAsync(
        ManagedRepository repository,
        string sourceBranch,
        string targetBranch,
        string title,
        string description,
        CancellationToken cancellationToken = default)
    {
        var source = "refs/heads/" + sourceBranch;
        var target = "refs/heads/" + targetBranch;

        var query = $"{RepoBase(repository)}/pullrequests?searchCriteria.status=active" +
                    $"&searchCriteria.sourceRefName={Uri.EscapeDataString(source)}" +
                    $"&searchCriteria.targetRefName={Uri.EscapeDataString(target)}&{ApiVersion}";

        using (var existing = await httpClient.GetAsync(query, cancellationToken))
        {
            if (existing.IsSuccessStatusCode)
            {
                await using var stream = await existing.Content.ReadAsStreamAsync(cancellationToken);
                var list = await JsonSerializer.DeserializeAsync<PullRequestList>(stream, JsonOptions, cancellationToken);
                var active = list?.Value?.FirstOrDefault();
                if (active is not null)
                {
                    return BuildPullRequestUrl(repository, active.PullRequestId);
                }
            }
        }

        var body = new { sourceRefName = source, targetRefName = target, title, description };
        using var created = await httpClient.PostAsJsonAsync($"{RepoBase(repository)}/pullrequests?{ApiVersion}", body, JsonOptions, cancellationToken);
        created.EnsureSuccessStatusCode();

        await using var createdStream = await created.Content.ReadAsStreamAsync(cancellationToken);
        var pr = await JsonSerializer.DeserializeAsync<PullRequest>(createdStream, JsonOptions, cancellationToken);
        return BuildPullRequestUrl(repository, pr?.PullRequestId ?? 0);
    }

    private static string RepoBase(ManagedRepository repository)
        => $"{Uri.EscapeDataString(repository.Project)}/_apis/git/repositories/{Uri.EscapeDataString(repository.Name)}";

    private string BuildPullRequestUrl(ManagedRepository repository, int pullRequestId)
        => httpClient.BaseAddress is null
            ? pullRequestId.ToString()
            : new Uri(httpClient.BaseAddress, $"{Uri.EscapeDataString(repository.Project)}/_git/{Uri.EscapeDataString(repository.Name)}/pullrequest/{pullRequestId}").ToString();

    private async Task<IReadOnlyList<string>> ListManifestPathsAsync(ManagedRepository repository, CancellationToken cancellationToken)
    {
        var basePath = $"{Uri.EscapeDataString(repository.Project)}/_apis/git/repositories/{Uri.EscapeDataString(repository.Name)}/items";
        var url = $"{basePath}?recursionLevel=Full&versionDescriptor.version={Uri.EscapeDataString(repository.TargetBranch)}&versionDescriptor.versionType=branch&{ApiVersion}";

        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<ItemsResponse>(stream, JsonOptions, cancellationToken);

        return (payload?.Value ?? [])
            .Where(i => i is { IsFolder: false, Path: not null } && IsManifest(i.Path))
            .Select(i => i.Path!)
            .ToList();
    }

    private async Task<string?> GetItemContentAsync(ManagedRepository repository, string path, CancellationToken cancellationToken)
    {
        var basePath = $"{Uri.EscapeDataString(repository.Project)}/_apis/git/repositories/{Uri.EscapeDataString(repository.Name)}/items";
        var url = $"{basePath}?path={Uri.EscapeDataString(path)}&includeContent=true&versionDescriptor.version={Uri.EscapeDataString(repository.TargetBranch)}&versionDescriptor.versionType=branch&{ApiVersion}";

        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var item = await JsonSerializer.DeserializeAsync<ItemContent>(stream, JsonOptions, cancellationToken);
        return item?.Content;
    }

    private static bool IsManifest(string path)
    {
        var name = path.AsSpan(path.LastIndexOf('/') + 1);
        return name.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ItemsResponse([property: JsonPropertyName("value")] List<Item>? Value);

    private sealed record Item(
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("isFolder")] bool IsFolder);

    private sealed record ItemContent([property: JsonPropertyName("content")] string? Content);

    private sealed record RefsResponse([property: JsonPropertyName("value")] List<GitRef>? Value);

    private sealed record GitRef(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("objectId")] string? ObjectId);

    private sealed record PullRequestList([property: JsonPropertyName("value")] List<PullRequest>? Value);

    private sealed record PullRequest([property: JsonPropertyName("pullRequestId")] int PullRequestId);
}
