using SIA.Application.DTOs.Inventory;
using SIA.Application.DTOs.Payments;
using SIA.Application.DTOs.Purchasing;
using SIA.Application.DTOs.Sales;

namespace SIA.Application.DTOs.Reporting;

/// <summary>
/// Pilihan periode waktu untuk grafik tren penjualan vs pembelian dan peringkat produk aki terlaris.
/// </summary>
public enum DashboardTrendPeriod
{
    /// <summary>7 hari terakhir (harian).</summary>
    Last7Days = 0,

    /// <summary>Bulan berjalan (harian s.d. hari ini).</summary>
    ThisMonth = 1,

    /// <summary>Tahun berjalan (bulanan Jan - Des).</summary>
    ThisYear = 2
}

/// <summary>
/// Titik data visualisasi tren perbandingan penjualan bersih vs pembelian diterima bersih.
/// </summary>
public class DashboardTrendPointDto
{
    /// <summary>Label sumbu X, mis. "01/11", "Nov", "Senin".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Nominal penjualan bersih (penjualan dikurangi retur penjualan) pada titik waktu ini.</summary>
    public decimal NetSales { get; set; }

    /// <summary>Nominal pembelian diterima bersih (PO diterima dikurangi retur pembelian) pada titik waktu ini.</summary>
    public decimal NetPurchases { get; set; }
}

/// <summary>
/// Data produk aki/baterai dengan kuantitas dan nominal penjualan tertinggi pada periode terpilih.
/// </summary>
public class TopProductDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? SKU { get; set; }
    public string? Brand { get; set; }

    /// <summary>Kuantitas bersih terjual (unit terjual dikurangi unit retur).</summary>
    public int NetQuantitySold { get; set; }

    /// <summary>Total nominal pendapatan bersih dari produk ini.</summary>
    public decimal NetRevenue { get; set; }

    /// <summary>Persentase kontribusi terhadap penjualan kelompok produk terlaris (0 - 100%).</summary>
    public double SharePercentage { get; set; }
}

/// <summary>
/// Data ringkasan lengkap untuk Dashboard utama SIA.
/// </summary>
public class DashboardSummaryDto
{
    // ==========================================
    // 1. KPI Operasional (Dapat dilihat semua role)
    // ==========================================

    /// <summary>Total penjualan bersih hari ini (penjualan dikurangi retur penjualan).</summary>
    public decimal TodayNetSales { get; set; }

    /// <summary>Jumlah transaksi nota penjualan hari ini.</summary>
    public int TodaySaleCount { get; set; }

    /// <summary>Total penjualan bersih bulan berjalan (penjualan dikurangi retur penjualan).</summary>
    public decimal MonthNetSales { get; set; }

    /// <summary>Jumlah transaksi nota penjualan bulan berjalan.</summary>
    public int MonthSaleCount { get; set; }

    /// <summary>Total sisa piutang penjualan belum lunas.</summary>
    public decimal TotalReceivables { get; set; }

    /// <summary>Jumlah invoice penjualan yang belum lunas (Unpaid / PartiallyPaid).</summary>
    public int UnpaidSaleCount { get; set; }

    /// <summary>Jumlah piutang yang telah melewati tanggal jatuh tempo.</summary>
    public int OverdueReceivableCount { get; set; }

    /// <summary>Total nominal piutang yang telah melewati tanggal jatuh tempo.</summary>
    public decimal OverdueReceivablesAmount { get; set; }

    /// <summary>Total sisa utang PO berstatus Received yang belum lunas.</summary>
    public decimal TotalPayables { get; set; }

    /// <summary>Jumlah PO diterima yang belum lunas (Unpaid / PartiallyPaid).</summary>
    public int UnpaidPoCount { get; set; }

    /// <summary>Jumlah utang yang telah melewati tanggal jatuh tempo.</summary>
    public int OverduePayableCount { get; set; }

    /// <summary>Total nominal utang yang telah melewati tanggal jatuh tempo.</summary>
    public decimal OverduePayablesAmount { get; set; }

    /// <summary>Jumlah PO dengan status Draft (antrean persetujuan Admin).</summary>
    public int DraftPoCount { get; set; }

    /// <summary>Jumlah PO dengan status Approved (antrean penerimaan barang Staff/Admin).</summary>
    public int ApprovedPoCount { get; set; }

    /// <summary>Jumlah SKU aktif dengan stok di bawah atau sama dengan ReorderPoint.</summary>
    public int LowStockProductCount { get; set; }

    /// <summary>Jumlah SKU aktif dengan stok habis (stok = 0).</summary>
    public int OutOfStockProductCount { get; set; }

    /// <summary>Total produk aktif yang terdaftar.</summary>
    public int TotalActiveProductCount { get; set; }


    // ==========================================
    // 2. KPI Keuangan (Khusus Admin & Viewer)
    // ==========================================

    /// <summary>
    /// Menandakan apakah pengguna yang sedang login memiliki hak akses melihat metrik keuangan (Admin &amp; Viewer).
    /// Jika false, nilai metrik keuangan di bawah akan bernilai null.
    /// </summary>
    public bool CanViewFinancials { get; set; }

    /// <summary>Total saldo akun Kas (1-1001) dan Bank (1-1002).</summary>
    public decimal? CashAndBankBalance { get; set; }

    /// <summary>Laba bersih bulan berjalan (Pendapatan - HPP - Beban Operasional + Bersih Lain-lain).</summary>
    public decimal? CurrentMonthNetIncome { get; set; }

    /// <summary>Total pendapatan akun kategori Pendapatan bulan berjalan.</summary>
    public decimal? CurrentMonthRevenue { get; set; }

    /// <summary>Total seluruh beban bulan berjalan (HPP + Beban Operasional + Beban Lain-lain).</summary>
    public decimal? CurrentMonthExpenses { get; set; }


    // ==========================================
    // 3. Visualisasi Tren & Produk Terlaris
    // ==========================================

    /// <summary>Periode aktif yang digunakan pada grafik tren dan produk terlaris.</summary>
    public DashboardTrendPeriod TrendPeriod { get; set; } = DashboardTrendPeriod.Last7Days;

    /// <summary>Daftar titik data tren penjualan vs pembelian.</summary>
    public List<DashboardTrendPointDto> TrendPoints { get; set; } = new();

    /// <summary>Peringkat hingga 5 produk aki terlaris berdasarkan kuantitas bersih terjual.</summary>
    public List<TopProductDto> TopProducts { get; set; } = new();


    // ==========================================
    // 4. Panel Operasional Bawah
    // ==========================================

    /// <summary>Peringatan produk dengan stok mencapai batas minimum atau habis.</summary>
    public List<ReorderAlertDto> LowStockAlerts { get; set; } = new();

    /// <summary>Tagihan piutang dan utang yang mendekati atau telah melewati jatuh tempo.</summary>
    public List<OutstandingDto> DueAndOverdueItems { get; set; } = new();

    /// <summary>Antrean Purchase Order berstatus Draft dan Approved.</summary>
    public List<PurchaseOrderDto> PendingPurchaseOrders { get; set; } = new();

    /// <summary>Daftar transaksi penjualan terbaru (maksimal 5 transaksi terakhir).</summary>
    public List<SaleDto> RecentSales { get; set; } = new();
}
