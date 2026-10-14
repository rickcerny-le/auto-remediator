using Microsoft.Playwright;

namespace AutoRemediator.Web.FunctionalTests;

/// <summary>
/// WCAG 2.4.7 / 2.4.11: every keyboard tab stop must show a visible focus indicator. axe cannot see
/// this (it does not compute outline-style), so this tabs through real pages in a real browser and
/// asserts the computed outline of each focused control. Requires WEB_BASE_URL, otherwise skipped.
/// </summary>
public class FocusVisibleTests
{
    private static string? BaseUrl => Environment.GetEnvironmentVariable("WEB_BASE_URL");

    public static TheoryData<string, bool, string> Cases()
    {
        var data = new TheoryData<string, bool, string>();
        foreach (var (scheme, forced) in new[] { ("light", false), ("dark", false), ("light", true) })
        {
            data.Add("/", forced, scheme);
            data.Add("/runs", forced, scheme);
            data.Add("/configuration", forced, scheme);
        }

        return data;
    }

    private sealed class Outline
    {
        public string Tag { get; set; } = "";
        public string Role { get; set; } = "";
        public string Name { get; set; } = "";
        public string Style { get; set; } = "";
        public double Width { get; set; }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Every_tab_stop_has_a_visible_focus_outline(string path, bool forcedColors, string scheme)
    {
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(BaseUrl),
            "Set WEB_BASE_URL to a running Web app to run the focus tests.");
        var baseUrl = BaseUrl!.TrimEnd('/');

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ForcedColors = forcedColors ? ForcedColors.Active : ForcedColors.None,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(baseUrl + path);
        await Assertions.Expect(page.Locator("main h1").First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Loading…")).ToHaveCountAsync(0);

        var seen = new List<string>();
        for (var i = 0; i < 40; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            var o = await page.EvaluateAsync<Outline?>(
                @"() => {
                    const el = document.activeElement;
                    if (!el || el === document.body || !el.matches(':focus-visible')) return null;
                    const s = getComputedStyle(el);
                    return { tag: el.tagName.toLowerCase(), name: (el.getAttribute('aria-label') || el.textContent || '').trim().slice(0, 40), role: el.getAttribute('role') ?? '', style: s.outlineStyle, width: parseFloat(s.outlineWidth) };
                }");
            if (o is null)
            {
                continue;
            }

            var label = $"{o.Tag}[{o.Role}] \"{o.Name}\" outline: {o.Style} {o.Width}px";
            seen.Add(label);
            Assert.True(o.Style != "none" && o.Style != "hidden" && o.Width >= 2, $"Focused control has no visible outline on {path}: {label}");
        }

        Assert.NotEmpty(seen);
        if (path == "/configuration")
        {
            Assert.Contains(seen, s => s.StartsWith("button[combobox]"));
        }

        Assert.Contains(seen, s => s.StartsWith("a["));
        Assert.Contains(seen, s => s.StartsWith("button["));
    }
}
