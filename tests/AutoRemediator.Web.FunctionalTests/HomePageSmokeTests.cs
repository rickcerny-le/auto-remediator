using Microsoft.Playwright;

namespace AutoRemediator.Web.FunctionalTests;

/// <summary>
/// Playwright smoke test for the Blazor Web App. Requires a running Web application;
/// set WEB_BASE_URL to its address (e.g. from the Aspire dashboard) to run it.
/// When WEB_BASE_URL is not set the test is skipped, so it does not run during a
/// plain `dotnet test` (which has no app running) but is available on demand.
/// </summary>
public class HomePageSmokeTests
{
    [Fact]
    public async Task Home_page_renders_heading_and_interactive_component()
    {
        var baseUrl = Environment.GetEnvironmentVariable("WEB_BASE_URL");
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(baseUrl),
            "Set WEB_BASE_URL to a running Web app to run the functional smoke test.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        // Ignore the local dev HTTPS certificate when hitting the Aspire-hosted app.
        var context = await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });
        var page = await context.NewPageAsync();

        await page.GotoAsync(baseUrl!);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Auto Dependency Remediator");
        await Assertions.Expect(page.GetByTestId("tagline")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("ping-button")).ToBeVisibleAsync();
    }
}
