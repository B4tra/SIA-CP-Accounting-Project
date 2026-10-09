namespace SIA.Application.DTOs.Reporting;

public record ReportLineDto
{
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public int IndentLevel { get; init; }
}

public record ReportSectionDto
{
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<ReportLineDto> Lines { get; init; } = Array.Empty<ReportLineDto>();
    public decimal Subtotal { get; init; }
}

public record BalanceSheetDto
{
    public DateTime AsOfDate { get; init; }
    public IReadOnlyList<ReportSectionDto> Assets { get; init; } = Array.Empty<ReportSectionDto>();
    public decimal TotalAssets { get; init; }
    public IReadOnlyList<ReportSectionDto> Liabilities { get; init; } = Array.Empty<ReportSectionDto>();
    public decimal TotalLiabilities { get; init; }
    public IReadOnlyList<ReportSectionDto> Equity { get; init; } = Array.Empty<ReportSectionDto>();
    public decimal TotalEquity { get; init; }
    public decimal TotalLiabilitiesAndEquity => TotalLiabilities + TotalEquity;
    public bool IsBalanced => TotalAssets == TotalLiabilitiesAndEquity;
}

public record IncomeStatementDto
{
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public IReadOnlyList<ReportSectionDto> Revenues { get; init; } = Array.Empty<ReportSectionDto>();
    public decimal TotalRevenue { get; init; }
    public IReadOnlyList<ReportSectionDto> CostOfGoodsSold { get; init; } = Array.Empty<ReportSectionDto>();
    public decimal TotalCOGS { get; init; }
    public decimal GrossProfit => TotalRevenue - TotalCOGS;
    public IReadOnlyList<ReportSectionDto> OperatingExpenses { get; init; } = Array.Empty<ReportSectionDto>();
    public decimal TotalOperatingExpenses { get; init; }
    public decimal OperatingIncome => GrossProfit - TotalOperatingExpenses;
    public IReadOnlyList<ReportSectionDto> OtherIncomeExpenses { get; init; } = Array.Empty<ReportSectionDto>();
    public decimal TotalOtherIncomeExpenses { get; init; }
    public decimal NetIncome => OperatingIncome + TotalOtherIncomeExpenses;
}

public record CashFlowDto
{
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public IReadOnlyList<ReportSectionDto> OperatingActivities { get; init; } = Array.Empty<ReportSectionDto>();
    public decimal NetCashFromOperating { get; init; }
    public IReadOnlyList<ReportSectionDto> InvestingActivities { get; init; } = Array.Empty<ReportSectionDto>();
    public decimal NetCashFromInvesting { get; init; }
    public IReadOnlyList<ReportSectionDto> FinancingActivities { get; init; } = Array.Empty<ReportSectionDto>();
    public decimal NetCashFromFinancing { get; init; }
    public decimal NetCashFlow => NetCashFromOperating + NetCashFromInvesting + NetCashFromFinancing;
    public decimal BeginningCash { get; init; }
    public decimal EndingCash => BeginningCash + NetCashFlow;
}
