using MudBlazor;

namespace AutoRemediator.Web;

/// <summary>
/// GitHub-esque, emerald-accented MudBlazor theme (dark default, light alternate).
/// Emerald is the primary/success accent; amber/red/blue carry semantic state so the
/// UI reads at a glance without a single neon pop.
/// </summary>
public static class AppTheme
{
    public static readonly MudTheme Theme = new()
    {
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
            DrawerWidthLeft = "248px",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#3fb950",
            PrimaryContrastText = "#0d1117",
            Secondary = "#58a6ff",
            Tertiary = "#8b949e",
            Background = "#0d1117",
            BackgroundGray = "#010409",
            Surface = "#161b22",
            AppbarBackground = "#161b22",
            AppbarText = "#e6edf3",
            DrawerBackground = "#0f141b",
            DrawerText = "#c9d1d9",
            DrawerIcon = "#8b949e",
            TextPrimary = "#e6edf3",
            TextSecondary = "#8b949e",
            TextDisabled = "#6e7681",
            ActionDefault = "#8b949e",
            ActionDisabled = "#484f58",
            Divider = "#30363d",
            DividerLight = "#21262d",
            LinesDefault = "#30363d",
            LinesInputs = "#30363d",
            TableLines = "#21262d",
            TableStriped = "#0f141b",
            TableHover = "#1c2128",
            Success = "#3fb950",
            Error = "#f85149",
            Warning = "#d29922",
            Info = "#58a6ff",
            Dark = "#010409",
        },
        PaletteLight = new PaletteLight
        {
            Primary = "#1a7f37",
            PrimaryContrastText = "#ffffff",
            Secondary = "#0969da",
            Tertiary = "#59636e",
            Background = "#ffffff",
            BackgroundGray = "#f6f8fa",
            Surface = "#ffffff",
            AppbarBackground = "#f6f8fa",
            AppbarText = "#1f2328",
            DrawerBackground = "#f6f8fa",
            DrawerText = "#1f2328",
            DrawerIcon = "#59636e",
            TextPrimary = "#1f2328",
            TextSecondary = "#59636e",
            TextDisabled = "#818b96",
            ActionDefault = "#59636e",
            Divider = "#d0d7de",
            DividerLight = "#eaeef2",
            LinesDefault = "#d0d7de",
            LinesInputs = "#d0d7de",
            TableLines = "#eaeef2",
            TableHover = "#f6f8fa",
            Success = "#1a7f37",
            Error = "#cf222e",
            Warning = "#9a6700",
            Info = "#0969da",
        },
    };
}
