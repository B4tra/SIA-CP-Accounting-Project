namespace SIA.Web.Theme;

/// <summary>
/// Token warna terpusat sesuai panduan tema SIA:
/// Proporsi: 50% hijau muda, 30% hitam, 20% putih.
/// Seluruh komponen wajib menggunakan token ini dan tidak boleh memakai kode hex langsung.
/// </summary>
public static class SiaColorTokens
{
    // Hijau muda (50%)
    public const string GreenBackground = "#BBF7D0";      // Latar utama halaman
    public const string GreenAccentLight = "#86EFAC";     // Chip, ikon, header kecil, drawer item aktif
    public const string GreenAction = "#4ADE80";          // Tombol aksi positif, progress bar, garis laba
    public const string GreenHover = "#DCFCE7";           // Hover, divider, skeleton

    // Hitam (30%)
    public const string BlackPrimary = "#0B0B0B";         // Tombol utama, header tabel, app bar, drawer
    public const string BlackAppbar = "#0B0B0B";          // Latar app bar
    public const string BlackDrawer = "#0B0B0B";          // Latar sidebar drawer
    public const string BlackKpiCard = "#0B0B0B";         // Kartu KPI berlatar hitam
    public const string BlackTextPrimary = "#0B0B0B";     // Teks utama
    public const string BlackTextSecondary = "#3F3F46";   // Teks sekunder

    // Putih (20%)
    public const string White = "#FFFFFF";                // Surface kartu, isi tabel, input, teks appbar & drawer

    // Pengecualian fungsional kecil (maksimal 3% layar, hanya untuk chip & status kecil)
    public const string StatusError = "#DC2626";          // "Terlambat", "Habis", pesan error
    public const string StatusWarning = "#F59E0B";        // "Menipis", "H-n"
    public const string StatusSuccess = "#15803D";        // Indikator tren naik

    // Palet grafik tren
    public const string ChartSales = "#0B0B0B";           // Garis/bar penjualan
    public const string ChartProfit = "#4ADE80";          // Garis/bar laba
}
