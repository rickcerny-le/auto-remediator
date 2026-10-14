namespace LocalScenarioSetup;

/// <summary>Pure helpers for the enroll mode.</summary>
internal static class Enrollment
{
    public static readonly IReadOnlyList<string> Profiles = ["baseline", "patch", "major", "prerelease"];

    public const string NoApiUrlMessage =
        "No API URL was injected; enroll runs as the Aspire resource local-scenarios-enroll.";

    /// <summary>Returns an error message when the profile is not one of the valid names, else null.</summary>
    public static string? ValidateProfile(string? profile) =>
        profile is not null && Profiles.Contains(profile, StringComparer.Ordinal)
            ? null
            : $"LocalRepos__Profile '{profile}' is not valid; use baseline, patch, major or prerelease.";

    /// <summary>
    /// Picks the API base URL Aspire injected through WithReference(api): http first, then https.
    /// Never falls back to a fixed port.
    /// </summary>
    public static string? ResolveApiUrl(Func<string, string?> getEnvironmentVariable)
    {
        foreach (var name in new[] { "services__api__http__0", "services__api__https__0" })
        {
            var value = getEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim().TrimEnd('/');
            }
        }

        return null;
    }

    /// <summary>Polls GET {url}/health until it returns success, or the timeout passes. Returns the last error.</summary>
    public static async Task<string?> WaitForHealthyAsync(
        HttpClient client, string apiUrl, TimeSpan interval, TimeSpan timeout, Action<string> log)
    {
        var deadline = DateTime.UtcNow + timeout;
        string? lastError;
        while (true)
        {
            try
            {
                using var response = await client.GetAsync($"{apiUrl}/health");
                if (response.IsSuccessStatusCode)
                {
                    return null;
                }

                lastError = $"GET {apiUrl}/health returned {(int)response.StatusCode}";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = $"GET {apiUrl}/health failed: {ex.Message}";
            }

            if (DateTime.UtcNow + interval >= deadline)
            {
                return lastError;
            }

            log($"waiting for the API: {lastError}");
            await Task.Delay(interval);
        }
    }
}
