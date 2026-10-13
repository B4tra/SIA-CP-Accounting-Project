namespace SIA.Application.DTOs.Reporting;

/// <summary>Satu baris laporan (per akun).</summary>
public class ReportLineDto
{
    public int AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>Kelompok baris laporan, mis. "Aset Lancar" atau "Beban Operasional".</summary>
public class ReportSectionDto
{
    public string Title { get; set; } = string.Empty;
    public List<ReportLineDto> Lines { get; set; } = new();
    public decimal Total { get; set; }
}

/// <summary>Neraca per tanggal tertentu.</summary>
public class BalanceSheetDto
{
    public DateTime AsOfDate { get; set; }

    /// <summary>Aset Lancar, Aset Tetap.</summary>
    public List<ReportSectionDto> AssetSections { get; set; } = new();

    /// <summary>Liabilitas Jangka Pendek, Liabilitas Jangka Panjang (termasuk utang usaha).</summary>
    public List<ReportSectionDto> LiabilitySections { get; set; } = new();

    public List<ReportSectionDto> EquitySections { get; set; } = new();

    /// <summary>Laba/rugi berjalan yang belum ditutup ke ekuitas.</summary>
    public decimal CurrentPeriodNetIncome { get; set; }

    public decimal TotalAssets { get; set; }
    public decimal TotalLiabilities { get; set; }
    public decimal TotalEquity { get; set; }
}

/// <summary>Laporan laba rugi untuk satu periode.</summary>
public class IncomeStatementDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }

    public ReportSectionDto Revenue { get; set; } = new();
    public ReportSectionDto CostOfGoodsSold { get; set; } = new();
    public ReportSectionDto OperatingExpenses { get; set; } = new();
    public ReportSectionDto OtherRevenue { get; set; } = new();
    public ReportSectionDto OtherExpenses { get; set; } = new();

    public decimal GrossProfit { get; set; }
    public decimal OperatingProfit { get; set; }
    public decimal NetIncome { get; set; }
}

/// <summary>Laporan arus kas untuk satu periode.</summary>
public class CashFlowDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }

    public decimal OpeningCash { get; set; }

    public ReportSectionDto OperatingActivities { get; set; } = new();
    public ReportSectionDto InvestingActivities { get; set; } = new();
    public ReportSectionDto FinancingActivities { get; set; } = new();

    public decimal NetCashChange { get; set; }
    public decimal ClosingCash { get; set; }
}
