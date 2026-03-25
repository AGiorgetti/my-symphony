using MudBlazor;

namespace Symphony.Host.Theming;

internal static class MudThemeCatalog
{
    private static readonly string[] FontFamily =
    [
        "Bahnschrift",
        "Segoe UI Variable Text",
        "Trebuchet MS",
        "sans-serif"
    ];

    internal static MudTheme GetTheme(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "dark-blue" => CreateDarkTheme("#3b82f6"),
            "light-blue" => CreateLightTheme("#3b82f6"),
            _ => CreateDarkTheme("#fbbf24")
        };
    }

    private static MudTheme CreateDarkTheme(string primary)
    {
        return new MudTheme
        {
            PaletteDark = new PaletteDark
            {
                Primary = primary,
                Secondary = "#94a3b8",
                Background = "#0f172a",
                Surface = "#111827",
                AppbarBackground = "rgba(17, 24, 39, 0.94)",
                DrawerBackground = "#111827",
                DrawerText = "#f9fafb",
                TextPrimary = "#f9fafb",
                TextSecondary = "#cbd5e1",
                Success = "#10b981",
                Warning = "#f59e0b",
                Error = "#f87171",
                Info = "#60a5fa",
                LinesDefault = "rgba(248, 250, 252, 0.12)"
            },
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "22px"
            },
            Typography = new Typography
            {
                Default = new DefaultTypography
                {
                    FontFamily = FontFamily
                },
                H1 = new H1Typography
                {
                    FontFamily = FontFamily
                },
                H2 = new H2Typography
                {
                    FontFamily = FontFamily
                },
                H3 = new H3Typography
                {
                    FontFamily = FontFamily
                },
                H4 = new H4Typography
                {
                    FontFamily = FontFamily
                },
                H5 = new H5Typography
                {
                    FontFamily = FontFamily
                },
                H6 = new H6Typography
                {
                    FontFamily = FontFamily
                },
                Button = new ButtonTypography
                {
                    FontFamily = FontFamily
                }
            }
        };
    }

    private static MudTheme CreateLightTheme(string primary)
    {
        return new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                Primary = primary,
                Secondary = "#475569",
                Background = "#eff6ff",
                Surface = "#ffffff",
                AppbarBackground = "rgba(255, 255, 255, 0.94)",
                DrawerBackground = "#ffffff",
                DrawerText = "#0f172a",
                TextPrimary = "#0f172a",
                TextSecondary = "#475569",
                Success = "#10b981",
                Warning = "#d97706",
                Error = "#ef4444",
                Info = "#2563eb",
                LinesDefault = "rgba(15, 23, 42, 0.12)"
            },
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "22px"
            },
            Typography = new Typography
            {
                Default = new DefaultTypography
                {
                    FontFamily = FontFamily
                },
                H1 = new H1Typography
                {
                    FontFamily = FontFamily
                },
                H2 = new H2Typography
                {
                    FontFamily = FontFamily
                },
                H3 = new H3Typography
                {
                    FontFamily = FontFamily
                },
                H4 = new H4Typography
                {
                    FontFamily = FontFamily
                },
                H5 = new H5Typography
                {
                    FontFamily = FontFamily
                },
                H6 = new H6Typography
                {
                    FontFamily = FontFamily
                },
                Button = new ButtonTypography
                {
                    FontFamily = FontFamily
                }
            }
        };
    }
}
