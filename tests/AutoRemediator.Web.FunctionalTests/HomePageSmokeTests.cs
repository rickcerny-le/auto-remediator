using Microsoft.Playwright;

namespace AutoRemediator.Web.FunctionalTests;

/// <summary>
/// Playwright smoke test for the React web app (SPA). Requires a running Web application;
/// set WEB_BASE_URL to its address to run it, otherwise it is skipped so a plain
/// `dotnet test` (no app running) stays green. Set AR_SHOT_DIR to also save screenshots.
/// </summary>
public class HomePageSmokeTests
{
    [Fact]
    public async Task Shell_overview_and_runs_pages_render()
    {
        var baseUrl = Environment.GetEnvironmentVariable("WEB_BASE_URL");
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(baseUrl),
            "Set WEB_BASE_URL to a running Web app to run the functional smoke test.");

        var shotDir = Environment.GetEnvironmentVariable("AR_SHOT_DIR");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        });
        var page = await context.NewPageAsync();

        // Overview dashboard + app shell.
        await page.GotoAsync(baseUrl!);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Overview" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("AutoRemediator").First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Managed repos").First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("nav")).ToBeVisibleAsync();

        // Dark by default; the toggle switches to light.
        var html = page.Locator("html");
        await Assertions.Expect(html).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bdark\b"));
        await page.GetByTestId("theme-toggle").ClickAsync();
        await Assertions.Expect(html).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bdark\b"));
        await page.GetByTestId("theme-toggle").ClickAsync();
        if (shotDir is not null)
        {
            await page.ScreenshotAsync(new() { Path = Path.Combine(shotDir, "overview.png"), FullPage = true });
        }

        // Runs feed page.
        await page.GotoAsync($"{baseUrl!.TrimEnd('/')}/runs");
        await Assertions.Expect(page.GetByTestId("refresh-runs")).ToBeVisibleAsync();
        if (shotDir is not null)
        {
            await page.ScreenshotAsync(new() { Path = Path.Combine(shotDir, "runs.png"), FullPage = true });
        }
    }

    [Fact]
    public async Task A_deep_link_to_an_unknown_run_is_served_by_the_spa_and_reports_not_found()
    {
        var baseUrl = Environment.GetEnvironmentVariable("WEB_BASE_URL");
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(baseUrl),
            "Set WEB_BASE_URL to a running Web app to run the functional smoke test.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await (await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true })).NewPageAsync();

        // A client-side route loaded directly: the server must fall back to the SPA shell.
        await page.GotoAsync($"{baseUrl!.TrimEnd('/')}/runs/{Guid.NewGuid()}");
        await Assertions.Expect(page.GetByTestId("run-error")).ToContainTextAsync("Run not found.");
    }
}
