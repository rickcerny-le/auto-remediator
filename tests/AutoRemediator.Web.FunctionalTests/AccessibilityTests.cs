using System.Text.Json;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace AutoRemediator.Web.FunctionalTests;

/// <summary>
/// axe-core (WCAG 2.0/2.1/2.2 A and AA) over every route in both colour schemes, plus the mobile
/// drawer. Requires a running Web app; set WEB_BASE_URL to run it, otherwise it is skipped so a
/// plain `dotnet test` (no app running) stays green. axe covers only the machine-checkable part of
/// WCAG: the manual checklist in the README still applies.
/// </summary>
public class AccessibilityTests
{
    private static readonly string[] Tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

    public static TheoryData<string, string, string> Routes()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var scheme in new[] { "light", "dark" })
        {
            // (name, path, selector that proves the page has rendered). "{run}" is replaced by a real run id.
            data.Add("overview", "/", scheme);
            data.Add("runs", "/runs", scheme);
            data.Add("run-detail", "/runs/{run}", scheme);
            data.Add("dependency-map", "/dependency-map", scheme);
            data.Add("configuration", "/configuration", scheme);
            data.Add("not-found", "/no/such/page", scheme);
        }

        return data;
    }

    private static string? BaseUrl => Environment.GetEnvironmentVariable("WEB_BASE_URL");

    private static async Task<string> FirstRunIdAsync(IPage page, string baseUrl)
    {
        var response = await page.APIRequest.GetAsync($"{baseUrl.TrimEnd('/')}/api/runs");
        if (response.Ok)
        {
            using var doc = JsonDocument.Parse(await response.BodyAsync());
            foreach (var run in doc.RootElement.EnumerateArray())
            {
                return run.GetProperty("id").GetString()!;
            }
        }

        // No run exists: the unknown-run page still has to be accessible.
        return Guid.NewGuid().ToString();
    }

    private static async Task AssertNoViolationsAsync(IPage page, string what)
    {
        var results = await page.RunAxe(new AxeRunOptions { RunOnly = new RunOnlyOptions { Type = "tag", Values = [.. Tags] } });
        var lines = results.Violations.Select(v =>
            $"  {v.Id} ({v.Impact}): {v.Help}{Environment.NewLine}" +
            string.Join(Environment.NewLine, v.Nodes.Select(n => $"    - {n.Target}")));
        Assert.True(
            results.Violations.Length == 0,
            $"axe found {results.Violations.Length} violation(s) on {what}:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");
    }

    /// <summary>Waits for running transitions/animations to finish so axe does not sample a mid-transition colour.</summary>
    private static Task SettleAsync(IPage page) =>
        page.EvaluateAsync("Promise.all(document.getAnimations().filter(a => a.effect.getTiming().iterations !== Infinity).map(a => a.finished.catch(() => {})))");

    private static async Task WaitForPageAsync(IPage page)
    {
        await Assertions.Expect(page.Locator("main h1").First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Loading…")).ToHaveCountAsync(0);
                await SettleAsync(page);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Page_has_no_wcag_a_or_aa_violations(string name, string path, string scheme)
    {
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(BaseUrl),
            "Set WEB_BASE_URL to a running Web app to run the accessibility tests.");
        var baseUrl = BaseUrl!.TrimEnd('/');

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
        });
        var page = await context.NewPageAsync();

        if (path.Contains("{run}"))
        {
            path = path.Replace("{run}", await FirstRunIdAsync(page, baseUrl));
        }

        await page.GotoAsync(baseUrl + path);
        await WaitForPageAsync(page);

        await AssertNoViolationsAsync(page, $"{name} ({scheme})");
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task Run_detail_with_pull_request_diff_has_no_wcag_a_or_aa_violations(string scheme)
    {
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(BaseUrl),
            "Set WEB_BASE_URL to a running Web app to run the accessibility tests.");
        var baseUrl = BaseUrl!.TrimEnd('/');

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
        });
        var page = await context.NewPageAsync();

        var runId = await PullRequestDiffTests.FindRunWithDiffAsync(page, baseUrl);
        Assert.SkipWhen(runId is null, "No run has a pull request diff (needs local repository mode and a Completed run).");

        await page.GotoAsync($"{baseUrl}/runs/{runId}");
        await WaitForPageAsync(page);

        // The diff loads separately from the run, with its own text, so WaitForPageAsync does not cover it.
        var section = page.GetByTestId("run-pr-diff");
        await Assertions.Expect(section).ToBeVisibleAsync();
        await Assertions.Expect(section.GetByText("Loading pull request changes…")).ToHaveCountAsync(0);
        await Assertions.Expect(section.GetByTestId("pr-diff-file").First).ToBeVisibleAsync();
        await SettleAsync(page);

        await AssertNoViolationsAsync(page, $"run-detail-pr-diff ({scheme})");
    }

    [Fact]
    public async Task Mobile_navigation_drawer_has_no_wcag_a_or_aa_violations()
    {
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(BaseUrl),
            "Set WEB_BASE_URL to a running Web app to run the accessibility tests.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 375, Height = 800 },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(BaseUrl!);
        await WaitForPageAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Toggle navigation" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Dialog)).ToBeVisibleAsync();
        await SettleAsync(page);

        await AssertNoViolationsAsync(page, "the mobile navigation drawer (375px)");
    }
}
