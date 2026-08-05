using Microsoft.Playwright;

namespace AutoRemediator.Web.FunctionalTests;

/// <summary>
/// Playwright smoke test for the MudBlazor Web app. Requires a running Web application;
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
        await Assertions.Expect(page.GetByText("Overview").First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("AutoRemediator").First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Managed repos").First).ToBeVisibleAsync();
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
}
