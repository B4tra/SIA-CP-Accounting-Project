using SIA.Application.Common.Models;
using SIA.Application.DTOs.Reporting;

namespace SIA.Application.Services.Reporting;

public interface IFinancialReportService
{
    Task<Result<BalanceSheetDto>> GetBalanceSheetAsync(DateTime asOfDate, CancellationToken ct = default);
    Task<Result<IncomeStatementDto>> GetIncomeStatementAsync(DateTime startDate, DateTime endDate, CancellationToken ct = default);
    Task<Result<CashFlowDto>> GetCashFlowAsync(DateTime startDate, DateTime endDate, CancellationToken ct = default);
}
