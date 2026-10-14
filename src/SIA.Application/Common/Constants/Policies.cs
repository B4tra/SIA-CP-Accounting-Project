namespace SIA.Application.Common.Constants;

/// <summary>
/// Nama policy otorisasi dan pemetaan policy -> role (sesuai matriks hak akses AGENTS.md).
/// Didaftarkan di layer Web lewat <c>AddAuthorization</c>; dipakai juga oleh service untuk
/// menegakkan otorisasi di sisi server.
/// </summary>
public static class Policies
{
    /// <summary>Hanya Admin: kelola user, konfigurasi nomor transaksi, audit log.</summary>
    public const string RequireAdmin = "RequireAdmin";

    /// <summary>Membuat/mengubah transaksi (penjualan, PO draft, inventori, customer, supplier, pembayaran).</summary>
    public const string ManageTransactions = "ManageTransactions";

    /// <summary>Approve Purchase Order: hanya Admin.</summary>
    public const string ApprovePurchaseOrder = "ApprovePurchaseOrder";

    /// <summary>Input jurnal umum (manual).</summary>
    public const string InputJournal = "InputJournal";

    /// <summary>Lihat dan cetak laporan keuangan.</summary>
    public const string ViewReports = "ViewReports";

    /// <summary>Melihat data (read-only) — semua role yang login.</summary>
    public const string ViewAllData = "ViewAllData";

    /// <summary>Pemetaan policy ke role yang diizinkan.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RoleMap =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [RequireAdmin] = new[] { Roles.Admin },
            [ManageTransactions] = new[] { Roles.Admin, Roles.Staff },
            [ApprovePurchaseOrder] = new[] { Roles.Admin },
            [InputJournal] = new[] { Roles.Admin, Roles.Viewer },
            [ViewReports] = new[] { Roles.Admin, Roles.Viewer },
            [ViewAllData] = new[] { Roles.Admin, Roles.Staff, Roles.Viewer },
        };
}
