using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace AutoRemediator.Web.FunctionalTests;

/// <summary>
/// Playwright coverage of the repository detail page: editing the repository and its per-repository
/// settings, the effective-settings view with its sources, server validation messages, and the
/// inheritance switch. Requires a running Web app (and its API); set WEB_BASE_URL to run it,
/// otherwise it is skipped. Each test works on a throw-away, disabled repository that it deletes
/// afterwards, so no scenario repository or its settings are touched and the scheduler ignores it.
/// </summary>
public class RepositorySettingsTests
{
    private static string? BaseUrl => Environment.GetEnvironmentVariable("WEB_BASE_URL");

    private static void SkipWithoutApp() =>
        Assert.SkipWhen(string.IsNullOrWhiteSpace(BaseUrl), "Set WEB_BASE_URL to a running Web app to run the repository settings tests.");

    private static HttpClient NewHttp() => new(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
    {
        BaseAddress = new Uri(BaseUrl!.TrimEnd('/') + "/"),
    };

    /// <summary>Creates a disabled, inheriting repository and deletes it on dispose.</summary>
    private sealed class TempRepository : IAsyncDisposable
    {
        private readonly HttpClient _http;

        private TempRepository(HttpClient http, string id, string name)
        {
            _http = http;
            Id = id;
            Name = name;
        }

        public string Id { get; }

        public string Name { get; }

        public static async Task<TempRepository> CreateAsync(HttpClient http)
        {
            var name = $"settings-test-{Guid.NewGuid():N}"[..22];
            var response = await http.PostAsJsonAsync("api/repositories", new JsonObject
            {
                ["organization"] = "functional-tests",
                ["project"] = "settings",
                ["name"] = name,
                ["enabled"] = false,
                ["targetBranch"] = "main",
            });
            response.EnsureSuccessStatusCode();
            var created = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            return new TempRepository(http, created["id"]!.GetValue<string>(), name);
        }

        public async Task<JsonObject> GetAsync() =>
            (await _http.GetFromJsonAsync<JsonObject>($"api/repositories/{Id}"))!;

        public async ValueTask DisposeAsync() => await _http.DeleteAsync($"api/repositories/{Id}");
    }

    private static async Task<(IPlaywright, IBrowser, IPage)> OpenAsync(string path, ColorScheme scheme = ColorScheme.Light)
    {
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync();
        var page = await (await browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ColorScheme = scheme,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
        })).NewPageAsync();
        await page.GotoAsync($"{BaseUrl!.TrimEnd('/')}{path}");
        await Assertions.Expect(page.Locator("main h1").First).ToBeVisibleAsync();
        return (playwright, browser, page);
    }

    private static async Task SaveSettingsAsync(IPage page, string expectedStatus)
    {
        await page.GetByTestId("save-repo-settings").ClickAsync();
        await Assertions.Expect(page.GetByTestId("repo-status")).ToContainTextAsync(expectedStatus);
    }

    [Fact]
    public async Task A_repository_only_pattern_round_trips_and_shows_source_Repo_in_the_effective_table()
    {
        SkipWithoutApp();
        using var http = NewHttp();
        await using var repo = await TempRepository.CreateAsync(http);
        var (playwright, browser, page) = await OpenAsync($"/configuration/repositories/{repo.Id}");
        using var _ = playwright;
        await using var __ = browser;

        await Assertions.Expect(page.Locator("main h1").First).ToHaveTextAsync(repo.Name);
        await Assertions.Expect(page.GetByTestId("inherit-global")).ToBeCheckedAsync();

        await page.GetByTestId("repo-patterns").FillAsync("Func.Only.*");
        await SaveSettingsAsync(page, "Settings saved.");

        var item = page.GetByTestId("effective-item-patterns").Filter(new() { HasText = "Func.Only.*" });
        await Assertions.Expect(item).ToBeVisibleAsync();
        await Assertions.Expect(item.GetByTestId("item-source")).ToHaveTextAsync("Repo");

        // The settings survive a reload (they are stored, not just held in the form).
        await page.ReloadAsync();
        await Assertions.Expect(page.GetByTestId("repo-patterns")).ToHaveValueAsync("Func.Only.*");
        var stored = (await repo.GetAsync())["settings"]!.AsObject();
        Assert.Equal("Func.Only.*", stored["patterns"]!.AsArray().Single()!.GetValue<string>());
    }

    [Fact]
    public async Task Editing_the_branch_keeps_the_settings_and_saving_settings_keeps_the_branch()
    {
        SkipWithoutApp();
        using var http = NewHttp();
        await using var repo = await TempRepository.CreateAsync(http);
        var (playwright, browser, page) = await OpenAsync($"/configuration/repositories/{repo.Id}");
        using var _ = playwright;
        await using var __ = browser;

        await page.GetByTestId("repo-patterns").FillAsync("Keep.Me.*");
        await page.GetByTestId("repo-strategy").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "Patch", Exact = true }).ClickAsync();
        await SaveSettingsAsync(page, "Settings saved.");

        await page.GetByTestId("repo-branch").FillAsync("release");
        await page.GetByTestId("save-repo").ClickAsync();
        await Assertions.Expect(page.GetByTestId("repo-status")).ToContainTextAsync("Repository saved.");
        var afterBranch = await repo.GetAsync();
        Assert.Equal("release", afterBranch["targetBranch"]!.GetValue<string>());
        Assert.Equal("Keep.Me.*", afterBranch["settings"]!["patterns"]!.AsArray().Single()!.GetValue<string>());
        Assert.Equal("Patch", afterBranch["settings"]!["strategy"]!.GetValue<string>());

        await page.GetByTestId("repo-patterns").FillAsync("Keep.Me.*, Also.Me.*");
        await SaveSettingsAsync(page, "Settings saved.");
        var afterSettings = await repo.GetAsync();
        Assert.Equal("release", afterSettings["targetBranch"]!.GetValue<string>());
        Assert.Equal(2, afterSettings["settings"]!["patterns"]!.AsArray().Count);
    }

    [Fact]
    public async Task Turning_inheritance_off_with_no_patterns_shows_the_explanation_and_the_warning()
    {
        SkipWithoutApp();
        using var http = NewHttp();
        await using var repo = await TempRepository.CreateAsync(http);
        var (playwright, browser, page) = await OpenAsync($"/configuration/repositories/{repo.Id}");
        using var _ = playwright;
        await using var __ = browser;

        await page.GetByTestId("inherit-global").ClickAsync();
        await Assertions.Expect(page.GetByTestId("inherit-global")).Not.ToBeCheckedAsync();
        await Assertions.Expect(page.GetByTestId("inherit-off-note")).ToContainTextAsync("This repository uses only the values below.");
        await SaveSettingsAsync(page, "Settings saved.");

        var warnings = page.GetByTestId("effective-warnings");
        await Assertions.Expect(warnings).ToBeVisibleAsync();
        await Assertions.Expect(warnings).ToContainTextAsync("No package patterns");
        // With inheritance off, an unset strategy falls back to the built-in default.
        await Assertions.Expect(page.GetByTestId("effective-value-strategy")).ToHaveTextAsync("Minor");
        await Assertions.Expect(page.GetByTestId("effective-source-strategy")).ToHaveTextAsync("Default");

        // Turning inheritance back on hides the explanation again.
        await page.GetByTestId("inherit-global").ClickAsync();
        await SaveSettingsAsync(page, "Settings saved.");
        await Assertions.Expect(page.GetByTestId("inherit-off-note")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task An_invalid_cron_expression_shows_a_field_error_tied_to_the_input()
    {
        SkipWithoutApp();
        using var http = NewHttp();
        await using var repo = await TempRepository.CreateAsync(http);
        var (playwright, browser, page) = await OpenAsync($"/configuration/repositories/{repo.Id}");
        using var _ = playwright;
        await using var __ = browser;

        var schedule = page.GetByTestId("repo-schedule");
        await schedule.FillAsync("* * * * * *");
        await SaveSettingsAsync(page, "Settings were not saved.");

        var error = page.GetByTestId("error-schedule");
        await Assertions.Expect(error).ToBeVisibleAsync();
        await Assertions.Expect(schedule).ToHaveAttributeAsync("aria-invalid", "true");
        var errorId = await error.GetAttributeAsync("id");
        Assert.False(string.IsNullOrEmpty(errorId));
        Assert.Contains(errorId!, (await schedule.GetAttributeAsync("aria-describedby"))!.Split(' '));
        await Assertions.Expect(page.GetByTestId("repo-settings-errors")).ToContainTextAsync("Schedule");

        // Nothing was stored.
        Assert.True((await repo.GetAsync())["settings"]!["schedule"] is null);

        await AccessibilityTests.AssertNoViolationsAsync(page, "the repository page with a validation error");

        // Correcting it clears the error.
        await schedule.FillAsync("0 3 * * *");
        await SaveSettingsAsync(page, "Settings saved.");
        await Assertions.Expect(page.GetByTestId("error-schedule")).ToHaveCountAsync(0);
        await Assertions.Expect(schedule).Not.ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(page.GetByTestId("effective-value-schedule")).ToHaveTextAsync("0 3 * * *");
        await Assertions.Expect(page.GetByTestId("effective-source-schedule")).ToHaveTextAsync("Repo");
    }

    [Fact]
    public async Task The_effective_view_shows_the_built_in_default_schedule_when_none_is_set()
    {
        SkipWithoutApp();
        using var http = NewHttp();
        var global = (await http.GetFromJsonAsync<JsonObject>("api/settings"))!;
        Assert.SkipWhen(!string.IsNullOrWhiteSpace(global["defaultSchedule"]?.GetValue<string>()), "A global default schedule is set.");
        await using var repo = await TempRepository.CreateAsync(http);
        var (playwright, browser, page) = await OpenAsync($"/configuration/repositories/{repo.Id}");
        using var _ = playwright;
        await using var __ = browser;

        await Assertions.Expect(page.GetByRole(AriaRole.Table, new() { Name = "Effective settings" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("effective-value-schedule")).ToHaveTextAsync("0 0 * * *");
        await Assertions.Expect(page.GetByTestId("effective-source-schedule")).ToHaveTextAsync("Default");
        await Assertions.Expect(page.GetByTestId("repo-schedule")).ToHaveAttributeAsync("placeholder", "default: 0 0 * * *");
    }

    [Fact]
    public async Task The_global_default_schedule_shows_a_field_error_for_an_invalid_cron_expression()
    {
        SkipWithoutApp();
        var (playwright, browser, page) = await OpenAsync("/configuration");
        using var _ = playwright;
        await using var __ = browser;

        // The form is filled from /api/settings when it arrives; saving earlier would send empty fields.
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var field = page.GetByTestId("default-schedule");
        await Assertions.Expect(field).ToHaveAttributeAsync("placeholder", "0 0 * * *");
        await field.FillAsync("* * * * * *");
        await page.GetByTestId("save-settings").ClickAsync();

        await Assertions.Expect(page.GetByTestId("error-defaultSchedule")).ToBeVisibleAsync();
        await Assertions.Expect(field).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(page.GetByTestId("config-status")).ToContainTextAsync("not saved");
    }

    [Fact]
    public async Task Run_now_on_a_disabled_repository_page_is_offered_with_the_not_scheduled_note()
    {
        SkipWithoutApp();
        using var http = NewHttp();
        var repos = (await http.GetFromJsonAsync<JsonArray>("api/repositories"))!.Select(n => n!.AsObject()).ToList();
        var disabled = repos.FirstOrDefault(r => r["name"]!.GetValue<string>() == "disabled-repo");
        Assert.SkipWhen(disabled is null, "No 'disabled-repo' scenario repository is configured (needs local repository mode).");
        Assert.False(disabled!["enabled"]!.GetValue<bool>());

        var (playwright, browser, page) = await OpenAsync($"/configuration/repositories/{disabled["id"]!.GetValue<string>()}");
        using var _ = playwright;
        await using var __ = browser;

        var note = page.GetByTestId("repo-not-scheduled");
        await Assertions.Expect(note).ToHaveTextAsync("Disabled: not scheduled. Run now still works.");
        var button = page.GetByTestId("run-now");
        await Assertions.Expect(button).ToBeEnabledAsync();
        var describedBy = await button.GetAttributeAsync("aria-describedby");
        Assert.False(string.IsNullOrEmpty(describedBy));
        await Assertions.Expect(page.Locator($"#{describedBy}")).ToHaveTextAsync("Disabled: not scheduled. Run now still works.");

        await button.ClickAsync();
        var status = page.GetByTestId("repo-status");
        await Assertions.Expect(status).ToContainTextAsync("Run queued for disabled-repo.");
        await Assertions.Expect(status).ToContainTextAsync("will not run on schedule");
    }

    [Fact]
    public async Task An_unknown_repository_shows_a_not_found_message()
    {
        SkipWithoutApp();
        var (playwright, browser, page) = await OpenAsync($"/configuration/repositories/{Guid.NewGuid()}");
        using var _ = playwright;
        await using var __ = browser;

        await Assertions.Expect(page.GetByTestId("repo-error")).ToHaveTextAsync("Repository not found.");
    }
}
