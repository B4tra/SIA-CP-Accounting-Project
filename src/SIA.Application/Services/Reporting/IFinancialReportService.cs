using SIA.Application.Common.Models;
using SIA.Application.DTOs.Reporting;

namespace SIA.Application.Services.Reporting;

/// <summary>
/// Laporan keuangan yang disusun dari jurnal umum dan saldo akun. Akses: Admin, Viewer.
/// </summary>
public interface IFinancialReportService
{
    Task<Result<BalanceSheetDto>> GetBalanceSheetAsync(DateTime asOfDate, CancellationToken cancellationToken = default);

    Task<Result<IncomeStatementDto>> GetIncomeStatementAsync(DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default);

    Task<Result<CashFlowDto>> GetCashFlowAsync(DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default);
}
