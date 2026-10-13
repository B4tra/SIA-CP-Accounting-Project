namespace SIA.Application.Common.Constants;

/// <summary>
/// Nama role ASP.NET Core Identity yang dipakai di seluruh aplikasi.
/// </summary>
public static class Roles
{
    /// <summary>Pemilik toko: akses penuh, approve PO, kelola user, konfigurasi, audit log.</summary>
    public const string Admin = "Admin";

    /// <summary>Kasir/karyawan: penjualan, PO draft, inventori, customer, supplier, pembayaran.</summary>
    public const string Staff = "Staff";

    /// <summary>Akuntan: read-only, input jurnal umum, lihat dan cetak laporan keuangan.</summary>
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = new[] { Admin, Staff, Viewer };
}
