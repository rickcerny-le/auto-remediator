using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace AutoRemediator.Web.FunctionalTests;

/// <summary>
/// Playwright coverage of the per-repository enable switch and Run now button on the
/// Configuration page. Requires a running Web app (and its API); set WEB_BASE_URL to run it,
/// otherwise it is skipped so a plain `dotnet test` (no app running) stays green. Pressing Run now
/// queues a real run, and the tests restore any repository they change.
/// </summary>
public class RunNowTests
{
    private static string? BaseUrl => Environment.GetEnvironmentVariable("WEB_BASE_URL");

    private static void SkipWithoutApp() =>
        Assert.SkipWhen(string.IsNullOrWhiteSpace(BaseUrl), "Set WEB_BASE_URL to a running Web app to run the Run now tests.");

    private static HttpClient NewHttp() => new(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
    {
        BaseAddress = new Uri(BaseUrl!.TrimEnd('/') + "/"),
    };

    private static async Task<List<JsonObject>> RepositoriesAsync(HttpClient http) =>
        (await http.GetFromJsonAsync<JsonArray>("api/repositories"))!.Select(n => n!.AsObject()).ToList();

    private static async Task SetEnabledAsync(HttpClient http, JsonObject repo, bool enabled)
    {
        var input = new JsonObject
        {
            ["organization"] = repo["organization"]!.GetValue<string>(),
            ["project"] = repo["project"]!.GetValue<string>(),
            ["name"] = repo["name"]!.GetValue<string>(),
            ["enabled"] = enabled,
            ["targetBranch"] = repo["targetBranch"]!.GetValue<string>(),
        };
        var response = await http.PutAsJsonAsync($"api/repositories/{repo["id"]!.GetValue<string>()}", input);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<(IPlaywright, IBrowser, IPage)> OpenConfigurationAsync()
    {
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync();
        var page = await (await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true })).NewPageAsync();
        await page.GotoAsync($"{BaseUrl!.TrimEnd('/')}/configuration");
        await Assertions.Expect(page.GetByTestId("repos-table")).ToBeVisibleAsync();
        return (playwright, browser, page);
    }

    private static ILocator RunNowButton(IPage page, string name) =>
        page.GetByRole(AriaRole.Button, new() { Name = $"Run {name} now", Exact = true });

    private static ILocator EnableSwitch(IPage page, string name) =>
        page.GetByRole(AriaRole.Switch, new() { Name = $"Enable {name}", Exact = true });

    [Fact]
    public async Task Run_now_on_an_enabled_repository_shows_the_queued_status()
    {
        SkipWithoutApp();
        using var http = NewHttp();
        var repo = (await RepositoriesAsync(http)).FirstOrDefault(r => r["enabled"]!.GetValue<bool>());
        Assert.SkipWhen(repo is null, "No enabled repository is configured.");
        var name = repo!["name"]!.GetValue<string>();

        var (playwright, browser, page) = await OpenConfigurationAsync();
        using var _ = playwright;
        await using var __ = browser;

        await RunNowButton(page, name).ClickAsync();

        var status = page.GetByTestId("config-status");
        await Assertions.Expect(status).ToContainTextAsync($"Run queued for {name}.");
        await Assertions.Expect(status).Not.ToContainTextAsync("will not run on schedule");
        await Assertions.Expect(status.GetByRole(AriaRole.Link, new() { Name = "Runs" })).ToHaveAttributeAsync("href", "/runs");
    }

    [Fact]
    public async Task Toggling_the_switch_changes_the_stored_enabled_flag_and_back()
    {
        SkipWithoutApp();
        using var http = NewHttp();
        var repo = (await RepositoriesAsync(http)).FirstOrDefault(r => r["enabled"]!.GetValue<bool>());
        Assert.SkipWhen(repo is null, "No enabled repository is configured.");
        var id = repo!["id"]!.GetValue<string>();
        var name = repo["name"]!.GetValue<string>();

        var (playwright, browser, page) = await OpenConfigurationAsync();
        using var _ = playwright;
        await using var __ = browser;

        async Task<bool> StoredEnabledAsync() =>
            (await RepositoriesAsync(http)).Single(r => r["id"]!.GetValue<string>() == id)["enabled"]!.GetValue<bool>();

        try
        {
            var toggle = EnableSwitch(page, name);
            await Assertions.Expect(toggle).ToBeCheckedAsync();

            await toggle.ClickAsync();
            await Assertions.Expect(toggle).Not.ToBeCheckedAsync();
            Assert.False(await StoredEnabledAsync());
            // The switch and the visible text agree straight after the reload.
            await Assertions.Expect(page.GetByTestId("repo-not-scheduled")).ToHaveCountAsync(
                (await RepositoriesAsync(http)).Count(r => !r["enabled"]!.GetValue<bool>()));

            await toggle.ClickAsync();
            await Assertions.Expect(toggle).ToBeCheckedAsync();
            Assert.True(await StoredEnabledAsync());
        }
        finally
        {
            await SetEnabledAsync(http, repo, true);
        }
    }

    [Fact]
    public async Task Run_now_on_a_disabled_repository_is_offered_and_says_it_will_not_run_on_schedule()
    {
        SkipWithoutApp();
        using var http = NewHttp();
        var repo = (await RepositoriesAsync(http)).FirstOrDefault(r => r["enabled"]!.GetValue<bool>());
        Assert.SkipWhen(repo is null, "No enabled repository is configured.");
        var name = repo!["name"]!.GetValue<string>();

        await SetEnabledAsync(http, repo, false);
        try
        {
            var (playwright, browser, page) = await OpenConfigurationAsync();
            using var _ = playwright;
            await using var __ = browser;

            var button = RunNowButton(page, name);
            await Assertions.Expect(button).ToBeVisibleAsync();
            await Assertions.Expect(button).ToBeEnabledAsync();
            await Assertions.Expect(page.GetByTestId("repo-not-scheduled").First).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByTestId("repo-not-scheduled").First).ToHaveTextAsync("Disabled: not scheduled");
            // The visible note is the button's accessible description.
            var describedBy = await button.GetAttributeAsync("aria-describedby");
            Assert.False(string.IsNullOrEmpty(describedBy));
            await Assertions.Expect(page.Locator($"#{describedBy}")).ToHaveTextAsync("Disabled: not scheduled");

            await button.ClickAsync();

            var status = page.GetByTestId("config-status");
            await Assertions.Expect(status).ToContainTextAsync($"Run queued for {name}.");
            await Assertions.Expect(status).ToContainTextAsync($"{name} is disabled and will not run on schedule.");
            Assert.False((await RepositoriesAsync(http)).Single(r => r["id"]!.GetValue<string>() == repo["id"]!.GetValue<string>())["enabled"]!.GetValue<bool>());
        }
        finally
        {
            await SetEnabledAsync(http, repo, true);
        }
    }
}
