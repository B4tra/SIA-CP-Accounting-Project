using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.Reporting;
using SIA.Application.Services.Reporting;
using SIA.Domain.Entities.Accounting;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Services;

/// <summary>
/// Layanan penyusunan laporan keuangan (Neraca, Laba Rugi, dan Arus Kas).
/// Akses: Admin dan Viewer (Policies.ViewReports).
/// </summary>
public class FinancialReportService : IFinancialReportService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public FinancialReportService(AppDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<BalanceSheetDto>> GetBalanceSheetAsync(DateTime asOfDate, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewReports))
            return Result<BalanceSheetDto>.Failure(ServiceGuard.ReadDenied);

        var asOf = asOfDate.Date;
        var endExclusive = asOf.AddDays(1);

        var accounts = await _context.Accounts.AsNoTracking()
            .OrderBy(a => a.Code)
            .ToListAsync(cancellationToken);

        // Ambil seluruh baris jurnal aktif s.d. asOfDate
        var lines = await _context.JournalEntryLines.AsNoTracking()
            .Where(l => !l.JournalEntry.IsDeleted && l.JournalEntry.TransactionDate < endExclusive)
            .Select(l => new { l.AccountId, l.Debit, l.Credit })
            .ToListAsync(cancellationToken);

        var lineTotals = lines
            .GroupBy(l => l.AccountId)
            .ToDictionary(
                g => g.Key,
                g => (Debit: g.Sum(x => x.Debit), Credit: g.Sum(x => x.Credit))
            );

        // 1. Asset Sections (AsetLancar, AsetTetap) -> Amount = Debit - Credit
        var currentAssets = BuildBalanceSheetSection("Aset Lancar", AccountCategory.AsetLancar, accounts, lineTotals, normalDebit: true);
        var fixedAssets = BuildBalanceSheetSection("Aset Tetap", AccountCategory.AsetTetap, accounts, lineTotals, normalDebit: true);
        var assetSections = new List<ReportSectionDto> { currentAssets, fixedAssets };
        var totalAssets = assetSections.Sum(s => s.Total);

        // 2. Liability Sections (LiabilitasJangkaPendek, LiabilitasJangkaPanjang) -> Amount = Credit - Debit
        var shortLiabilities = BuildBalanceSheetSection("Liabilitas Jangka Pendek", AccountCategory.LiabilitasJangkaPendek, accounts, lineTotals, normalDebit: false);
        var longLiabilities = BuildBalanceSheetSection("Liabilitas Jangka Panjang", AccountCategory.LiabilitasJangkaPanjang, accounts, lineTotals, normalDebit: false);
        var liabilitySections = new List<ReportSectionDto> { shortLiabilities, longLiabilities };
        var totalLiabilities = liabilitySections.Sum(s => s.Total);

        // 3. Equity Sections (Ekuitas) -> Amount = Credit - Debit
        var equitySection = BuildBalanceSheetSection("Ekuitas", AccountCategory.Ekuitas, accounts, lineTotals, normalDebit: false);
        var equitySections = new List<ReportSectionDto> { equitySection };

        // 4. CurrentPeriodNetIncome s.d. asOfDate
        // Pendapatan (Pendapatan, PendapatanLainLain) = Credit - Debit
        // Beban (HPP, BebanOperasional, BebanLainLain) = Debit - Credit
        decimal totalRevenue = 0m;
        decimal totalExpenses = 0m;

        foreach (var acc in accounts)
        {
            var (debit, credit) = lineTotals.TryGetValue(acc.Id, out var t) ? t : (0m, 0m);
            switch (acc.Category)
            {
                case AccountCategory.Pendapatan:
                case AccountCategory.PendapatanLainLain:
                    totalRevenue += (credit - debit);
                    break;

                case AccountCategory.BebanPokokPenjualan:
                case AccountCategory.BebanOperasional:
                case AccountCategory.BebanLainLain:
                    totalExpenses += (debit - credit);
                    break;
            }
        }

        var currentPeriodNetIncome = totalRevenue - totalExpenses;
        var totalEquity = equitySections.Sum(s => s.Total) + currentPeriodNetIncome;

        return Result<BalanceSheetDto>.Success(new BalanceSheetDto
        {
            AsOfDate = asOf,
            AssetSections = assetSections,
            LiabilitySections = liabilitySections,
            EquitySections = equitySections,
            CurrentPeriodNetIncome = currentPeriodNetIncome,
            TotalAssets = totalAssets,
            TotalLiabilities = totalLiabilities,
            TotalEquity = totalEquity
        });
    }

    public async Task<Result<IncomeStatementDto>> GetIncomeStatementAsync(DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewReports))
            return Result<IncomeStatementDto>.Failure(ServiceGuard.ReadDenied);

        if (dateFrom.Date > dateTo.Date)
            return Result<IncomeStatementDto>.Failure("Tanggal awal tidak boleh melebihi tanggal akhir.");

        var start = dateFrom.Date;
        var endExclusive = dateTo.Date.AddDays(1);

        var accounts = await _context.Accounts.AsNoTracking()
            .OrderBy(a => a.Code)
            .ToListAsync(cancellationToken);

        var lines = await _context.JournalEntryLines.AsNoTracking()
            .Where(l => !l.JournalEntry.IsDeleted
                && l.JournalEntry.TransactionDate >= start
                && l.JournalEntry.TransactionDate < endExclusive)
            .Select(l => new { l.AccountId, l.Debit, l.Credit })
            .ToListAsync(cancellationToken);

        var lineTotals = lines
            .GroupBy(l => l.AccountId)
            .ToDictionary(
                g => g.Key,
                g => (Debit: g.Sum(x => x.Debit), Credit: g.Sum(x => x.Credit))
            );

        var revenue = BuildIncomeStatementSection("Pendapatan", AccountCategory.Pendapatan, accounts, lineTotals, isCreditPositive: true);
        var cogs = BuildIncomeStatementSection("Beban Pokok Penjualan", AccountCategory.BebanPokokPenjualan, accounts, lineTotals, isCreditPositive: false);
        var grossProfit = revenue.Total - cogs.Total;

        var operatingExpenses = BuildIncomeStatementSection("Beban Operasional", AccountCategory.BebanOperasional, accounts, lineTotals, isCreditPositive: false);
        var operatingProfit = grossProfit - operatingExpenses.Total;

        var otherRevenue = BuildIncomeStatementSection("Pendapatan Lain-lain", AccountCategory.PendapatanLainLain, accounts, lineTotals, isCreditPositive: true);
        var otherExpenses = BuildIncomeStatementSection("Beban Lain-lain", AccountCategory.BebanLainLain, accounts, lineTotals, isCreditPositive: false);
        var netIncome = operatingProfit + otherRevenue.Total - otherExpenses.Total;

        return Result<IncomeStatementDto>.Success(new IncomeStatementDto
        {
            DateFrom = start,
            DateTo = dateTo.Date,
            Revenue = revenue,
            CostOfGoodsSold = cogs,
            GrossProfit = grossProfit,
            OperatingExpenses = operatingExpenses,
            OperatingProfit = operatingProfit,
            OtherRevenue = otherRevenue,
            OtherExpenses = otherExpenses,
            NetIncome = netIncome
        });
    }

    public async Task<Result<CashFlowDto>> GetCashFlowAsync(DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewReports))
            return Result<CashFlowDto>.Failure(ServiceGuard.ReadDenied);

        if (dateFrom.Date > dateTo.Date)
            return Result<CashFlowDto>.Failure("Tanggal awal tidak boleh melebihi tanggal akhir.");

        var start = dateFrom.Date;
        var endExclusive = dateTo.Date.AddDays(1);

        var accounts = await _context.Accounts.AsNoTracking()
            .OrderBy(a => a.Code)
            .ToListAsync(cancellationToken);

        var accountMap = accounts.ToDictionary(a => a.Id);

        // Deteksi akun Kas & Bank
        var cashAccountIds = accounts
            .Where(a => a.Category == AccountCategory.AsetLancar &&
                (a.Code == "1-1001" || a.Code == "1-1002" ||
                 a.Name.Contains("Kas", StringComparison.OrdinalIgnoreCase) ||
                 a.Name.Contains("Bank", StringComparison.OrdinalIgnoreCase)))
            .Select(a => a.Id)
            .ToHashSet();

        // 1. Saldo Awal Kas sebelum dateFrom
        var openingLines = await _context.JournalEntryLines.AsNoTracking()
            .Where(l => !l.JournalEntry.IsDeleted
                && l.JournalEntry.TransactionDate < start
                && cashAccountIds.Contains(l.AccountId))
            .Select(l => new { l.Debit, l.Credit })
            .ToListAsync(cancellationToken);

        var openingCash = openingLines.Sum(l => l.Debit - l.Credit);

        // 2. Ambil seluruh jurnal dalam periode yang menyentuh kas
        var periodJournals = await _context.JournalEntries.AsNoTracking()
            .Where(j => !j.IsDeleted
                && j.TransactionDate >= start
                && j.TransactionDate < endExclusive
                && j.Lines.Any(l => cashAccountIds.Contains(l.AccountId)))
            .Include(j => j.Lines)
            .ToListAsync(cancellationToken);

        var operatingItems = new List<(Account Account, decimal Amount)>();
        var investingItems = new List<(Account Account, decimal Amount)>();
        var financingItems = new List<(Account Account, decimal Amount)>();

        foreach (var journal in periodJournals)
        {
            var nonCashLines = journal.Lines.Where(l => !cashAccountIds.Contains(l.AccountId)).ToList();
            if (nonCashLines.Count == 0)
            {
                // Transfer internal kas <-> bank; lewati (perubahan kas bersih = 0)
                continue;
            }

            foreach (var line in nonCashLines)
            {
                if (!accountMap.TryGetValue(line.AccountId, out var acc))
                    continue;

                // Dampak kas dari baris akun non-kas: Credit - Debit
                // (Kredit pada akun lawan = uang masuk; Debit pada akun lawan = uang keluar)
                var amount = line.Credit - line.Debit;

                switch (acc.Category)
                {
                    case AccountCategory.AsetTetap:
                        investingItems.Add((acc, amount));
                        break;

                    case AccountCategory.LiabilitasJangkaPanjang:
                    case AccountCategory.Ekuitas:
                        financingItems.Add((acc, amount));
                        break;

                    default:
                        // Pendapatan, Beban, Liabilitas Jangka Pendek, Aset Lancar non-kas
                        operatingItems.Add((acc, amount));
                        break;
                }
            }
        }

        var operatingActivities = BuildCashFlowSection("Arus Kas dari Aktivitas Operasi", operatingItems);
        var investingActivities = BuildCashFlowSection("Arus Kas dari Aktivitas Investasi", investingItems);
        var financingActivities = BuildCashFlowSection("Arus Kas dari Aktivitas Pendanaan", financingItems);

        var netCashChange = operatingActivities.Total + investingActivities.Total + financingActivities.Total;
        var closingCash = openingCash + netCashChange;

        return Result<CashFlowDto>.Success(new CashFlowDto
        {
            DateFrom = start,
            DateTo = dateTo.Date,
            OpeningCash = openingCash,
            OperatingActivities = operatingActivities,
            InvestingActivities = investingActivities,
            FinancingActivities = financingActivities,
            NetCashChange = netCashChange,
            ClosingCash = closingCash
        });
    }

    #region Helper Methods

    private static ReportSectionDto BuildBalanceSheetSection(
        string title,
        AccountCategory category,
        IReadOnlyList<Account> accounts,
        IReadOnlyDictionary<int, (decimal Debit, decimal Credit)> lineTotals,
        bool normalDebit)
    {
        var sectionAccounts = accounts.Where(a => a.Category == category).ToList();
        var linesDto = new List<ReportLineDto>();

        foreach (var acc in sectionAccounts)
        {
            var (debit, credit) = lineTotals.TryGetValue(acc.Id, out var t) ? t : (0m, 0m);
            var amount = normalDebit ? (debit - credit) : (credit - debit);

            // Sertakan semua akun aktif, atau akun nonaktif yang masih memiliki saldo
            if (acc.IsActive || amount != 0m)
            {
                linesDto.Add(new ReportLineDto
                {
                    AccountId = acc.Id,
                    AccountCode = acc.Code,
                    AccountName = acc.Name,
                    Amount = amount
                });
            }
        }

        return new ReportSectionDto
        {
            Title = title,
            Lines = linesDto,
            Total = linesDto.Sum(l => l.Amount)
        };
    }

    private static ReportSectionDto BuildIncomeStatementSection(
        string title,
        AccountCategory category,
        IReadOnlyList<Account> accounts,
        IReadOnlyDictionary<int, (decimal Debit, decimal Credit)> lineTotals,
        bool isCreditPositive)
    {
        var sectionAccounts = accounts.Where(a => a.Category == category).ToList();
        var linesDto = new List<ReportLineDto>();

        foreach (var acc in sectionAccounts)
        {
            var (debit, credit) = lineTotals.TryGetValue(acc.Id, out var t) ? t : (0m, 0m);
            var amount = isCreditPositive ? (credit - debit) : (debit - credit);

            if (acc.IsActive || amount != 0m)
            {
                linesDto.Add(new ReportLineDto
                {
                    AccountId = acc.Id,
                    AccountCode = acc.Code,
                    AccountName = acc.Name,
                    Amount = amount
                });
            }
        }

        return new ReportSectionDto
        {
            Title = title,
            Lines = linesDto,
            Total = linesDto.Sum(l => l.Amount)
        };
    }

    private static ReportSectionDto BuildCashFlowSection(
        string title,
        IReadOnlyList<(Account Account, decimal Amount)> items)
    {
        var linesDto = items
            .GroupBy(x => x.Account.Id)
            .Select(g =>
            {
                var acc = g.First().Account;
                return new ReportLineDto
                {
                    AccountId = acc.Id,
                    AccountCode = acc.Code,
                    AccountName = acc.Name,
                    Amount = g.Sum(x => x.Amount)
                };
            })
            .Where(l => l.Amount != 0m)
            .OrderBy(l => l.AccountCode)
            .ToList();

        return new ReportSectionDto
        {
            Title = title,
            Lines = linesDto,
            Total = linesDto.Sum(l => l.Amount)
        };
    }

    #endregion
}
