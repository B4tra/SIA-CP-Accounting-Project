namespace SIA.Web.Theme;

/// <summary>
/// Token warna SIA — SATU-SATUNYA tempat kode hex boleh ditulis.
/// Komponen memakai token ini lewat <see cref="SiaTheme"/> atau CSS variable MudBlazor
/// (<c>var(--mud-palette-*)</c>), tidak boleh menulis hex langsung.
/// Proporsi: 50% hijau, 30% hitam, 20% putih. Hanya mode terang.
/// </summary>
public static class SiaColors
{
    // Hijau (50%)
    /// <summary>Hijau utama desain Figma: latar halaman dan item menu aktif (disetujui pemilik proyek).</summary>
    public const string Green = "#8FE388";
    /// <summary>Hijau muda: latar alternatif lembut.</summary>
    public const string GreenLight = "#BBF7D0";
    /// <summary>Aksen: chip, ikon drawer, teks kontras di atas hitam.</summary>
    public const string GreenAccent = "#86EFAC";
    /// <summary>Aksi positif: tombol Setujui, progress bar, garis laba.</summary>
    public const string GreenStrong = "#4ADE80";
    /// <summary>Hover, divider, skeleton.</summary>
    public const string GreenSoft = "#DCFCE7";

    // Hitam (30%)
    public const string Black = "#0B0B0B";
    public const string TextSecondary = "#3F3F46";

    // Putih (20%)
    public const string White = "#FFFFFF";

    // Warna status fungsional (chip/ikon kecil saja)
    public const string StatusError = "#DC2626";
    public const string StatusWarning = "#F59E0B";
    public const string StatusSuccess = "#15803D";
}
