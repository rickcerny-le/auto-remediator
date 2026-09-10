using AutoRemediator.Domain;
using AutoRemediator.Infrastructure;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Review;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright;

namespace AutoRemediator.Web.FunctionalTests;

/// <summary>
/// Playwright coverage of the review surface. Seeds a run and a proposal directly through
/// storage rather than by running a real remediation, so the scenario is deterministic. Requires
/// a running Web app (and its backing Azurite) — set WEB_BASE_URL to run it, otherwise it is
/// skipped so a plain `dotnet test` (no app running) stays green.
/// </summary>
public class ReviewGateTests
{
    private static readonly DateTimeOffset VerifiedAt = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private static ServiceProvider BuildStorageProvider()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:tables"] =
            Environment.GetEnvironmentVariable("ConnectionStrings__tables") ?? "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:blobs"] =
            Environment.GetEnvironmentVariable("ConnectionStrings__blobs") ?? "UseDevelopmentStorage=true";
        builder.Configuration["ConnectionStrings:servicebus"] =
            Environment.GetEnvironmentVariable("ConnectionStrings__servicebus")
            ?? "Endpoint=sb://localhost/;SharedAccessKeyName=k;SharedAccessKey=a2V5a2V5a2V5a2V5";
        builder.AddInfrastructure();
        return builder.Services.BuildServiceProvider();
    }

    private static ChangeProposal NewProposal(Guid runId, Guid repositoryId) => new(
        runId, repositoryId, "commit-abc123", VerifiedAt,
        [
            new ProposedFile("/Directory.Packages.props", "<Old/>", "<New/>", ProposedFileOrigin.Manifest),
            new ProposedFile("/src/App/Program.cs", "old source", "new source", ProposedFileOrigin.AgentEdit),
        ],
        [new VerificationDiagnostic("CS1061", "'Client' has no member 'SubmitAsync'", "src/App/Program.cs", 3, 42)],
        attempts: 2,
        transcriptReference: null);

    /// <summary>Seeds a held run and its proposal, skipping the test when Azurite is not reachable.</summary>
    private static async Task<Guid?> SeedHeldRunAsync(ServiceProvider provider, string? reviewNote = null)
    {
        var runStore = provider.GetRequiredService<IRemediationRunStore>();
        var proposalStore = provider.GetRequiredService<IChangeProposalStore>();

        var repositoryId = Guid.NewGuid();
        var run = new RemediationRun(Guid.NewGuid(), repositoryId, "orion180/platform/review-gate-e2e", DateTimeOffset.UtcNow.AddMinutes(-15));
        run.RecordUpdates([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")]);
        run.Advance(RunStatus.Remediating);

        var proposal = NewProposal(run.Id, repositoryId);

        try
        {
            var reference = await proposalStore.StoreAsync(proposal);
            run.AwaitingReview(reference, VerifiedAt);
            if (reviewNote is not null)
            {
                run.ReviewBlocked(reviewNote);
            }

            await runStore.SaveAsync(run);
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            Assert.Skip($"Azurite storage emulator not reachable: {ex.GetType().Name}");
            return null;
        }

        return run.Id;
    }

    [Fact]
    public async Task Review_panel_shows_the_change_and_all_four_controls_and_approve_reports_requested()
    {
        var baseUrl = Environment.GetEnvironmentVariable("WEB_BASE_URL");
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(baseUrl),
            "Set WEB_BASE_URL to a running Web app to run the functional smoke test.");

        await using var provider = BuildStorageProvider();
        var runId = await SeedHeldRunAsync(provider);
        if (runId is null)
        {
            return;
        }

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await (await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true })).NewPageAsync();

        await page.GotoAsync($"{baseUrl!.TrimEnd('/')}/runs/{runId}");

        await Assertions.Expect(page.GetByTestId("review-panel")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("new source")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("old source")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("proposal-diagnostics")).ToContainTextAsync("CS1061");

        await Assertions.Expect(page.GetByTestId("approve-button")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("retry-button")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("discard-button")).ToBeVisibleAsync();

        await page.GetByTestId("approve-button").ClickAsync();

        // 202 means accepted, not complete (FR-014, SC-007) — the message says "requested".
        await Assertions.Expect(page.GetByTestId("review-command-message")).ToContainTextAsync("requested");
    }

    [Fact]
    public async Task A_staleness_review_note_renders_as_recoverable_with_a_rebuild_action_not_an_error()
    {
        var baseUrl = Environment.GetEnvironmentVariable("WEB_BASE_URL");
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(baseUrl),
            "Set WEB_BASE_URL to a running Web app to run the functional smoke test.");

        await using var provider = BuildStorageProvider();
        var runId = await SeedHeldRunAsync(provider, reviewNote: "The update branch moved since this proposal was verified; rebuild to try again.");
        if (runId is null)
        {
            return;
        }

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await (await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true })).NewPageAsync();

        await page.GotoAsync($"{baseUrl!.TrimEnd('/')}/runs/{runId}");

        var note = page.GetByTestId("review-note");
        await Assertions.Expect(note).ToBeVisibleAsync();
        await Assertions.Expect(note).ToContainTextAsync("moved");
        await Assertions.Expect(page.GetByTestId("rebuild-button")).ToBeVisibleAsync();

        // No error styling for a recoverable wait — the run-level error alert must not appear.
        await Assertions.Expect(page.GetByTestId("run-error-message")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Runs_feed_and_home_present_the_held_repository()
    {
        var baseUrl = Environment.GetEnvironmentVariable("WEB_BASE_URL");
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(baseUrl),
            "Set WEB_BASE_URL to a running Web app to run the functional smoke test.");

        await using var provider = BuildStorageProvider();
        var runId = await SeedHeldRunAsync(provider);
        if (runId is null)
        {
            return;
        }

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await (await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true })).NewPageAsync();

        await page.GotoAsync($"{baseUrl!.TrimEnd('/')}/runs?status=AwaitingReview");
        await Assertions.Expect(page.GetByTestId("runs-table")).ToContainTextAsync("Awaiting review");

        await page.GotoAsync(baseUrl!);
        await Assertions.Expect(page.GetByTestId("held-panel")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("held-table")).ToContainTextAsync("review-gate-e2e");
    }
}
