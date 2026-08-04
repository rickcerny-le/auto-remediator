using System.Net;
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
}
