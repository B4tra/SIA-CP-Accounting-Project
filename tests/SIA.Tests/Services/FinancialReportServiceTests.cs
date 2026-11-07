using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.Accounting;
using SIA.Domain.Entities.Accounting;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data.Seed;
using SIA.Infrastructure.Identity;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;
using Xunit;

namespace SIA.Tests.Services;

public class FinancialReportServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private FinancialReportService CreateReportService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);
        return new FinancialReportService(context, user);
    }

    private JournalService CreateJournalService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);
        var numbers = new TransactionNumberService(context, user);
        return new JournalService(context, user, numbers);
    }

    private async Task<Dictionary<string, int>> SeedFixtureAsync()
    {
        await using var context = _db.CreateContext();
        await MasterDataSeeder.SeedAsync(context, NullLogger.Instance);

        if (!await context.Users.AnyAsync(u => u.Id == "user-test"))
        {
            context.Users.Add(new ApplicationUser
            {
                Id = "user-test",
                UserName = "tester@sia.local",
                Email = "tester@sia.local",
                FullName = "Tester SIA",
                IsActive = true
            });
            await context.SaveChangesAsync();
        }

        return await context.Accounts.ToDictionaryAsync(a => a.Code, a => a.Id);
    }

    #region Authorization Tests

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Viewer)]
    public async Task Reports_AdminAndViewer_Allowed(string role)
    {
        await SeedFixtureAsync();
        var service = CreateReportService(role);

        var balanceSheet = await service.GetBalanceSheetAsync(DateTime.Today);
        var incomeStatement = await service.GetIncomeStatementAsync(DateTime.Today.AddDays(-30), DateTime.Today);
        var cashFlow = await service.GetCashFlowAsync(DateTime.Today.AddDays(-30), DateTime.Today);

        Assert.True(balanceSheet.IsSuccess);
        Assert.True(incomeStatement.IsSuccess);
        Assert.True(cashFlow.IsSuccess);
    }

    [Fact]
    public async Task Reports_Staff_Denied()
    {
        await SeedFixtureAsync();
        var service = CreateReportService(Roles.Staff);

        var balanceSheet = await service.GetBalanceSheetAsync(DateTime.Today);
        var incomeStatement = await service.GetIncomeStatementAsync(DateTime.Today.AddDays(-30), DateTime.Today);
        var cashFlow = await service.GetCashFlowAsync(DateTime.Today.AddDays(-30), DateTime.Today);

        Assert.True(balanceSheet.IsFailure);
        Assert.Contains("hak akses", balanceSheet.Error);

        Assert.True(incomeStatement.IsFailure);
        Assert.Contains("hak akses", incomeStatement.Error);

        Assert.True(cashFlow.IsFailure);
        Assert.Contains("hak akses", cashFlow.Error);
    }

    [Fact]
    public async Task Reports_Anonymous_Denied()
    {
        await SeedFixtureAsync();
        var service = CreateReportService(); // Tanpa role

        var balanceSheet = await service.GetBalanceSheetAsync(DateTime.Today);
        var incomeStatement = await service.GetIncomeStatementAsync(DateTime.Today.AddDays(-30), DateTime.Today);
        var cashFlow = await service.GetCashFlowAsync(DateTime.Today.AddDays(-30), DateTime.Today);

        Assert.True(balanceSheet.IsFailure);
        Assert.True(incomeStatement.IsFailure);
        Assert.True(cashFlow.IsFailure);
    }

    #endregion

    #region Date Range Validation Tests

    [Fact]
    public async Task IncomeStatement_DateFromGreaterThanDateTo_Rejected()
    {
        await SeedFixtureAsync();
        var service = CreateReportService(Roles.Admin);

        var result = await service.GetIncomeStatementAsync(
            new DateTime(2026, 2, 1),
            new DateTime(2026, 1, 1));

        Assert.True(result.IsFailure);
        Assert.Contains("Tanggal awal tidak boleh melebihi tanggal akhir", result.Error);
    }

    [Fact]
    public async Task CashFlow_DateFromGreaterThanDateTo_Rejected()
    {
        await SeedFixtureAsync();
        var service = CreateReportService(Roles.Admin);

        var result = await service.GetCashFlowAsync(
            new DateTime(2026, 2, 1),
            new DateTime(2026, 1, 1));

        Assert.True(result.IsFailure);
        Assert.Contains("Tanggal awal tidak boleh melebihi tanggal akhir", result.Error);
    }

    #endregion

    #region GetBalanceSheetAsync Tests

    [Fact]
    public async Task BalanceSheet_ZeroState_ReturnsBalancedReport()
    {
        await SeedFixtureAsync();
        var service = CreateReportService(Roles.Admin);

        var result = await service.GetBalanceSheetAsync(DateTime.Today);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        Assert.Equal(0m, result.Value.TotalAssets);
        Assert.Equal(0m, result.Value.TotalLiabilities);
        Assert.Equal(0m, result.Value.TotalEquity);
        Assert.Equal(0m, result.Value.CurrentPeriodNetIncome);
        Assert.Equal(result.Value.TotalAssets, result.Value.TotalLiabilities + result.Value.TotalEquity);
    }

    [Fact]
    public async Task BalanceSheet_SectionsAndContraAccounts_CalculatedCorrectly()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var peralatanId = accounts["1-2001"];
        var akumPenyusutanId = accounts["1-2002"];
        var utangUsahaId = accounts["2-1001"];
        var modalId = accounts["3-1001"];
        var priveId = accounts["3-1002"];
        var pendapatanId = accounts["4-1001"];
        var bebanGajiId = accounts["6-1001"];
        var bebanPenyusutanId = accounts["6-1004"];

        var journalService = CreateJournalService(Roles.Admin);

        // 1. Setor modal 100 jt ke Kas
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 1),
            Description = "Setor modal kas",
            Lines = new() { new() { AccountId = kasId, Debit = 100_000_000m }, new() { AccountId = modalId, Credit = 100_000_000m } }
        });

        // 2. Beli peralatan toko 20 jt (10 jt kas, 10 jt utang)
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 2),
            Description = "Beli peralatan toko",
            Lines = new()
            {
                new() { AccountId = peralatanId, Debit = 20_000_000m },
                new() { AccountId = kasId, Credit = 10_000_000m },
                new() { AccountId = utangUsahaId, Credit = 10_000_000m }
            }
        });

        // 3. Penyusutan peralatan toko 2 jt (contra-asset: Akumulasi Penyusutan kredit 2 jt)
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 15),
            Description = "Penyusutan peralatan",
            Lines = new()
            {
                new() { AccountId = bebanPenyusutanId, Debit = 2_000_000m },
                new() { AccountId = akumPenyusutanId, Credit = 2_000_000m }
            }
        });

        // 4. Penjualan aki tunai 15 jt
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 18),
            Description = "Penjualan aki tunai",
            Lines = new() { new() { AccountId = kasId, Debit = 15_000_000m }, new() { AccountId = pendapatanId, Credit = 15_000_000m } }
        });

        // 5. Beban gaji tunai 5 jt
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 20),
            Description = "Beban gaji",
            Lines = new() { new() { AccountId = bebanGajiId, Debit = 5_000_000m }, new() { AccountId = kasId, Credit = 5_000_000m } }
        });

        // 6. Prive pemilik 3 jt (contra-equity: Prive debit 3 jt)
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 25),
            Description = "Prive pemilik tunai",
            Lines = new() { new() { AccountId = priveId, Debit = 3_000_000m }, new() { AccountId = kasId, Credit = 3_000_000m } }
        });

        var reportService = CreateReportService(Roles.Admin);
        var reportResult = await reportService.GetBalanceSheetAsync(new DateTime(2026, 1, 31));

        Assert.True(reportResult.IsSuccess, reportResult.Error);
        var bs = reportResult.Value!;

        // 1. Verifikasi Asset Sections
        var asetLancar = bs.AssetSections.Single(s => s.Title == "Aset Lancar");
        var asetTetap = bs.AssetSections.Single(s => s.Title == "Aset Tetap");

        // Kas: 100 - 10 + 15 - 5 - 3 = 97 jt
        var kasLine = asetLancar.Lines.Single(l => l.AccountId == kasId);
        Assert.Equal(97_000_000m, kasLine.Amount);
        Assert.Equal(97_000_000m, asetLancar.Total);

        // Aset Tetap: Peralatan (20 jt) + Akumulasi Penyusutan (-2 jt) = 18 jt
        var peralatanLine = asetTetap.Lines.Single(l => l.AccountId == peralatanId);
        var akumPenyusutanLine = asetTetap.Lines.Single(l => l.AccountId == akumPenyusutanId);
        Assert.Equal(20_000_000m, peralatanLine.Amount);
        Assert.Equal(-2_000_000m, akumPenyusutanLine.Amount); // Contra-account bernilai negatif!
        Assert.Equal(18_000_000m, asetTetap.Total);

        // Total Assets: 97 + 18 = 115 jt
        Assert.Equal(115_000_000m, bs.TotalAssets);

        // 2. Verifikasi Liability Sections
        var shortLiab = bs.LiabilitySections.Single(s => s.Title == "Liabilitas Jangka Pendek");
        var utangLine = shortLiab.Lines.Single(l => l.AccountId == utangUsahaId);
        Assert.Equal(10_000_000m, utangLine.Amount);
        Assert.Equal(10_000_000m, bs.TotalLiabilities);

        // 3. Verifikasi Equity Sections
        var ekuitasSection = bs.EquitySections.Single(s => s.Title == "Ekuitas");
        var modalLine = ekuitasSection.Lines.Single(l => l.AccountId == modalId);
        var priveLine = ekuitasSection.Lines.Single(l => l.AccountId == priveId);
        Assert.Equal(100_000_000m, modalLine.Amount);
        Assert.Equal(-3_000_000m, priveLine.Amount); // Contra-account bernilai negatif!
        Assert.Equal(97_000_000m, ekuitasSection.Total);

        // 4. Verifikasi CurrentPeriodNetIncome
        // Revenue: Penjualan 15 jt
        // Expense: Beban gaji 5 jt + Beban penyusutan 2 jt = 7 jt
        // Net Income: 15 jt - 7 jt = 8 jt
        Assert.Equal(8_000_000m, bs.CurrentPeriodNetIncome);

        // 5. Total Equity = Ekuitas (97 jt) + Net Income (8 jt) = 105 jt
        Assert.Equal(105_000_000m, bs.TotalEquity);

        // 6. STRICT INVARIANT: Total Assets (115 jt) == Total Liabilities (10 jt) + Total Equity (105 jt)
        Assert.Equal(bs.TotalAssets, bs.TotalLiabilities + bs.TotalEquity);
    }

    [Fact]
    public async Task BalanceSheet_ExcludesFutureTransactionsAndSoftDeletedJournals()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var modalId = accounts["3-1001"];
        var journalService = CreateJournalService(Roles.Admin);

        // Transaksi 1: 15 Januari (Valid)
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 15),
            Description = "Modal valid",
            Lines = new() { new() { AccountId = kasId, Debit = 50_000_000m }, new() { AccountId = modalId, Credit = 50_000_000m } }
        });

        // Transaksi 2: 20 Januari (Akan di-soft-delete)
        var toDelete = await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 20),
            Description = "Modal deleted",
            Lines = new() { new() { AccountId = kasId, Debit = 20_000_000m }, new() { AccountId = modalId, Credit = 20_000_000m } }
        });
        await journalService.DeleteAsync(toDelete.Value!.Id);

        // Transaksi 3: 15 Februari (Masa depan terhadap per 31 Januari)
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 2, 15),
            Description = "Modal masa depan",
            Lines = new() { new() { AccountId = kasId, Debit = 30_000_000m }, new() { AccountId = modalId, Credit = 30_000_000m } }
        });

        var reportService = CreateReportService(Roles.Admin);
        var report = await reportService.GetBalanceSheetAsync(new DateTime(2026, 1, 31));

        Assert.True(report.IsSuccess);
        // Hanya 50 jt dari transaksi 1 yang masuk per 31 Januari
        Assert.Equal(50_000_000m, report.Value!.TotalAssets);
        Assert.Equal(50_000_000m, report.Value.TotalEquity);
    }

    #endregion

    #region GetIncomeStatementAsync Tests

    [Fact]
    public async Task IncomeStatement_MultiTierProfit_CalculatedAccurately()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var piutangId = accounts["1-1003"];
        var persediaanId = accounts["1-1004"];
        var utangId = accounts["2-1001"];
        var penjualanId = accounts["4-1001"];
        var returPenjualanId = accounts["4-1002"];
        var pendapatanLainId = accounts["4-2001"];
        var hppId = accounts["5-1001"];
        var returPembelianId = accounts["5-1002"];
        var bebanGajiId = accounts["6-1001"];
        var bebanListrikId = accounts["6-1003"];
        var bebanLainId = accounts["7-1001"];

        var journalService = CreateJournalService(Roles.Admin);

        // 1. Penjualan: Penjualan 30 jt (kredit), Kas 30 jt (debit)
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 5),
            Description = "Penjualan aki",
            Lines = new() { new() { AccountId = kasId, Debit = 30_000_000m }, new() { AccountId = penjualanId, Credit = 30_000_000m } }
        });

        // 2. Retur Penjualan: Retur 2 jt (debit), Kas 2 jt (kredit)
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 8),
            Description = "Retur penjualan aki rusak",
            Lines = new() { new() { AccountId = returPenjualanId, Debit = 2_000_000m }, new() { AccountId = kasId, Credit = 2_000_000m } }
        });

        // 3. HPP: HPP 18 jt (debit), Persediaan 18 jt (kredit)
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 5),
            Description = "Pengakuan HPP penjualan",
            Lines = new() { new() { AccountId = hppId, Debit = 18_000_000m }, new() { AccountId = persediaanId, Credit = 18_000_000m } }
        });

        // 4. Retur Pembelian: Utang 1 jt (debit), Retur Pembelian 1 jt (kredit)
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 9),
            Description = "Retur pembelian ke supplier aki",
            Lines = new() { new() { AccountId = utangId, Debit = 1_000_000m }, new() { AccountId = returPembelianId, Credit = 1_000_000m } }
        });

        // 5. Beban Operasional: Gaji 4 jt, Listrik 1 jt
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 15),
            Description = "Beban gaji dan listrik",
            Lines = new()
            {
                new() { AccountId = bebanGajiId, Debit = 4_000_000m },
                new() { AccountId = bebanListrikId, Debit = 1_000_000m },
                new() { AccountId = kasId, Credit = 5_000_000m }
            }
        });

        // 6. Pendapatan Lain-lain: 800 rb
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 20),
            Description = "Pendapatan jasa kurir/pasang aki",
            Lines = new() { new() { AccountId = kasId, Debit = 800_000m }, new() { AccountId = pendapatanLainId, Credit = 800_000m } }
        });

        // 7. Beban Lain-lain: 300 rb
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 22),
            Description = "Beban administrasi bank & lain-lain",
            Lines = new() { new() { AccountId = bebanLainId, Debit = 300_000m }, new() { AccountId = kasId, Credit = 300_000m } }
        });

        var reportService = CreateReportService(Roles.Admin);
        var result = await reportService.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

        Assert.True(result.IsSuccess, result.Error);
        var isDto = result.Value!;

        // 1. Revenue: Penjualan (30 jt) - Retur Penjualan (2 jt) = 28 jt
        Assert.Equal(28_000_000m, isDto.Revenue.Total);

        // 2. COGS: HPP (18 jt) - Retur Pembelian (1 jt) = 17 jt
        Assert.Equal(17_000_000m, isDto.CostOfGoodsSold.Total);

        // 3. Gross Profit: 28 jt - 17 jt = 11 jt
        Assert.Equal(11_000_000m, isDto.GrossProfit);

        // 4. Operating Expenses: Gaji 4 jt + Listrik 1 jt = 5 jt
        Assert.Equal(5_000_000m, isDto.OperatingExpenses.Total);

        // 5. Operating Profit: 11 jt - 5 jt = 6 jt
        Assert.Equal(6_000_000m, isDto.OperatingProfit);

        // 6. Other Revenue: 800 rb & Other Expenses: 300 rb
        Assert.Equal(800_000m, isDto.OtherRevenue.Total);
        Assert.Equal(300_000m, isDto.OtherExpenses.Total);

        // 7. Net Income: 6 jt + 800 rb - 300 rb = 6_500_000m
        Assert.Equal(6_500_000m, isDto.NetIncome);
    }

    [Fact]
    public async Task IncomeStatement_DateFilter_ExcludesTransactionsOutsidePeriod()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var penjualanId = accounts["4-1001"];
        var journalService = CreateJournalService(Roles.Admin);

        // Januari
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 15),
            Description = "Penjualan Januari",
            Lines = new() { new() { AccountId = kasId, Debit = 10_000_000m }, new() { AccountId = penjualanId, Credit = 10_000_000m } }
        });
        // Februari
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 2, 10),
            Description = "Penjualan Februari",
            Lines = new() { new() { AccountId = kasId, Debit = 15_000_000m }, new() { AccountId = penjualanId, Credit = 15_000_000m } }
        });

        var reportService = CreateReportService(Roles.Admin);

        var jan = await reportService.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        var feb = await reportService.GetIncomeStatementAsync(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));

        Assert.True(jan.IsSuccess);
        Assert.True(feb.IsSuccess);
        Assert.Equal(10_000_000m, jan.Value!.Revenue.Total);
        Assert.Equal(15_000_000m, feb.Value!.Revenue.Total);
    }

    #endregion

    #region GetCashFlowAsync Tests

    [Fact]
    public async Task CashFlow_OpeningCash_Categorization_AndReconciliation()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var bankId = accounts["1-1002"];
        var peralatanId = accounts["1-2001"];
        var utangBankId = accounts["2-2001"];
        var modalId = accounts["3-1001"];
        var priveId = accounts["3-1002"];
        var penjualanId = accounts["4-1001"];
        var bebanSewaId = accounts["6-1002"];

        var journalService = CreateJournalService(Roles.Admin);

        // --- Transaksi SEBELUM periode (1 Januari 2026) -> Membentuk OpeningCash ---
        var initRes = await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 1),
            Description = "Setoran modal awal",
            Lines = new()
            {
                new() { AccountId = kasId, Debit = 30_000_000m },
                new() { AccountId = bankId, Debit = 50_000_000m },
                new() { AccountId = modalId, Credit = 80_000_000m }
            }
        });
        Assert.True(initRes.IsSuccess, initRes.Error);

        // --- Transaksi DALAM periode (Januari 2026) ---

        // 1. Aktivitas Operasi:
        //    a. Kas masuk dari Penjualan 20 jt
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 5),
            Description = "Penjualan tunai",
            Lines = new() { new() { AccountId = kasId, Debit = 20_000_000m }, new() { AccountId = penjualanId, Credit = 20_000_000m } }
        });
        //    b. Kas keluar bayar Beban Sewa 4 jt
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 7),
            Description = "Bayar sewa toko",
            Lines = new() { new() { AccountId = bebanSewaId, Debit = 4_000_000m }, new() { AccountId = kasId, Credit = 4_000_000m } }
        });

        // 2. Aktivitas Investasi:
        //    Beli Peralatan Toko 15 jt pakai Bank (AsetTetap)
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 10),
            Description = "Beli peralatan toko via transfer bank",
            Lines = new() { new() { AccountId = peralatanId, Debit = 15_000_000m }, new() { AccountId = bankId, Credit = 15_000_000m } }
        });

        // 3. Aktivitas Pendanaan:
        //    a. Terima pinjaman Bank 25 jt ke rekening Bank (UtangBank: LiabilitasJangkaPanjang)
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 12),
            Description = "Pencairan kredit usaha bank",
            Lines = new() { new() { AccountId = bankId, Debit = 25_000_000m }, new() { AccountId = utangBankId, Credit = 25_000_000m } }
        });
        //    b. Prive pemilik kas 5 jt (Ekuitas)
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 20),
            Description = "Prive pemilik",
            Lines = new() { new() { AccountId = priveId, Debit = 5_000_000m }, new() { AccountId = kasId, Credit = 5_000_000m } }
        });

        // 4. Transfer internal Kas ke Bank (10 jt) -> Net cash change = 0, diabaikan dari aktivitas
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 25),
            Description = "Setor tunai kas ke bank toko",
            Lines = new() { new() { AccountId = bankId, Debit = 10_000_000m }, new() { AccountId = kasId, Credit = 10_000_000m } }
        });

        var reportService = CreateReportService(Roles.Admin);
        var result = await reportService.GetCashFlowAsync(new DateTime(2026, 1, 5), new DateTime(2026, 1, 31));

        Assert.True(result.IsSuccess, result.Error);
        var cf = result.Value!;

        // 1. Saldo Awal Kas (Kas 30 jt + Bank 50 jt = 80 jt)
        Assert.Equal(80_000_000m, cf.OpeningCash);

        // 2. Operasi: +20 jt (penjualan) - 4 jt (sewa) = +16 jt
        Assert.Equal(16_000_000m, cf.OperatingActivities.Total);

        // 3. Investasi: -15 jt (beli peralatan)
        Assert.Equal(-15_000_000m, cf.InvestingActivities.Total);

        // 4. Pendanaan: +25 jt (utang bank) - 5 jt (prive) = +20 jt
        Assert.Equal(20_000_000m, cf.FinancingActivities.Total);

        // 5. Net Cash Change = 16 - 15 + 20 = 21 jt
        Assert.Equal(21_000_000m, cf.NetCashChange);

        // 6. Closing Cash = 80 + 21 = 101 jt
        Assert.Equal(101_000_000m, cf.ClosingCash);
        Assert.Equal(cf.ClosingCash, cf.OpeningCash + cf.NetCashChange);

        // 7. Rekonsiliasi dengan Saldo Riil Kas & Bank per 31 Jan
        await using var verifyContext = _db.CreateContext();
        var kasBalance = (await verifyContext.Accounts.SingleAsync(a => a.Id == kasId)).Balance;
        var bankBalance = (await verifyContext.Accounts.SingleAsync(a => a.Id == bankId)).Balance;
        Assert.Equal(cf.ClosingCash, kasBalance + bankBalance);
    }

    [Fact]
    public async Task CashFlow_NonCashJournal_ExcludedFromActivities()
    {
        var accounts = await SeedFixtureAsync();
        var bebanPenyusutanId = accounts["6-1004"];
        var akumPenyusutanId = accounts["1-2002"];
        var journalService = CreateJournalService(Roles.Admin);

        // Jurnal non-kas: beban penyusutan vs akumulasi penyusutan
        await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 15),
            Description = "Penyusutan peralatan bulanan",
            Lines = new() { new() { AccountId = bebanPenyusutanId, Debit = 1_000_000m }, new() { AccountId = akumPenyusutanId, Credit = 1_000_000m } }
        });

        var reportService = CreateReportService(Roles.Admin);
        var result = await reportService.GetCashFlowAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value!.NetCashChange);
        Assert.Empty(result.Value.OperatingActivities.Lines);
        Assert.Empty(result.Value.InvestingActivities.Lines);
        Assert.Empty(result.Value.FinancingActivities.Lines);
    }

    #endregion
}

