using MudBlazor;

namespace TodoList.Web.Client.Theme;

public static class AppTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#8B5CF6",
            Success = "#00FF9D",
            Error = "#EF4444",
            Warning = "#F59E0B",
            Background = "#0B0D10",
            Surface = "#161920",
            AppbarBackground = "#161920",
            DrawerBackground = "#161920",
            DrawerText = "#E2E8F0",
            TextPrimary = "#E2E8F0",
            TextSecondary = "#8492A6",
            ActionDefault = "#8492A6",
            Divider = "#1F242D",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#8B5CF6",
            Success = "#00FF9D",
            Error = "#EF4444",
            Warning = "#F59E0B",
            Background = "#0B0D10",
            Surface = "#161920",
            AppbarBackground = "#161920",
            DrawerBackground = "#161920",
            DrawerText = "#E2E8F0",
            TextPrimary = "#E2E8F0",
            TextSecondary = "#8492A6",
            ActionDefault = "#8492A6",
            Divider = "#1F242D",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["IBM Plex Sans", "sans-serif"]
            },
            H1 = new H1Typography { FontFamily = ["Space Grotesk", "sans-serif"] },
            H2 = new H2Typography { FontFamily = ["Space Grotesk", "sans-serif"] },
            H3 = new H3Typography { FontFamily = ["Space Grotesk", "sans-serif"] },
            H4 = new H4Typography { FontFamily = ["Space Grotesk", "sans-serif"] },
            H5 = new H5Typography { FontFamily = ["Space Grotesk", "sans-serif"] },
            H6 = new H6Typography { FontFamily = ["Space Grotesk", "sans-serif"] },
        },
        Shape = new Shape
        {
            BorderRadius = 4
        }
    };
}
