using System.Text.Json;
using Microsoft.Playwright;

namespace AutoRemediator.Web.FunctionalTests;

/// <summary>
/// The read-only "Pull request changes" panel on the run detail page (local repository mode). Needs a
/// running app composed with LocalRepos:Enabled and a run that has a pull request record; set WEB_BASE_URL
/// to run it. Skipped when the URL is unset or no run links a diff, so a plain `dotnet test` stays green.
/// </summary>
public class PullRequestDiffTests
{
    private static string? BaseUrl => Environment.GetEnvironmentVariable("WEB_BASE_URL");

    /// <summary>The id of the first run whose detail has a non-null <c>pullRequestDiffUrl</c>, or null.</summary>
    internal static async Task<string?> FindRunWithDiffAsync(IPage page, string baseUrl)
    {
        var list = await page.APIRequest.GetAsync($"{baseUrl.TrimEnd('/')}/api/runs");
        if (!list.Ok)
        {
            return null;
        }

        using var runs = JsonDocument.Parse(await list.BodyAsync());
        foreach (var run in runs.RootElement.EnumerateArray())
        {
            var id = run.GetProperty("id").GetString()!;
            var detail = await page.APIRequest.GetAsync($"{baseUrl.TrimEnd('/')}/api/runs/{id}");
            if (!detail.Ok)
            {
                continue;
            }

            using var doc = JsonDocument.Parse(await detail.BodyAsync());
            if (doc.RootElement.TryGetProperty("pullRequestDiffUrl", out var url) && url.ValueKind == JsonValueKind.String)
            {
                return id;
            }
        }

        return null;
    }

    [Fact]
    public async Task Run_detail_shows_the_pull_request_files_read_only()
    {
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(BaseUrl),
            "Set WEB_BASE_URL to a running Web app to run the pull request diff tests.");
        var baseUrl = BaseUrl!.TrimEnd('/');

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });
        var page = await context.NewPageAsync();

        var runId = await FindRunWithDiffAsync(page, baseUrl);
        Assert.SkipWhen(runId is null, "No run has a pull request diff (needs local repository mode and a Completed run).");

        await page.GotoAsync($"{baseUrl}/runs/{runId}");

        var section = page.GetByTestId("run-pr-diff");
        await Assertions.Expect(section.GetByRole(AriaRole.Heading, new() { Name = "Pull request changes" })).ToBeVisibleAsync();
        await Assertions.Expect(section.GetByText("Loading pull request changes…")).ToHaveCountAsync(0);

        var files = section.GetByTestId("pr-diff-file");
        await Assertions.Expect(files.First).ToBeVisibleAsync();
        await Assertions.Expect(files.First.GetByText("Before", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(files.First.GetByText("After", new() { Exact = true })).ToBeVisibleAsync();

        // Read-only: the only buttons are the accordion triggers.
        var buttons = await section.GetByRole(AriaRole.Button).CountAsync();
        var triggers = await section.Locator("[data-slot='accordion-trigger']").CountAsync();
        Assert.Equal(triggers, buttons);
    }
}
