using MudBlazor;

namespace SIA.Web.Theme;

/// <summary>
/// Tema MudBlazor terpusat SIA (mode terang saja). Dipasang di <c>MudThemeProvider</c>
/// pada MainLayout dan EmptyLayout.
/// </summary>
public static class SiaTheme
{
    public static MudTheme Theme { get; } = Create();

    private static MudTheme Create() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = SiaColors.Black,
            PrimaryContrastText = SiaColors.GreenAccent,
            Secondary = SiaColors.GreenStrong,
            SecondaryContrastText = SiaColors.Black,
            Tertiary = SiaColors.GreenAccent,
            TertiaryContrastText = SiaColors.Black,
            Info = SiaColors.Black,
            InfoContrastText = SiaColors.White,
            Dark = SiaColors.Black,
            DarkContrastText = SiaColors.White,

            Success = SiaColors.StatusSuccess,
            SuccessContrastText = SiaColors.White,
            Warning = SiaColors.StatusWarning,
            WarningContrastText = SiaColors.Black,
            Error = SiaColors.StatusError,
            ErrorContrastText = SiaColors.White,

            Black = SiaColors.Black,
            White = SiaColors.White,

            Background = SiaColors.Green,
            BackgroundGray = SiaColors.GreenSoft,
            Surface = SiaColors.White,

            AppbarBackground = SiaColors.Black,
            AppbarText = SiaColors.White,
            DrawerBackground = SiaColors.Black,
            DrawerText = SiaColors.White,
            DrawerIcon = SiaColors.GreenAccent,

            TextPrimary = SiaColors.Black,
            TextSecondary = SiaColors.TextSecondary,
            ActionDefault = SiaColors.Black,

            Divider = SiaColors.GreenSoft,
            DividerLight = SiaColors.GreenSoft,
            LinesDefault = SiaColors.GreenSoft,
            LinesInputs = SiaColors.TextSecondary,
            TableLines = SiaColors.GreenSoft,
            TableStriped = SiaColors.GreenSoft,
            TableHover = SiaColors.GreenSoft,
            Skeleton = SiaColors.GreenSoft,
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = new[] { "Inter", "Segoe UI", "Roboto", "Helvetica", "Arial", "sans-serif" },
            },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "10px",
            DrawerWidthLeft = "240px",
        },
    };
}
