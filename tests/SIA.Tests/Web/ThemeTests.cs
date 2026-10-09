using MudBlazor;
using MudBlazor.Utilities;
using SIA.Web.Theme;

namespace SIA.Tests.Web;

public class ThemeTests
{
    [Fact]
    public void SiaColorTokens_ShouldMatchRequiredColorPalette()
    {
        // 50% Hijau muda
        Assert.Equal("#BBF7D0", SiaColorTokens.GreenBackground);
        Assert.Equal("#86EFAC", SiaColorTokens.GreenAccentLight);
        Assert.Equal("#4ADE80", SiaColorTokens.GreenAction);
        Assert.Equal("#DCFCE7", SiaColorTokens.GreenHover);

        // 30% Hitam
        Assert.Equal("#0B0B0B", SiaColorTokens.BlackPrimary);
        Assert.Equal("#0B0B0B", SiaColorTokens.BlackAppbar);
        Assert.Equal("#0B0B0B", SiaColorTokens.BlackDrawer);
        Assert.Equal("#0B0B0B", SiaColorTokens.BlackKpiCard);
        Assert.Equal("#0B0B0B", SiaColorTokens.BlackTextPrimary);
        Assert.Equal("#3F3F46", SiaColorTokens.BlackTextSecondary);

        // 20% Putih
        Assert.Equal("#FFFFFF", SiaColorTokens.White);

        // Pengecualian status kecil (<= 3%)
        Assert.Equal("#DC2626", SiaColorTokens.StatusError);
        Assert.Equal("#F59E0B", SiaColorTokens.StatusWarning);
        Assert.Equal("#15803D", SiaColorTokens.StatusSuccess);

        // Grafik tren
        Assert.Equal("#0B0B0B", SiaColorTokens.ChartSales);
        Assert.Equal("#4ADE80", SiaColorTokens.ChartProfit);
    }

    [Fact]
    public void SiaTheme_PaletteLight_ShouldHaveCorrectThemeTokens()
    {
        var palette = SiaTheme.Theme.PaletteLight;
        Assert.NotNull(palette);

        // Hijau muda (50%)
        Assert.Equal(new MudColor(SiaColorTokens.GreenBackground), palette.Background);
        Assert.Equal(new MudColor(SiaColorTokens.GreenHover), palette.Divider);

        // Hitam (30%)
        Assert.Equal(new MudColor(SiaColorTokens.BlackAppbar), palette.AppbarBackground);
        Assert.Equal(new MudColor(SiaColorTokens.BlackDrawer), palette.DrawerBackground);
        Assert.Equal(new MudColor(SiaColorTokens.BlackTextPrimary), palette.TextPrimary);
        Assert.Equal(new MudColor(SiaColorTokens.BlackTextSecondary), palette.TextSecondary);
        Assert.Equal(new MudColor(SiaColorTokens.BlackPrimary), palette.Primary);
        Assert.Equal(new MudColor(SiaColorTokens.GreenAccentLight), palette.PrimaryContrastText);

        // Putih (20%)
        Assert.Equal(new MudColor(SiaColorTokens.White), palette.Surface);
        Assert.Equal(new MudColor(SiaColorTokens.White), palette.AppbarText);
        Assert.Equal(new MudColor(SiaColorTokens.White), palette.DrawerText);
        Assert.Equal(new MudColor(SiaColorTokens.GreenAccentLight), palette.DrawerIcon);

        // Aksi positif
        Assert.Equal(new MudColor(SiaColorTokens.GreenAction), palette.Secondary);
        Assert.Equal(new MudColor(SiaColorTokens.BlackPrimary), palette.SecondaryContrastText);

        // Status kecil
        Assert.Equal(new MudColor(SiaColorTokens.StatusError), palette.Error);
        Assert.Equal(new MudColor(SiaColorTokens.StatusWarning), palette.Warning);
        Assert.Equal(new MudColor(SiaColorTokens.StatusSuccess), palette.Success);
    }

    [Fact]
    public void SiaTheme_ChartPalette_ShouldHaveSalesAndProfitColors()
    {
        Assert.Equal(2, SiaTheme.ChartPalette.Length);
        Assert.Equal(SiaColorTokens.ChartSales, SiaTheme.ChartPalette[0]);
        Assert.Equal(SiaColorTokens.ChartProfit, SiaTheme.ChartPalette[1]);
    }
}
