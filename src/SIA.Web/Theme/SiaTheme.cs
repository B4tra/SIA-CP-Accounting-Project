using MudBlazor;

namespace SIA.Web.Theme;

/// <summary>
/// Konfigurasi tema terpusat MudBlazor dengan proporsi:
/// 50% hijau muda, 30% hitam, 20% putih.
/// Hanya mode terang (dark mode dinonaktifkan).
/// </summary>
public static class SiaTheme
{
    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            // Hijau muda (50%)
            Background = SiaColorTokens.GreenBackground,
            Divider = SiaColorTokens.GreenHover,
            TableHover = SiaColorTokens.GreenHover,
            LinesDefault = SiaColorTokens.GreenHover,
            LinesInputs = SiaColorTokens.GreenHover,

            // Hitam (30%)
            AppbarBackground = SiaColorTokens.BlackAppbar,
            DrawerBackground = SiaColorTokens.BlackDrawer,
            TextPrimary = SiaColorTokens.BlackTextPrimary,
            TextSecondary = SiaColorTokens.BlackTextSecondary,
            Primary = SiaColorTokens.BlackPrimary,
            PrimaryContrastText = SiaColorTokens.GreenAccentLight,

            // Putih (20%)
            Surface = SiaColorTokens.White,
            AppbarText = SiaColorTokens.White,
            DrawerText = SiaColorTokens.White,
            DrawerIcon = SiaColorTokens.GreenAccentLight,

            // Tombol & Aksi
            Secondary = SiaColorTokens.GreenAction,
            SecondaryContrastText = SiaColorTokens.BlackPrimary,

            // Status kecil (maks 3%)
            Error = SiaColorTokens.StatusError,
            Warning = SiaColorTokens.StatusWarning,
            Success = SiaColorTokens.StatusSuccess,
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
            AppbarHeight = "64px"
        }
    };

    /// <summary>
    /// Warna seri grafik tren: Penjualan (#0B0B0B), Laba (#4ADE80)
    /// </summary>
    public static readonly string[] ChartPalette =
    [
        SiaColorTokens.ChartSales,
        SiaColorTokens.ChartProfit
    ];
}
