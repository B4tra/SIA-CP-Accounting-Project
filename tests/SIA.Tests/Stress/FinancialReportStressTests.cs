using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.Accounting;
using SIA.Application.DTOs.Reporting;
using SIA.Application.Services.Accounting;
using SIA.Application.Services.Reporting;
using SIA.Domain.Entities.Accounting;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data;
using SIA.Infrastructure.Data.Seed;
using SIA.Infrastructure.Identity;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;
using SIA.Tests.Services;
using Xunit;

namespace SIA.Tests.Stress;

/// <summary>
/// EMPIRICAL ADVERSARIAL STRESS TEST HARNESS FOR FinancialReportService
/// Challenges:
/// - Balance Sheet: Multi-period progression, contra-accounts (accumulated depreciation, drawings),
///   depreciation exceeding asset cost, drawings exceeding equity, severe net loss, manual year-end closing.
/// - Income Statement: Sales returns, purchase returns, negative net revenue, negative net COGS,
///   multi-tier margins, operating loss offset by other revenue, empty periods, single-day reporting, quarterly additivity.
/// - Cash Flow Statement: Cash & Bank detection (standard + custom), opening cash accumulation & deficit,
///   activity categorization (operating, investing, financing), Kas <-> Bank internal transfers,
///   compound transfers with admin fees, multi-offset compound journals, and 3-way reconciliation.
/// - Extreme Stress: High-volume 100-journal sequence, 1 Trillion IDR extreme scale, authorization matrix.
/// </summary>
public class FinancialReportStressTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private Dictionary<string, int>? _accountCodeMap;

    public void Dispose() => _db.Dispose();

    private async Task InitializeAsync()
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

        var accounts = await context.Accounts.AsNoTracking().ToListAsync();
        _accountCodeMap = accounts.ToDictionary(a => a.Code, a => a.Id);
    }

    private int GetAccId(string code) => _accountCodeMap![code];

    private (IJournalService Journal, IFinancialReportService Report) CreateServices(string role = Roles.Admin)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(role);
        var numService = new TransactionNumberService(context, user);
        var journalService = new JournalService(context, user, numService);
        var reportService = new FinancialReportService(context, user);
        return (journalService, reportService);
    }

    private static CreateJournalEntryDto MakeJournal(DateTime date, string desc, params (int accountId, decimal debit, decimal credit)[] lines)
    {
        return new CreateJournalEntryDto
        {
            TransactionDate = date,
            Description = desc,
            Lines = lines.Select(l => new CreateJournalEntryLineDto
            {
                AccountId = l.accountId,
                Debit = l.debit,
                Credit = l.credit
            }).ToList()
        };
    }

    // =========================================================================
    // SECTION 1: BALANCE SHEET STRESS TESTS
    // =========================================================================

    [Fact]
    public async Task Stress_BalanceSheet_MultiPeriod_QuarterlyProgression()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var bank = GetAccId("1-1002");
        var piutang = GetAccId("1-1003");
        var peralatan = GetAccId("1-2001");
        var utang = GetAccId("2-1001");
        var modal = GetAccId("3-1001");
        var prive = GetAccId("3-1002");
        var penjualan = GetAccId("4-1001");
        var hpp = GetAccId("5-1001");
        var bebanGaji = GetAccId("6-1001");

        // Q1 (Jan - Mar 2026): Capital + Equipment purchase + initial sale
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 10), "Setoran Modal", (bank, 100_000_000m, 0m), (modal, 0m, 100_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 2, 1), "Beli Peralatan", (peralatan, 30_000_000m, 0m), (bank, 0m, 30_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 3, 15), "Penjualan Q1", (kas, 20_000_000m, 0m), (penjualan, 0m, 20_000_000m)));

        var bsQ1 = (await report.GetBalanceSheetAsync(new DateTime(2026, 3, 31))).Value!;
        Assert.Equal(bsQ1.TotalAssets, bsQ1.TotalLiabilities + bsQ1.TotalEquity);
        Assert.Equal(120_000_000m, bsQ1.TotalAssets); // Bank 70M + Peralatan 30M + Kas 20M = 120M
        Assert.Equal(0m, bsQ1.TotalLiabilities);
        Assert.Equal(100_000_000m, bsQ1.EquitySections.Sum(s => s.Total));
        Assert.Equal(20_000_000m, bsQ1.CurrentPeriodNetIncome);
        Assert.Equal(120_000_000m, bsQ1.TotalEquity);

        // Q2 (Apr - Jun 2026): Inventory procurement on credit + credit sale + wage expense
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 4, 10), "Utang Pembelian", (hpp, 15_000_000m, 0m), (utang, 0m, 15_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 5, 12), "Penjualan Tempo", (piutang, 25_000_000m, 0m), (penjualan, 0m, 25_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 6, 25), "Bayar Gaji", (bebanGaji, 5_000_000m, 0m), (bank, 0m, 5_000_000m)));

        var bsQ2 = (await report.GetBalanceSheetAsync(new DateTime(2026, 6, 30))).Value!;
        Assert.Equal(bsQ2.TotalAssets, bsQ2.TotalLiabilities + bsQ2.TotalEquity);
        Assert.Equal(140_000_000m, bsQ2.TotalAssets); // Bank 65M + Kas 20M + Piutang 25M + Peralatan 30M = 140M
        Assert.Equal(15_000_000m, bsQ2.TotalLiabilities);
        Assert.Equal(25_000_000m, bsQ2.CurrentPeriodNetIncome); // Rev 45M - Exp 20M = 25M
        Assert.Equal(125_000_000m, bsQ2.TotalEquity); // Modal 100M + NetIncome 25M = 125M

        // Q3 (Jul - Sep 2026): Customer settlement + Supplier payment + Prive withdrawal
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 7, 5), "Pelunasan Piutang", (bank, 25_000_000m, 0m), (piutang, 0m, 25_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 8, 10), "Pelunasan Utang", (utang, 10_000_000m, 0m), (bank, 0m, 10_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 9, 20), "Prive Pemilik", (prive, 8_000_000m, 0m), (kas, 0m, 8_000_000m)));

        var bsQ3 = (await report.GetBalanceSheetAsync(new DateTime(2026, 9, 30))).Value!;
        Assert.Equal(bsQ3.TotalAssets, bsQ3.TotalLiabilities + bsQ3.TotalEquity);
        Assert.Equal(122_000_000m, bsQ3.TotalAssets); // Bank 80M + Kas 12M + Piutang 0 + Peralatan 30M = 122M
        Assert.Equal(5_000_000m, bsQ3.TotalLiabilities); // Utang 5M
        Assert.Equal(92_000_000m, bsQ3.EquitySections.Sum(s => s.Total)); // Modal 100M - Prive 8M = 92M
        Assert.Equal(25_000_000m, bsQ3.CurrentPeriodNetIncome);
        Assert.Equal(117_000_000m, bsQ3.TotalEquity); // 92M + 25M = 117M. 5M + 117M = 122M!
    }

    [Fact]
    public async Task Stress_BalanceSheet_ContraAccount_AccumulatedDepreciation_DeductsFromAssets()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var bank = GetAccId("1-1002");
        var peralatan = GetAccId("1-2001");
        var akumPenyusutan = GetAccId("1-2002");
        var modal = GetAccId("3-1001");
        var bebanPenyusutan = GetAccId("6-1004");

        // Modal 50M, Beli Peralatan 50M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 1), "Modal", (bank, 50_000_000m, 0m), (modal, 0m, 50_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 2), "Beli Peralatan", (peralatan, 50_000_000m, 0m), (bank, 0m, 50_000_000m)));

        // Penyusutan 3 bulan berturut-turut masing-masing 5jt
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 31), "Penyusutan Jan", (bebanPenyusutan, 5_000_000m, 0m), (akumPenyusutan, 0m, 5_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 2, 28), "Penyusutan Feb", (bebanPenyusutan, 5_000_000m, 0m), (akumPenyusutan, 0m, 5_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 3, 31), "Penyusutan Mar", (bebanPenyusutan, 5_000_000m, 0m), (akumPenyusutan, 0m, 5_000_000m)));

        var bs = (await report.GetBalanceSheetAsync(new DateTime(2026, 3, 31))).Value!;

        var fixedAssetSection = bs.AssetSections.First(s => s.Title == "Aset Tetap");
        var akumLine = fixedAssetSection.Lines.First(l => l.AccountCode == "1-2002");

        // Akumulasi penyusutan harus negatif pada aset
        Assert.Equal(-15_000_000m, akumLine.Amount);
        Assert.Equal(35_000_000m, fixedAssetSection.Total); // 50M - 15M = 35M
        Assert.Equal(35_000_000m, bs.TotalAssets); // Bank 0 + Aset Tetap 35M = 35M
        Assert.Equal(-15_000_000m, bs.CurrentPeriodNetIncome); // Beban 15M -> Net Loss -15M
        Assert.Equal(35_000_000m, bs.TotalEquity); // Modal 50M - 15M = 35M
        Assert.Equal(bs.TotalAssets, bs.TotalLiabilities + bs.TotalEquity);
    }

    [Fact]
    public async Task Stress_BalanceSheet_ContraAccount_DepreciationExceedsAssetCost_Impairment()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var bank = GetAccId("1-1002");
        var peralatan = GetAccId("1-2001");
        var akumPenyusutan = GetAccId("1-2002");
        var modal = GetAccId("3-1001");
        var bebanPenyusutan = GetAccId("6-1004");

        // Modal 20M, Beli Peralatan 10M (Sisa Bank 10M)
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 1), "Modal", (bank, 20_000_000m, 0m), (modal, 0m, 20_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 2), "Beli Peralatan", (peralatan, 10_000_000m, 0m), (bank, 0m, 10_000_000m)));

        // Penyusutan ekstrim 15jt (melebihi nilai aset 10jt)
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 31), "Penyusutan Ekstrim", (bebanPenyusutan, 15_000_000m, 0m), (akumPenyusutan, 0m, 15_000_000m)));

        var bs = (await report.GetBalanceSheetAsync(new DateTime(2026, 1, 31))).Value!;
        var fixedAssetSection = bs.AssetSections.First(s => s.Title == "Aset Tetap");

        Assert.Equal(-5_000_000m, fixedAssetSection.Total); // 10M - 15M = -5M
        Assert.Equal(5_000_000m, bs.TotalAssets); // Bank 10M + (-5M) = 5M
        Assert.Equal(-15_000_000m, bs.CurrentPeriodNetIncome);
        Assert.Equal(5_000_000m, bs.TotalEquity); // Modal 20M - 15M = 5M
        Assert.Equal(bs.TotalAssets, bs.TotalLiabilities + bs.TotalEquity);
    }

    [Fact]
    public async Task Stress_BalanceSheet_ContraAccount_OwnerDrawings_DeductsFromEquity()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var modal = GetAccId("3-1001");
        var prive = GetAccId("3-1002");

        // Modal 40M, Prive 15M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 1), "Modal", (kas, 40_000_000m, 0m), (modal, 0m, 40_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 15), "Prive", (prive, 15_000_000m, 0m), (kas, 0m, 15_000_000m)));

        var bs = (await report.GetBalanceSheetAsync(new DateTime(2026, 1, 31))).Value!;
        var equitySection = bs.EquitySections.First();
        var priveLine = equitySection.Lines.First(l => l.AccountCode == "3-1002");

        Assert.Equal(-15_000_000m, priveLine.Amount); // Prive harus negatif pada ekuitas
        Assert.Equal(25_000_000m, equitySection.Total); // 40M - 15M = 25M
        Assert.Equal(25_000_000m, bs.TotalAssets); // Kas 25M
        Assert.Equal(25_000_000m, bs.TotalEquity);
        Assert.Equal(bs.TotalAssets, bs.TotalLiabilities + bs.TotalEquity);
    }

    [Fact]
    public async Task Stress_BalanceSheet_ContraAccount_DrawingsExceedCapital_CapitalDeficit()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var utang = GetAccId("2-1001");
        var modal = GetAccId("3-1001");
        var prive = GetAccId("3-1002");

        // Modal 10M, Utang 30M -> Kas 40M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 1), "Modal", (kas, 10_000_000m, 0m), (modal, 0m, 10_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 2), "Utang", (kas, 30_000_000m, 0m), (utang, 0m, 30_000_000m)));

        // Prive 25M (melebihi modal 10M)
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 10), "Prive Besar", (prive, 25_000_000m, 0m), (kas, 0m, 25_000_000m)));

        var bs = (await report.GetBalanceSheetAsync(new DateTime(2026, 1, 31))).Value!;
        var equitySection = bs.EquitySections.First();

        Assert.Equal(-15_000_000m, equitySection.Total); // Modal 10M - Prive 25M = -15M (Defisit)
        Assert.Equal(15_000_000m, bs.TotalAssets); // Kas 40M - 25M = 15M
        Assert.Equal(30_000_000m, bs.TotalLiabilities); // Utang 30M
        Assert.Equal(-15_000_000m, bs.TotalEquity); // Ekuitas defisit -15M
        Assert.Equal(bs.TotalAssets, bs.TotalLiabilities + bs.TotalEquity); // 15M == 30M + (-15M) = 15M!
    }

    [Fact]
    public async Task Stress_BalanceSheet_SevereNetLoss_DecreasesTotalEquity()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var modal = GetAccId("3-1001");
        var penjualan = GetAccId("4-1001");
        var bebanSewa = GetAccId("6-1002");

        // Modal 100M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 1), "Modal", (kas, 100_000_000m, 0m), (modal, 0m, 100_000_000m)));

        // Penjualan 20M, Beban Sewa 70M (Rugi Bersih 50M)
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 10), "Penjualan", (kas, 20_000_000m, 0m), (penjualan, 0m, 20_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 20), "Beban Sewa", (bebanSewa, 70_000_000m, 0m), (kas, 0m, 70_000_000m)));

        var bs = (await report.GetBalanceSheetAsync(new DateTime(2026, 1, 31))).Value!;

        Assert.Equal(50_000_000m, bs.TotalAssets); // Kas 100M + 20M - 70M = 50M
        Assert.Equal(-50_000_000m, bs.CurrentPeriodNetIncome);
        Assert.Equal(50_000_000m, bs.TotalEquity); // Modal 100M - Rugi 50M = 50M
        Assert.Equal(bs.TotalAssets, bs.TotalLiabilities + bs.TotalEquity);
    }

    [Fact]
    public async Task Stress_BalanceSheet_ManualYearEndClosingEntry_TransfersIncomeToEquity()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var modal = GetAccId("3-1001");
        var penjualan = GetAccId("4-1001");
        var bebanGaji = GetAccId("6-1001");

        // Modal 50M, Penjualan 30M, Beban Gaji 10M -> Laba Bersih 20M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 1), "Modal", (kas, 50_000_000m, 0m), (modal, 0m, 50_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 6, 1), "Penjualan", (kas, 30_000_000m, 0m), (penjualan, 0m, 30_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 6, 2), "Gaji", (bebanGaji, 10_000_000m, 0m), (kas, 0m, 10_000_000m)));

        var bsBeforeClose = (await report.GetBalanceSheetAsync(new DateTime(2026, 12, 30))).Value!;
        Assert.Equal(20_000_000m, bsBeforeClose.CurrentPeriodNetIncome);
        Assert.Equal(50_000_000m, bsBeforeClose.EquitySections.Sum(s => s.Total));
        Assert.Equal(70_000_000m, bsBeforeClose.TotalEquity);

        // JURNAL PENUTUP MANUAL (Closing Entry):
        // Debit Penjualan 30M, Kredit Beban Gaji 10M, Kredit Modal Pemilik 20M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 12, 31), "Jurnal Penutup Akhir Tahun",
            (penjualan, 30_000_000m, 0m),
            (bebanGaji, 0m, 10_000_000m),
            (modal, 0m, 20_000_000m)));

        var bsAfterClose = (await report.GetBalanceSheetAsync(new DateTime(2026, 12, 31))).Value!;
        Assert.Equal(0m, bsAfterClose.CurrentPeriodNetIncome); // Laba periode berjalan nol karena sudah ditutup
        Assert.Equal(70_000_000m, bsAfterClose.EquitySections.Sum(s => s.Total)); // Modal bertambah menjadi 70M
        Assert.Equal(70_000_000m, bsAfterClose.TotalEquity);
        Assert.Equal(70_000_000m, bsAfterClose.TotalAssets);
        Assert.Equal(bsAfterClose.TotalAssets, bsAfterClose.TotalLiabilities + bsAfterClose.TotalEquity);
    }

    // =========================================================================
    // SECTION 2: INCOME STATEMENT STRESS TESTS
    // =========================================================================

    [Fact]
    public async Task Stress_IncomeStatement_SalesReturn_ReducesRevenue()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var penjualan = GetAccId("4-1001");
        var returPenjualan = GetAccId("4-1002");

        // Penjualan 50jt, Retur Penjualan 12jt
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 2, 5), "Penjualan", (kas, 50_000_000m, 0m), (penjualan, 0m, 50_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 2, 10), "Retur Penjualan", (returPenjualan, 12_000_000m, 0m), (kas, 0m, 12_000_000m)));

        var lr = (await report.GetIncomeStatementAsync(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28))).Value!;

        var returLine = lr.Revenue.Lines.First(l => l.AccountCode == "4-1002");
        Assert.Equal(-12_000_000m, returLine.Amount); // Retur penjualan harus negatif pada pendapatan
        Assert.Equal(38_000_000m, lr.Revenue.Total); // 50M - 12M = 38M
        Assert.Equal(38_000_000m, lr.GrossProfit);
        Assert.Equal(38_000_000m, lr.NetIncome);
    }

    [Fact]
    public async Task Stress_IncomeStatement_SalesReturnExceedsSales_NegativeRevenue()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var penjualan = GetAccId("4-1001");
        var returPenjualan = GetAccId("4-1002");

        // Penjualan 5jt, Retur 15jt (retur barang dari periode lalu) -> Pendapatan bersih -10jt
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 3, 5), "Penjualan Kecil", (kas, 5_000_000m, 0m), (penjualan, 0m, 5_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 3, 10), "Retur Besar", (returPenjualan, 15_000_000m, 0m), (kas, 0m, 15_000_000m)));

        var lr = (await report.GetIncomeStatementAsync(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31))).Value!;

        Assert.Equal(-10_000_000m, lr.Revenue.Total); // Pendapatan bersih negatif
        Assert.Equal(-10_000_000m, lr.GrossProfit);
        Assert.Equal(-10_000_000m, lr.NetIncome);
    }

    [Fact]
    public async Task Stress_IncomeStatement_PurchaseReturn_ReducesCOGS()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var hpp = GetAccId("5-1001");
        var returPembelian = GetAccId("5-1002");

        // HPP 30jt, Retur Pembelian 8jt
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 4, 2), "HPP", (hpp, 30_000_000m, 0m), (kas, 0m, 30_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 4, 8), "Retur Pembelian", (kas, 8_000_000m, 0m), (returPembelian, 0m, 8_000_000m)));

        var lr = (await report.GetIncomeStatementAsync(new DateTime(2026, 4, 1), new DateTime(2026, 4, 30))).Value!;

        var returLine = lr.CostOfGoodsSold.Lines.First(l => l.AccountCode == "5-1002");
        Assert.Equal(-8_000_000m, returLine.Amount); // Retur pembelian harus negatif pada HPP
        Assert.Equal(22_000_000m, lr.CostOfGoodsSold.Total); // 30M - 8M = 22M
        Assert.Equal(-22_000_000m, lr.GrossProfit);
        Assert.Equal(-22_000_000m, lr.NetIncome);
    }

    [Fact]
    public async Task Stress_IncomeStatement_PurchaseReturnExceedsCOGS_NegativeCOGS()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var hpp = GetAccId("5-1001");
        var returPembelian = GetAccId("5-1002");

        // HPP 4jt, Retur Pembelian 10jt -> HPP bersih -6jt
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 5, 2), "HPP Kecil", (hpp, 4_000_000m, 0m), (kas, 0m, 4_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 5, 8), "Retur Pembelian Besar", (kas, 10_000_000m, 0m), (returPembelian, 0m, 10_000_000m)));

        var lr = (await report.GetIncomeStatementAsync(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31))).Value!;

        Assert.Equal(-6_000_000m, lr.CostOfGoodsSold.Total);
        Assert.Equal(6_000_000m, lr.GrossProfit); // 0 - (-6M) = +6M!
        Assert.Equal(6_000_000m, lr.NetIncome);
    }

    [Fact]
    public async Task Stress_IncomeStatement_MultiTierProfit_AllTiersActive()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var penjualan = GetAccId("4-1001");
        var returPenjualan = GetAccId("4-1002");
        var pendLain = GetAccId("4-2001");
        var hpp = GetAccId("5-1001");
        var returBeli = GetAccId("5-1002");
        var bebanGaji = GetAccId("6-1001");
        var bebanSewa = GetAccId("6-1002");
        var bebanListrik = GetAccId("6-1003");
        var bebanLain = GetAccId("7-1001");

        // Pendapatan: Penjualan 100M, Retur 10M -> Net Rev = 90M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 6, 1), "Penjualan", (kas, 100_000_000m, 0m), (penjualan, 0m, 100_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 6, 2), "Retur Jual", (returPenjualan, 10_000_000m, 0m), (kas, 0m, 10_000_000m)));

        // HPP: 60M, Retur Beli 5M -> Net HPP = 55M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 6, 3), "HPP", (hpp, 60_000_000m, 0m), (kas, 0m, 60_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 6, 4), "Retur Beli", (kas, 5_000_000m, 0m), (returBeli, 0m, 5_000_000m)));

        // Beban Operasional: Gaji 15M, Sewa 5M, Listrik 2M -> Total Beban Ops = 22M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 6, 10), "Gaji", (bebanGaji, 15_000_000m, 0m), (kas, 0m, 15_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 6, 11), "Sewa", (bebanSewa, 5_000_000m, 0m), (kas, 0m, 5_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 6, 12), "Listrik", (bebanListrik, 2_000_000m, 0m), (kas, 0m, 2_000_000m)));

        // Non-operasional: Pendapatan Lain 8M, Beban Lain 3M -> Net Other = +5M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 6, 20), "Pendapatan Bunga", (kas, 8_000_000m, 0m), (pendLain, 0m, 8_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 6, 25), "Biaya Adm Bank", (bebanLain, 3_000_000m, 0m), (kas, 0m, 3_000_000m)));

        var lr = (await report.GetIncomeStatementAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30))).Value!;

        Assert.Equal(90_000_000m, lr.Revenue.Total);
        Assert.Equal(55_000_000m, lr.CostOfGoodsSold.Total);
        Assert.Equal(35_000_000m, lr.GrossProfit); // 90M - 55M = 35M
        Assert.Equal(22_000_000m, lr.OperatingExpenses.Total);
        Assert.Equal(13_000_000m, lr.OperatingProfit); // 35M - 22M = 13M
        Assert.Equal(8_000_000m, lr.OtherRevenue.Total);
        Assert.Equal(3_000_000m, lr.OtherExpenses.Total);
        Assert.Equal(18_000_000m, lr.NetIncome); // 13M + 8M - 3M = 18M
    }

    [Fact]
    public async Task Stress_IncomeStatement_OperatingLoss_OffsetByOtherRevenue()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var penjualan = GetAccId("4-1001");
        var pendLain = GetAccId("4-2001");
        var bebanGaji = GetAccId("6-1001");

        // Penjualan 10M, Gaji 25M -> Rugi Operasional -15M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 7, 1), "Penjualan", (kas, 10_000_000m, 0m), (penjualan, 0m, 10_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 7, 2), "Gaji", (bebanGaji, 25_000_000m, 0m), (kas, 0m, 25_000_000m)));

        // Pendapatan Lain-lain (penjualan rongsok aki bekas) 20M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 7, 15), "Rongsok", (kas, 20_000_000m, 0m), (pendLain, 0m, 20_000_000m)));

        var lr = (await report.GetIncomeStatementAsync(new DateTime(2026, 7, 1), new DateTime(2026, 7, 31))).Value!;

        Assert.Equal(10_000_000m, lr.GrossProfit);
        Assert.Equal(25_000_000m, lr.OperatingExpenses.Total);
        Assert.Equal(-15_000_000m, lr.OperatingProfit); // Rugi Operasi -15M
        Assert.Equal(20_000_000m, lr.OtherRevenue.Total);
        Assert.Equal(5_000_000m, lr.NetIncome); // -15M + 20M = +5M Laba Bersih!
    }

    [Fact]
    public async Task Stress_IncomeStatement_ZeroActivityPeriod_ReturnsCleanZeros()
    {
        await InitializeAsync();
        var (_, report) = CreateServices(Roles.Admin);

        var lr = (await report.GetIncomeStatementAsync(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31))).Value!;

        Assert.Equal(0m, lr.Revenue.Total);
        Assert.Equal(0m, lr.CostOfGoodsSold.Total);
        Assert.Equal(0m, lr.GrossProfit);
        Assert.Equal(0m, lr.OperatingExpenses.Total);
        Assert.Equal(0m, lr.OperatingProfit);
        Assert.Equal(0m, lr.OtherRevenue.Total);
        Assert.Equal(0m, lr.OtherExpenses.Total);
        Assert.Equal(0m, lr.NetIncome);
    }

    [Fact]
    public async Task Stress_IncomeStatement_QuarterlyAdditivity()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var penjualan = GetAccId("4-1001");
        var bebanGaji = GetAccId("6-1001");

        // Jan: Penjualan 10M, Gaji 3M -> Net 7M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 15), "Jan Jual", (kas, 10_000_000m, 0m), (penjualan, 0m, 10_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 28), "Jan Gaji", (bebanGaji, 3_000_000m, 0m), (kas, 0m, 3_000_000m)));

        // Feb: Penjualan 15M, Gaji 4M -> Net 11M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 2, 15), "Feb Jual", (kas, 15_000_000m, 0m), (penjualan, 0m, 15_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 2, 28), "Feb Gaji", (bebanGaji, 4_000_000m, 0m), (kas, 0m, 4_000_000m)));

        // Mar: Penjualan 20M, Gaji 5M -> Net 15M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 3, 15), "Mar Jual", (kas, 20_000_000m, 0m), (penjualan, 0m, 20_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 3, 28), "Mar Gaji", (bebanGaji, 5_000_000m, 0m), (kas, 0m, 5_000_000m)));

        var lrJan = (await report.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31))).Value!;
        var lrFeb = (await report.GetIncomeStatementAsync(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28))).Value!;
        var lrMar = (await report.GetIncomeStatementAsync(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31))).Value!;
        var lrQ1 = (await report.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 3, 31))).Value!;

        Assert.Equal(7_000_000m, lrJan.NetIncome);
        Assert.Equal(11_000_000m, lrFeb.NetIncome);
        Assert.Equal(15_000_000m, lrMar.NetIncome);
        Assert.Equal(lrJan.NetIncome + lrFeb.NetIncome + lrMar.NetIncome, lrQ1.NetIncome);
        Assert.Equal(33_000_000m, lrQ1.NetIncome);
    }

    // =========================================================================
    // SECTION 3: CASH FLOW STATEMENT STRESS TESTS
    // =========================================================================

    [Fact]
    public async Task Stress_CashFlow_Detection_StandardAndCustomAccounts()
    {
        await InitializeAsync();

        // Tambah akun kas & bank kustom
        await using (var context = _db.CreateContext())
        {
            context.Accounts.Add(new Account { Code = "1-1009", Name = "Kas Kecil Cabang", Category = AccountCategory.AsetLancar, NormalBalance = NormalBalance.Debit, IsActive = true });
            context.Accounts.Add(new Account { Code = "1-1010", Name = "Bank BCA Operasional", Category = AccountCategory.AsetLancar, NormalBalance = NormalBalance.Debit, IsActive = true });
            context.Accounts.Add(new Account { Code = "1-1099", Name = "Piutang Giro Cabang", Category = AccountCategory.AsetLancar, NormalBalance = NormalBalance.Debit, IsActive = true });
            await context.SaveChangesAsync();
        }

        var (journal, report) = CreateServices(Roles.Admin);
        int kasKecilId, bankBcaId, modalId, penjualanId;
        await using (var context = _db.CreateContext())
        {
            kasKecilId = (await context.Accounts.FirstAsync(a => a.Code == "1-1009")).Id;
            bankBcaId = (await context.Accounts.FirstAsync(a => a.Code == "1-1010")).Id;
            modalId = (await context.Accounts.FirstAsync(a => a.Code == "3-1001")).Id;
            penjualanId = (await context.Accounts.FirstAsync(a => a.Code == "4-1001")).Id;
        }

        // Transaksi menyentuh Kas Kecil dan Bank BCA kustom
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 5), "Modal Kas Kecil", (kasKecilId, 5_000_000m, 0m), (modalId, 0m, 5_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 10), "Penjualan via BCA", (bankBcaId, 12_000_000m, 0m), (penjualanId, 0m, 12_000_000m)));

        var cf = (await report.GetCashFlowAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31))).Value!;

        Assert.Equal(0m, cf.OpeningCash);
        Assert.Equal(12_000_000m, cf.OperatingActivities.Total); // Penjualan via BCA
        Assert.Equal(5_000_000m, cf.FinancingActivities.Total); // Modal via Kas Kecil
        Assert.Equal(17_000_000m, cf.NetCashChange);
        Assert.Equal(17_000_000m, cf.ClosingCash);
    }

    [Fact]
    public async Task Stress_CashFlow_OpeningCash_MultiPeriodAccumulation()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var modal = GetAccId("3-1001");
        var penjualan = GetAccId("4-1001");
        var bebanGaji = GetAccId("6-1001");

        // Jan 2026: Setoran modal 50jt
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 10), "Modal Jan", (kas, 50_000_000m, 0m), (modal, 0m, 50_000_000m)));

        // Feb 2026: Penjualan 30jt, Gaji 10jt -> Kas bertambah 20jt
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 2, 15), "Jual Feb", (kas, 30_000_000m, 0m), (penjualan, 0m, 30_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 2, 25), "Gaji Feb", (bebanGaji, 10_000_000m, 0m), (kas, 0m, 10_000_000m)));

        // Mar 2026: Laporan Arus Kas untuk Maret 2026
        // Saldo awal Maret harus sama dengan saldo akhir Februari (50M + 20M = 70M)
        var cfMar = (await report.GetCashFlowAsync(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31))).Value!;

        Assert.Equal(70_000_000m, cfMar.OpeningCash);
        Assert.Equal(0m, cfMar.NetCashChange);
        Assert.Equal(70_000_000m, cfMar.ClosingCash);
    }

    [Fact]
    public async Task Stress_CashFlow_OpeningCash_OverdraftDeficit()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var bank = GetAccId("1-1002");
        var utang = GetAccId("2-1001");

        // Jan 2026: Pembayaran utang 15jt dari bank tanpa saldo awal (overdraft fasilitas cerukan)
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 15), "Overdraft Bank", (utang, 15_000_000m, 0m), (bank, 0m, 15_000_000m)));

        // Feb 2026: Laporan arus kas
        var cfFeb = (await report.GetCashFlowAsync(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28))).Value!;

        Assert.Equal(-15_000_000m, cfFeb.OpeningCash); // Saldo awal negatif
        Assert.Equal(0m, cfFeb.NetCashChange);
        Assert.Equal(-15_000_000m, cfFeb.ClosingCash);
    }

    [Fact]
    public async Task Stress_CashFlow_ActivityCategorization_AllThreeTiers()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var bank = GetAccId("1-1002");
        var piutang = GetAccId("1-1003");
        var peralatan = GetAccId("1-2001");
        var utangUsaha = GetAccId("2-1001");
        var utangBank = GetAccId("2-2001");
        var modal = GetAccId("3-1001");
        var prive = GetAccId("3-1002");
        var penjualan = GetAccId("4-1001");
        var bebanGaji = GetAccId("6-1001");

        // 1. FINANCING:
        // Modal +50M, Pinjaman Bank +30M, Prive -5M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 5, 1), "Modal", (bank, 50_000_000m, 0m), (modal, 0m, 50_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 5, 2), "Pinjaman", (bank, 30_000_000m, 0m), (utangBank, 0m, 30_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 5, 3), "Prive", (prive, 5_000_000m, 0m), (kas, 0m, 5_000_000m)));

        // 2. INVESTING:
        // Beli Peralatan -20M, Jual Peralatan bekas +4M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 5, 5), "Beli Alat", (peralatan, 20_000_000m, 0m), (bank, 0m, 20_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 5, 8), "Jual Alat Bekas", (kas, 4_000_000m, 0m), (peralatan, 0m, 4_000_000m)));

        // 3. OPERATING:
        // Penjualan +15M, Pelunasan Piutang +10M, Bayar Gaji -6M, Bayar Utang Usaha -8M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 5, 10), "Penjualan", (kas, 15_000_000m, 0m), (penjualan, 0m, 15_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 5, 12), "Terima Piutang", (bank, 10_000_000m, 0m), (piutang, 0m, 10_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 5, 15), "Gaji", (bebanGaji, 6_000_000m, 0m), (kas, 0m, 6_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 5, 20), "Bayar Utang", (utangUsaha, 8_000_000m, 0m), (bank, 0m, 8_000_000m)));

        var cf = (await report.GetCashFlowAsync(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31))).Value!;

        // Operating: Penjualan +15M, Piutang +10M, Gaji -6M, Utang Usaha -8M = +11M
        Assert.Equal(11_000_000m, cf.OperatingActivities.Total);

        // Investing: Beli Alat -20M, Jual Alat +4M = -16M
        Assert.Equal(-16_000_000m, cf.InvestingActivities.Total);

        // Financing: Modal +50M, Utang Bank +30M, Prive -5M = +75M
        Assert.Equal(75_000_000m, cf.FinancingActivities.Total);

        // Net Change = 11M - 16M + 75M = 70M
        Assert.Equal(70_000_000m, cf.NetCashChange);
        Assert.Equal(70_000_000m, cf.ClosingCash);
    }

    [Fact]
    public async Task Stress_CashFlow_InternalCashTransfer_KasToBank_ZeroNetChange()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var bank = GetAccId("1-1002");
        var modal = GetAccId("3-1001");

        // Modal 20M ke Kas
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 1), "Modal Kas", (kas, 20_000_000m, 0m), (modal, 0m, 20_000_000m)));

        // Transfer internal Kas ke Bank 12jt
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 15), "Setor Kas ke Bank", (bank, 12_000_000m, 0m), (kas, 0m, 12_000_000m)));

        var cf = (await report.GetCashFlowAsync(new DateTime(2026, 1, 10), new DateTime(2026, 1, 31))).Value!;

        Assert.Equal(20_000_000m, cf.OpeningCash);
        Assert.Empty(cf.OperatingActivities.Lines);
        Assert.Empty(cf.InvestingActivities.Lines);
        Assert.Empty(cf.FinancingActivities.Lines);
        Assert.Equal(0m, cf.NetCashChange);
        Assert.Equal(20_000_000m, cf.ClosingCash);
    }

    [Fact]
    public async Task Stress_CashFlow_InternalCashTransfer_BankToKas_ZeroNetChange()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var bank = GetAccId("1-1002");
        var modal = GetAccId("3-1001");

        // Modal 30M ke Bank
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 1), "Modal Bank", (bank, 30_000_000m, 0m), (modal, 0m, 30_000_000m)));

        // Tarik tunai dari Bank ke Kas 10jt
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 20), "Tarik Tunai", (kas, 10_000_000m, 0m), (bank, 0m, 10_000_000m)));

        var cf = (await report.GetCashFlowAsync(new DateTime(2026, 1, 10), new DateTime(2026, 1, 31))).Value!;

        Assert.Equal(30_000_000m, cf.OpeningCash);
        Assert.Equal(0m, cf.NetCashChange);
        Assert.Equal(30_000_000m, cf.ClosingCash);
    }

    [Fact]
    public async Task Stress_CashFlow_InternalCashTransfer_WithAdminFee_SplitsCorrectly()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var bank = GetAccId("1-1002");
        var modal = GetAccId("3-1001");
        var bebanLain = GetAccId("7-1001");

        // Modal 50M di Kas
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 1), "Modal", (kas, 50_000_000m, 0m), (modal, 0m, 50_000_000m)));

        // Transfer 10M dari Kas ke Bank dengan biaya transfer 6.500:
        // Debit Bank 9.993.500, Debit Beban Lain 6.500, Kredit Kas 10.000.000
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 15), "Transfer Kas ke Bank + Biaya",
            (bank, 9_993_500m, 0m),
            (bebanLain, 6_500m, 0m),
            (kas, 0m, 10_000_000m)));

        var cf = (await report.GetCashFlowAsync(new DateTime(2026, 1, 10), new DateTime(2026, 1, 31))).Value!;

        Assert.Equal(50_000_000m, cf.OpeningCash);
        Assert.Equal(-6_500m, cf.OperatingActivities.Total); // Biaya transfer adalah aktivitas operasi
        Assert.Equal(0m, cf.InvestingActivities.Total);
        Assert.Equal(0m, cf.FinancingActivities.Total);
        Assert.Equal(-6_500m, cf.NetCashChange);
        Assert.Equal(49_993_500m, cf.ClosingCash);
    }

    [Fact]
    public async Task Stress_CashFlow_MultiOffsetCompoundJournal_ReconcilesExactCashChange()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var peralatan = GetAccId("1-2001");
        var utangUsaha = GetAccId("2-1001");
        var bebanKirim = GetAccId("7-1001");

        // Transaksi pengadaan gabungan:
        // Beli Peralatan 25jt, Beban Pengiriman 500rb
        // Bayar tunai 5.5jt, sisanya Utang Usaha 20jt
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 3, 10), "Pengadaan Peralatan Campuran",
            (peralatan, 25_000_000m, 0m),
            (bebanKirim, 500_000m, 0m),
            (kas, 0m, 5_500_000m),
            (utangUsaha, 0m, 20_000_000m)));

        var cf = (await report.GetCashFlowAsync(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31))).Value!;

        // Peralatan -> Investing: -25M
        Assert.Equal(-25_000_000m, cf.InvestingActivities.Total);

        // Utang Usaha (+20M) + Beban Kirim (-500rb) -> Operating: +19.5M
        Assert.Equal(19_500_000m, cf.OperatingActivities.Total);

        // Perubahan Kas Bersih = -25M + 19.5M = -5.5M (persis kas keluar)
        Assert.Equal(-5_500_000m, cf.NetCashChange);
        Assert.Equal(-5_500_000m, cf.ClosingCash);
    }

    [Fact]
    public async Task Stress_CashFlow_ThreeWayReconciliation_WithBalanceSheetAndIncomeStatement()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var bank = GetAccId("1-1002");
        var piutang = GetAccId("1-1003");
        var peralatan = GetAccId("1-2001");
        var akumPeny = GetAccId("1-2002");
        var utang = GetAccId("2-1001");
        var modal = GetAccId("3-1001");
        var prive = GetAccId("3-1002");
        var penjualan = GetAccId("4-1001");
        var hpp = GetAccId("5-1001");
        var bebanGaji = GetAccId("6-1001");
        var bebanPeny = GetAccId("6-1004");

        var dtStart = new DateTime(2026, 1, 1);
        var dtEnd = new DateTime(2026, 1, 31);

        // Siklus komprehensif 1 bulan:
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 1), "Modal Pemilik", (bank, 80_000_000m, 0m), (modal, 0m, 80_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 2), "Beli Alat", (peralatan, 20_000_000m, 0m), (bank, 0m, 20_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 5), "Beli Aki Kredit", (hpp, 30_000_000m, 0m), (utang, 0m, 30_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 10), "Jual Aki Tunai", (kas, 35_000_000m, 0m), (penjualan, 0m, 35_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 12), "Jual Aki Tempo", (piutang, 15_000_000m, 0m), (penjualan, 0m, 15_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 18), "Cicil Utang", (utang, 15_000_000m, 0m), (bank, 0m, 15_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 22), "Terima Piutang", (bank, 10_000_000m, 0m), (piutang, 0m, 10_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 25), "Setor Kas ke Bank", (bank, 20_000_000m, 0m), (kas, 0m, 20_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 28), "Bayar Gaji", (bebanGaji, 6_000_000m, 0m), (kas, 0m, 6_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 30), "Penyusutan", (bebanPeny, 2_000_000m, 0m), (akumPeny, 0m, 2_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 31), "Prive", (prive, 4_000_000m, 0m), (bank, 0m, 4_000_000m)));

        // Ambil ke-3 laporan keuangan
        var bs = (await report.GetBalanceSheetAsync(dtEnd)).Value!;
        var lr = (await report.GetIncomeStatementAsync(dtStart, dtEnd)).Value!;
        var cf = (await report.GetCashFlowAsync(dtStart, dtEnd)).Value!;

        // 1. Neraca Seimbang
        Assert.Equal(bs.TotalAssets, bs.TotalLiabilities + bs.TotalEquity);

        // 2. Rekonsiliasi Laba Bersih: Laba Rugi == Neraca CurrentPeriodNetIncome
        Assert.Equal(lr.NetIncome, bs.CurrentPeriodNetIncome);
        Assert.Equal(12_000_000m, lr.NetIncome); // Rev 50M - HPP 30M - Gaji 6M - Peny 2M = 12M

        // 3. Rekonsiliasi Kas: Saldo Akhir Arus Kas == Total Kas & Bank di Neraca
        var currentAssetSection = bs.AssetSections.First(s => s.Title == "Aset Lancar");
        var kasLine = currentAssetSection.Lines.First(l => l.AccountCode == "1-1001").Amount;
        var bankLine = currentAssetSection.Lines.First(l => l.AccountCode == "1-1002").Amount;
        var totalCashInBalanceSheet = kasLine + bankLine;

        Assert.Equal(totalCashInBalanceSheet, cf.ClosingCash);
        Assert.Equal(cf.OpeningCash + cf.NetCashChange, cf.ClosingCash);
        Assert.Equal(80_000_000m, totalCashInBalanceSheet); // Kas 9M + Bank 71M = 80M
    }

    // =========================================================================
    // SECTION 4: EXTREME VOLUME, VALUE, AND SECURITY TESTS
    // =========================================================================

    [Fact]
    public async Task Stress_Volume_100Journals_HighThroughputConsistency()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var bank = GetAccId("1-1002");
        var piutang = GetAccId("1-1003");
        var utang = GetAccId("2-1001");
        var modal = GetAccId("3-1001");
        var penjualan = GetAccId("4-1001");
        var hpp = GetAccId("5-1001");
        var bebanGaji = GetAccId("6-1001");

        // Setoran modal awal
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 1), "Modal Awal", (bank, 500_000_000m, 0m), (modal, 0m, 500_000_000m)));

        // 100 jurnal bersiklus sepanjang tahun 2026
        for (int i = 1; i <= 100; i++)
        {
            var date = new DateTime(2026, 1, 1).AddDays(i * 3);
            decimal amount = 1_000_000m + (i * 250_000m);

            switch (i % 5)
            {
                case 0:
                    // Penjualan tunai
                    await journal.CreateAsync(MakeJournal(date, $"Penjualan Tunai #{i}", (kas, amount, 0m), (penjualan, 0m, amount)));
                    break;
                case 1:
                    // HPP tunai
                    await journal.CreateAsync(MakeJournal(date, $"HPP #{i}", (hpp, amount, 0m), (kas, 0m, amount)));
                    break;
                case 2:
                    // Penjualan tempo
                    await journal.CreateAsync(MakeJournal(date, $"Penjualan Tempo #{i}", (piutang, amount, 0m), (penjualan, 0m, amount)));
                    break;
                case 3:
                    // Bayar gaji via bank
                    await journal.CreateAsync(MakeJournal(date, $"Gaji #{i}", (bebanGaji, amount, 0m), (bank, 0m, amount)));
                    break;
                case 4:
                    // Transfer internal Kas ke Bank
                    await journal.CreateAsync(MakeJournal(date, $"Transfer Kas-Bank #{i}", (bank, amount, 0m), (kas, 0m, amount)));
                    break;
            }
        }

        // Jalankan ke-3 laporan di akhir tahun
        var bs = (await report.GetBalanceSheetAsync(new DateTime(2026, 12, 31))).Value!;
        var lr = (await report.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31))).Value!;
        var cf = (await report.GetCashFlowAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31))).Value!;

        // Semua invarian harus tetap terjaga tanpa cacat
        Assert.Equal(bs.TotalAssets, bs.TotalLiabilities + bs.TotalEquity);
        Assert.Equal(lr.NetIncome, bs.CurrentPeriodNetIncome);

        var currentAssetSection = bs.AssetSections.First(s => s.Title == "Aset Lancar");
        var totalCashInBalanceSheet = currentAssetSection.Lines.Where(l => l.AccountCode == "1-1001" || l.AccountCode == "1-1002").Sum(l => l.Amount);
        Assert.Equal(totalCashInBalanceSheet, cf.ClosingCash);
        Assert.Equal(cf.OpeningCash + cf.NetCashChange, cf.ClosingCash);
    }

    [Fact]
    public async Task Stress_ExtremeValues_TrillionIDR_NoOverflow()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var bank = GetAccId("1-1002");
        var modal = GetAccId("3-1001");
        var penjualan = GetAccId("4-1001");

        // 1 Triliun Rupiah dengan sen (1.000.000.000.000,75)
        decimal extremeModal = 1_000_000_000_000.75m;
        decimal extremeSale = 500_000_000_000.25m;

        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 1), "Modal 1T", (bank, extremeModal, 0m), (modal, 0m, extremeModal)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 15), "Jual 500M", (bank, extremeSale, 0m), (penjualan, 0m, extremeSale)));

        var bs = (await report.GetBalanceSheetAsync(new DateTime(2026, 1, 31))).Value!;
        var lr = (await report.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31))).Value!;
        var cf = (await report.GetCashFlowAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31))).Value!;

        decimal expectedTotal = 1_500_000_000_001.00m;

        Assert.Equal(expectedTotal, bs.TotalAssets);
        Assert.Equal(expectedTotal, bs.TotalEquity);
        Assert.Equal(bs.TotalAssets, bs.TotalLiabilities + bs.TotalEquity);
        Assert.Equal(extremeSale, lr.NetIncome);
        Assert.Equal(expectedTotal, cf.ClosingCash);
    }

    [Fact]
    public async Task Stress_SecurityGuard_AuthorizationMatrix()
    {
        await InitializeAsync();

        var (_, adminReport) = CreateServices(Roles.Admin);
        var (_, viewerReport) = CreateServices(Roles.Viewer);
        var (_, staffReport) = CreateServices(Roles.Staff);

        var asOf = DateTime.Today;

        // Admin: Allowed
        Assert.True((await adminReport.GetBalanceSheetAsync(asOf)).IsSuccess);
        Assert.True((await adminReport.GetIncomeStatementAsync(asOf.AddDays(-30), asOf)).IsSuccess);
        Assert.True((await adminReport.GetCashFlowAsync(asOf.AddDays(-30), asOf)).IsSuccess);

        // Viewer: Allowed
        Assert.True((await viewerReport.GetBalanceSheetAsync(asOf)).IsSuccess);
        Assert.True((await viewerReport.GetIncomeStatementAsync(asOf.AddDays(-30), asOf)).IsSuccess);
        Assert.True((await viewerReport.GetCashFlowAsync(asOf.AddDays(-30), asOf)).IsSuccess);

        // Staff: Rejected with ServiceGuard.ReadDenied
        var bsStaff = await staffReport.GetBalanceSheetAsync(asOf);
        Assert.False(bsStaff.IsSuccess);
        Assert.Equal("Anda tidak memiliki hak akses untuk melihat data ini.", bsStaff.Error);

        var isStaff = await staffReport.GetIncomeStatementAsync(asOf.AddDays(-30), asOf);
        Assert.False(isStaff.IsSuccess);
        Assert.Equal("Anda tidak memiliki hak akses untuk melihat data ini.", isStaff.Error);

        var cfStaff = await staffReport.GetCashFlowAsync(asOf.AddDays(-30), asOf);
        Assert.False(cfStaff.IsSuccess);
        Assert.Equal("Anda tidak memiliki hak akses untuk melihat data ini.", cfStaff.Error);
    }

    [Fact]
    public async Task Stress_BalanceSheet_FarPastAndFarFutureDates()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var modal = GetAccId("3-1001");

        // Transaksi di tahun 2026
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 6, 15), "Modal", (kas, 50_000_000m, 0m), (modal, 0m, 50_000_000m)));

        // 1. Tanggal jauh di masa lampau (tahun 2000): semua saldo 0, neraca tetap seimbang
        var bsPast = (await report.GetBalanceSheetAsync(new DateTime(2000, 1, 1))).Value!;
        Assert.Equal(0m, bsPast.TotalAssets);
        Assert.Equal(0m, bsPast.TotalLiabilities);
        Assert.Equal(0m, bsPast.TotalEquity);
        Assert.Equal(bsPast.TotalAssets, bsPast.TotalLiabilities + bsPast.TotalEquity);

        // 2. Tanggal jauh di masa depan (tahun 2099): transaksi tetap tercakup
        var bsFuture = (await report.GetBalanceSheetAsync(new DateTime(2099, 12, 31))).Value!;
        Assert.Equal(50_000_000m, bsFuture.TotalAssets);
        Assert.Equal(50_000_000m, bsFuture.TotalEquity);
        Assert.Equal(bsFuture.TotalAssets, bsFuture.TotalLiabilities + bsFuture.TotalEquity);
    }

    [Fact]
    public async Task Stress_IncomeStatement_StandaloneReturnsInZeroActivityPeriods()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var returPenjualan = GetAccId("4-1002");
        var returPembelian = GetAccId("5-1002");
        var bebanGaji = GetAccId("6-1001");

        // Pada bulan Feb 2026: TIDAK ADA PENJUALAN DAN TIDAK ADA PEMBELIAN BARU.
        // Hanya ada retur penjualan 3jt (uang kas dikembalikan ke pembeli),
        // retur pembelian 2jt (uang kas diterima kembali dari pemasok),
        // dan beban gaji 4jt dibayar via kas.
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 2, 5), "Retur Jual Periode Lalu", (returPenjualan, 3_000_000m, 0m), (kas, 0m, 3_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 2, 10), "Retur Beli Periode Lalu", (kas, 2_000_000m, 0m), (returPembelian, 0m, 2_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 2, 25), "Gaji", (bebanGaji, 4_000_000m, 0m), (kas, 0m, 4_000_000m)));

        var lr = (await report.GetIncomeStatementAsync(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28))).Value!;

        // Net Revenue = 0 - 3M = -3M
        Assert.Equal(-3_000_000m, lr.Revenue.Total);

        // Net COGS = 0 - 2M = -2M
        Assert.Equal(-2_000_000m, lr.CostOfGoodsSold.Total);

        // Gross Profit = Revenue (-3M) - COGS (-2M) = -3M - (-2M) = -1M
        Assert.Equal(-1_000_000m, lr.GrossProfit);

        // Operating Profit = Gross Profit (-1M) - Beban Gaji (4M) = -5M
        Assert.Equal(-5_000_000m, lr.OperatingProfit);
        Assert.Equal(-5_000_000m, lr.NetIncome);

        // Konsistensi dengan Neraca
        var bs = (await report.GetBalanceSheetAsync(new DateTime(2026, 2, 28))).Value!;
        Assert.Equal(-5_000_000m, bs.CurrentPeriodNetIncome);
        Assert.Equal(bs.TotalAssets, bs.TotalLiabilities + bs.TotalEquity);
    }

    [Fact]
    public async Task Stress_CashFlow_AssetTradeInCompoundJournal_ConservesNetCash()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var peralatan = GetAccId("1-2001");
        var akumPeny = GetAccId("1-2002");
        var modal = GetAccId("3-1001");

        // Modal awal di kas 50jt
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 1), "Modal Awal", (kas, 50_000_000m, 0m), (modal, 0m, 50_000_000m)));

        // Trade-in peralatan lama (Biaya 20M, Akumulasi 15M) untuk peralatan baru 25M
        // dengan tambahan kas 20M (tanpa untung/rugi, nilai wajar tukar = 5M):
        // Debit: Peralatan 25M (Baru)
        // Debit: Akumulasi Penyusutan 15M (Tutup lama)
        // Kredit: Peralatan 20M (Tutup lama)
        // Kredit: Kas 20M (Bayar selisih tunai)
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 3, 15), "Tukar Tambah Peralatan",
            (peralatan, 25_000_000m, 0m),
            (akumPeny, 15_000_000m, 0m),
            (peralatan, 0m, 20_000_000m),
            (kas, 0m, 20_000_000m)));

        var cf = (await report.GetCashFlowAsync(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31))).Value!;

        // Investasi: Peralatan Baru (-25M) + Akum Tutup (-15M) + Peralatan Lama (+20M) = -20M
        Assert.Equal(-20_000_000m, cf.InvestingActivities.Total);
        Assert.Equal(0m, cf.OperatingActivities.Total);
        Assert.Equal(0m, cf.FinancingActivities.Total);
        Assert.Equal(-20_000_000m, cf.NetCashChange);
        Assert.Equal(30_000_000m, cf.ClosingCash); // 50M - 20M = 30M
    }

    [Fact]
    public async Task Stress_CashFlow_MultiAccountCircularTransfer_ZeroNetImpact()
    {
        await InitializeAsync();
        var (journal, report) = CreateServices(Roles.Admin);

        var kas = GetAccId("1-1001");
        var bank = GetAccId("1-1002");
        var modal = GetAccId("3-1001");

        // Modal awal 100M di Bank
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 1), "Modal", (bank, 100_000_000m, 0m), (modal, 0m, 100_000_000m)));

        // Sirkular transfer pada tanggal 10 Jan:
        // Bank ke Kas 30M, lalu Kas balik ke Bank 30M
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 10), "Bank ke Kas", (kas, 30_000_000m, 0m), (bank, 0m, 30_000_000m)));
        await journal.CreateAsync(MakeJournal(new DateTime(2026, 1, 10), "Kas ke Bank", (bank, 30_000_000m, 0m), (kas, 0m, 30_000_000m)));

        var cf = (await report.GetCashFlowAsync(new DateTime(2026, 1, 5), new DateTime(2026, 1, 15))).Value!;

        Assert.Equal(100_000_000m, cf.OpeningCash);
        Assert.Empty(cf.OperatingActivities.Lines);
        Assert.Empty(cf.InvestingActivities.Lines);
        Assert.Empty(cf.FinancingActivities.Lines);
        Assert.Equal(0m, cf.NetCashChange);
        Assert.Equal(100_000_000m, cf.ClosingCash);
    }

    // =========================================================================
    // IN-PROCESS STRESS RUNNER ENTRY POINT
    // =========================================================================

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("    EMPIRICAL STRESS TEST HARNESS: FinancialReportService ADVERSARIAL SUITE     ");
        Console.WriteLine("================================================================================");

        var testClass = typeof(FinancialReportStressTests);
        var methods = testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<FactAttribute>() != null)
            .OrderBy(m => m.Name)
            .ToList();

        int totalPassed = 0;
        int totalFailed = 0;
        var failedTests = new List<(string Name, string Error, string? StackTrace)>();
        var totalSw = Stopwatch.StartNew();

        foreach (var method in methods)
        {
            var instance = Activator.CreateInstance(testClass);
            var sw = Stopwatch.StartNew();

            try
            {
                var result = method.Invoke(instance, null);
                if (result is Task task)
                {
                    await task;
                }

                sw.Stop();
                totalPassed++;
                Console.WriteLine($"  [PASS] {method.Name,-70} ({sw.ElapsedMilliseconds,4} ms)");
            }
            catch (Exception ex)
            {
                sw.Stop();
                totalFailed++;
                var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                while (inner.InnerException != null)
                {
                    inner = inner.InnerException;
                }
                failedTests.Add((method.Name, inner.Message, inner.StackTrace));
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] {method.Name,-70} ({sw.ElapsedMilliseconds,4} ms)");
                Console.WriteLine($"         Error: {inner.Message}");
                Console.ResetColor();
            }
            finally
            {
                if (instance is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        totalSw.Stop();

        Console.WriteLine("\n================================================================================");
        Console.WriteLine("                         STRESS TEST RUN SUMMARY                                ");
        Console.WriteLine("================================================================================");
        Console.WriteLine($"Total Stress Tests Executed : {totalPassed + totalFailed}");
        Console.WriteLine($"Passed                      : {totalPassed}");
        Console.WriteLine($"Failed                      : {totalFailed}");
        Console.WriteLine($"Execution Duration          : {totalSw.Elapsed.TotalSeconds:F2} seconds");

        if (totalFailed > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nFAILED STRESS TESTS:");
            foreach (var (name, error, trace) in failedTests)
            {
                Console.WriteLine($"\n[FAIL] {name}");
                Console.WriteLine($"  Message: {error}");
                if (!string.IsNullOrWhiteSpace(trace))
                {
                    Console.WriteLine($"  Trace: {trace}");
                }
            }
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\nALL ADVERSARIAL STRESS TESTS PASSED SUCCESSFULLY! (100% PASS RATE)");
        Console.ResetColor();
        return 0;
    }
}
