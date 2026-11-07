using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.Accounting;
using SIA.Application.DTOs.Reporting;

namespace SIA.Tests.E2E;

/// <summary>
/// TIER 3: CROSS-FEATURE COMBINATIONS
/// Pairwise and multi-feature interaction tests validating accounting consistency
/// across General Journal, Balance Sheet, Income Statement, Cash Flow,
/// and Balance Recalculation modules.
/// </summary>
public class Tier3_CrossFeatureCombinationsTests : IDisposable
{
    private readonly E2ETestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private async Task InitializeAsync() => await _fixture.InitializeAsync();

    [Fact]
    public async Task T3_Consistency_IncomeStatementNetIncome_Matches_BalanceSheetCurrentPeriodNetIncome()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);

        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");
        var returJualId = _fixture.GetAccountId("4-1002");
        var hppId = _fixture.GetAccountId("5-1001");
        var persediaanId = _fixture.GetAccountId("1-1004");
        var bebanGajiId = _fixture.GetAccountId("6-1001");
        var pendapatanLainId = _fixture.GetAccountId("4-2001");

        var dateFrom = new DateTime(2026, 1, 1);
        var dateTo = new DateTime(2026, 1, 31);

        // Transaksi beragam dalam periode
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 100_000_000m, date: new DateTime(2026, 1, 2)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 35_000_000m, date: new DateTime(2026, 1, 10)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(returJualId, kasId, 2_000_000m, date: new DateTime(2026, 1, 12)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(hppId, persediaanId, 20_000_000m, date: new DateTime(2026, 1, 15)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanGajiId, kasId, 5_000_000m, date: new DateTime(2026, 1, 25)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, pendapatanLainId, 1_500_000m, date: new DateTime(2026, 1, 28)));

        var isResult = await reportService.GetIncomeStatementAsync(dateFrom, dateTo);
        var bsResult = await reportService.GetBalanceSheetAsync(dateTo);

        Assert.True(isResult.IsSuccess);
        Assert.True(bsResult.IsSuccess);

        // Rekonsiliasi krusial: NetIncome di Laba Rugi HARUS SAMA PERSIS dengan CurrentPeriodNetIncome di Neraca!
        Assert.Equal(isResult.Value!.NetIncome, bsResult.Value!.CurrentPeriodNetIncome);
        // Revenue (35 - 2 = 33jt) - HPP (20jt) = GrossProfit 13jt. 13jt - Gaji 5jt = OperatingProfit 8jt. 8jt + PendLain 1.5jt = 9.5jt!
        Assert.Equal(9_500_000m, isResult.Value.NetIncome);
        Assert.Equal(9_500_000m, bsResult.Value.CurrentPeriodNetIncome);
    }

    [Fact]
    public async Task T3_Consistency_CashFlowClosingCash_Matches_BalanceSheetCashAndBankTotal()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);

        var kasId = _fixture.GetAccountId("1-1001");
        var bankId = _fixture.GetAccountId("1-1002");
        var modalId = _fixture.GetAccountId("3-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");
        var peralatanId = _fixture.GetAccountId("1-2001");
        var utangUsahaId = _fixture.GetAccountId("2-1001");
        var bebanGajiId = _fixture.GetAccountId("6-1001");

        var dateFrom = new DateTime(2026, 2, 1);
        var dateTo = new DateTime(2026, 2, 28);

        // Saldo awal di Januari: Kas 20jt, Bank 30jt (Total 50jt)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 20_000_000m, date: new DateTime(2026, 1, 10)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bankId, modalId, 30_000_000m, date: new DateTime(2026, 1, 15)));

        // Transaksi Februari:
        // Operasi: Penjualan tunai Kas 15jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 15_000_000m, date: new DateTime(2026, 2, 5)));
        // Investasi: Beli peralatan Bank 10jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(peralatanId, bankId, 10_000_000m, date: new DateTime(2026, 2, 10)));
        // Operasi: Bayar utang Bank 5jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(utangUsahaId, bankId, 5_000_000m, date: new DateTime(2026, 2, 18)));
        // Operasi: Bayar gaji Kas 3jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanGajiId, kasId, 3_000_000m, date: new DateTime(2026, 2, 25)));

        var cfResult = await reportService.GetCashFlowAsync(dateFrom, dateTo);
        var bsResult = await reportService.GetBalanceSheetAsync(dateTo);

        Assert.True(cfResult.IsSuccess);
        Assert.True(bsResult.IsSuccess);

        // Saldo kas & bank di Neraca
        var assetLines = bsResult.Value!.AssetSections.SelectMany(s => s.Lines).ToList();
        var kasLine = assetLines.SingleOrDefault(l => l.AccountCode == "1-1001")?.Amount ?? 0m;
        var bankLine = assetLines.SingleOrDefault(l => l.AccountCode == "1-1002")?.Amount ?? 0m;
        var totalCashInBalanceSheet = kasLine + bankLine;

        // ClosingCash di Laporan Arus Kas HARUS TEPAT SAMA dengan Total Kas + Bank di Neraca!
        Assert.Equal(50_000_000m, cfResult.Value!.OpeningCash);
        Assert.Equal(-3_000_000m, cfResult.Value.NetCashChange); // Kas: +15 - 3 = +12jt; Bank: -10 - 5 = -15jt. Net = -3jt
        Assert.Equal(47_000_000m, cfResult.Value.ClosingCash);
        Assert.Equal(totalCashInBalanceSheet, cfResult.Value.ClosingCash);
    }

    [Fact]
    public async Task T3_CrossFeature_JournalPosting_InstantlyMutatesBalanceSheet()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);

        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        // Sebelum posting: Neraca 0
        var bsBefore = await reportService.GetBalanceSheetAsync(DateTime.Today);
        Assert.Equal(0m, bsBefore.Value!.TotalAssets);

        // Posting jurnal baru
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 25_000_000m));

        // Setelah posting: Neraca langsung terefleksi tanpa perlu rekalkulasi manual
        var bsAfter = await reportService.GetBalanceSheetAsync(DateTime.Today);
        Assert.Equal(25_000_000m, bsAfter.Value!.TotalAssets);
        Assert.Equal(25_000_000m, bsAfter.Value.TotalEquity);
    }

    [Fact]
    public async Task T3_CrossFeature_JournalSoftDelete_InstantlyReversesReports()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);

        var kasId = _fixture.GetAccountId("1-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");

        var createRes = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 12_000_000m));

        // Laba rugi mencatat 12jt
        var is1 = await reportService.GetIncomeStatementAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
        Assert.Equal(12_000_000m, is1.Value!.Revenue.Total);

        // Hapus jurnal
        await journalService.DeleteAsync(createRes.Value!.Id);

        // Laba rugi kembali ke 0jt
        var is2 = await reportService.GetIncomeStatementAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
        Assert.Equal(0m, is2.Value!.Revenue.Total);
    }

    [Fact]
    public async Task T3_CrossFeature_RecalculateBalances_MaintainsFullAgreementWithReports()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var accountService = _fixture.CreateAccountService(Roles.Admin);

        var kasId = _fixture.GetAccountId("1-1001");
        var bankId = _fixture.GetAccountId("1-1002");
        var modalId = _fixture.GetAccountId("3-1001");
        var persediaanId = _fixture.GetAccountId("1-1004");
        var utangUsahaId = _fixture.GetAccountId("2-1001");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 30_000_000m));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bankId, modalId, 40_000_000m));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(persediaanId, utangUsahaId, 15_000_000m));

        // Jalankan rekalkulasi saldo
        var recalcRes = await accountService.RecalculateBalancesAsync();
        Assert.True(recalcRes.IsSuccess);

        var bs = await reportService.GetBalanceSheetAsync(DateTime.Today);
        Assert.True(bs.IsSuccess);

        // Saldo di Neraca persis sama dengan saldo akun setelah rekalkulasi
        Assert.Equal(30_000_000m, await _fixture.GetAccountBalanceAsync(kasId));
        Assert.Equal(40_000_000m, await _fixture.GetAccountBalanceAsync(bankId));
        Assert.Equal(15_000_000m, await _fixture.GetAccountBalanceAsync(persediaanId));
        Assert.Equal(15_000_000m, await _fixture.GetAccountBalanceAsync(utangUsahaId));
        Assert.Equal(70_000_000m, await _fixture.GetAccountBalanceAsync(modalId));
        Assert.Equal(85_000_000m, bs.Value!.TotalAssets);
        Assert.Equal(15_000_000m, bs.Value.TotalLiabilities);
        Assert.Equal(70_000_000m, bs.Value.TotalEquity);
    }

    [Fact]
    public async Task T3_CrossFeature_MultiPeriod_IncomeStatementAdditivity()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);

        var kasId = _fixture.GetAccountId("1-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");
        var bebanGajiId = _fixture.GetAccountId("6-1001");

        // Periode 1: 1-15 Jan
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 10_000_000m, date: new DateTime(2026, 1, 5)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanGajiId, kasId, 2_000_000m, date: new DateTime(2026, 1, 10)));

        // Periode 2: 16-31 Jan
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 15_000_000m, date: new DateTime(2026, 1, 20)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanGajiId, kasId, 3_000_000m, date: new DateTime(2026, 1, 25)));

        var p1 = await reportService.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 15));
        var p2 = await reportService.GetIncomeStatementAsync(new DateTime(2026, 1, 16), new DateTime(2026, 1, 31));
        var full = await reportService.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

        // Sifat aditif: NetIncome(P1) + NetIncome(P2) == NetIncome(Full)
        Assert.Equal(8_000_000m, p1.Value!.NetIncome); // 10 - 2 = 8jt
        Assert.Equal(12_000_000m, p2.Value!.NetIncome); // 15 - 3 = 12jt
        Assert.Equal(20_000_000m, full.Value!.NetIncome); // 25 - 5 = 20jt
        Assert.Equal(full.Value.NetIncome, p1.Value.NetIncome + p2.Value.NetIncome);
    }

    [Fact]
    public async Task T3_CrossFeature_InternalCashTransfer_PreservesTotalAssetsAndCashFlowInvariance()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);

        var kasId = _fixture.GetAccountId("1-1001");
        var bankId = _fixture.GetAccountId("1-1002");
        var modalId = _fixture.GetAccountId("3-1001");

        // Setor modal awal 50jt ke Kas
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 50_000_000m, date: new DateTime(2026, 3, 1)));

        var bsBeforeTransfer = await reportService.GetBalanceSheetAsync(new DateTime(2026, 3, 10));
        var assetsBefore = bsBeforeTransfer.Value!.TotalAssets;

        // Transfer 20jt dari Kas ke Bank (Debit Bank, Kredit Kas)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bankId, kasId, 20_000_000m, date: new DateTime(2026, 3, 15)));

        var bsAfterTransfer = await reportService.GetBalanceSheetAsync(new DateTime(2026, 3, 20));
        var cf = await reportService.GetCashFlowAsync(new DateTime(2026, 3, 11), new DateTime(2026, 3, 20));

        // Total Aset tidak boleh berubah
        Assert.Equal(assetsBefore, bsAfterTransfer.Value!.TotalAssets);
        // Arus kas bersih transfer internal harus nol
        Assert.Equal(0m, cf.Value!.NetCashChange);
    }

    [Fact]
    public async Task T3_CrossFeature_PayableSettlement_ReducesCashAndLiabilitySimultaneously()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);

        var kasId = _fixture.GetAccountId("1-1001");
        var persediaanId = _fixture.GetAccountId("1-1004");
        var utangUsahaId = _fixture.GetAccountId("2-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        // Modal 40jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 40_000_000m));
        // Beli barang kredit 20jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(persediaanId, utangUsahaId, 20_000_000m));
        // Pelunasan utang tunai 12jt (Utang Debit, Kas Credit)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(utangUsahaId, kasId, 12_000_000m));

        var bs = await reportService.GetBalanceSheetAsync(DateTime.Today);
        Assert.Equal(28_000_000m, await _fixture.GetAccountBalanceAsync(kasId)); // 40 - 12 = 28jt
        Assert.Equal(8_000_000m, await _fixture.GetAccountBalanceAsync(utangUsahaId)); // 20 - 12 = 8jt
        Assert.Equal(48_000_000m, bs.Value!.TotalAssets); // Kas 28 + Persediaan 20 = 48jt
        Assert.Equal(8_000_000m, bs.Value.TotalLiabilities);
        Assert.Equal(40_000_000m, bs.Value.TotalEquity);
    }

    [Fact]
    public async Task T3_CrossFeature_ReceivableCollection_ReducesReceivableAndIncreasesCashSimultaneously()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);

        var kasId = _fixture.GetAccountId("1-1001");
        var piutangId = _fixture.GetAccountId("1-1003");
        var penjualanId = _fixture.GetAccountId("4-1001");

        // Penjualan kredit 10jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(piutangId, penjualanId, 10_000_000m));
        // Penerimaan piutang tunai 6jt (Kas Debit, Piutang Credit)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, piutangId, 6_000_000m));

        Assert.Equal(6_000_000m, await _fixture.GetAccountBalanceAsync(kasId));
        Assert.Equal(4_000_000m, await _fixture.GetAccountBalanceAsync(piutangId)); // 10 - 6 = 4jt

        var bs = await reportService.GetBalanceSheetAsync(DateTime.Today);
        Assert.Equal(10_000_000m, bs.Value!.TotalAssets); // Kas 6 + Piutang 4 = 10jt
        Assert.Equal(10_000_000m, bs.Value.TotalEquity); // Laba berjalan 10jt
    }

    [Fact]
    public async Task T3_CrossFeature_ContraAccounts_PreserveBalanceSheetEquationAcrossAllTransactions()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);

        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");
        var peralatanId = _fixture.GetAccountId("1-2001");
        var akumulasiId = _fixture.GetAccountId("1-2002"); // Contra Asset
        var priveId = _fixture.GetAccountId("3-1002"); // Contra Equity
        var bebanPenyusutanId = _fixture.GetAccountId("6-1004");

        // Modal 50jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 50_000_000m));
        // Beli peralatan 20jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(peralatanId, kasId, 20_000_000m));
        // Penyusutan 2jt (Beban Debit, Akumulasi Credit)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanPenyusutanId, akumulasiId, 2_000_000m));
        // Prive 3jt (Prive Debit, Kas Credit)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(priveId, kasId, 3_000_000m));

        var bs = await reportService.GetBalanceSheetAsync(DateTime.Today);
        Assert.True(bs.IsSuccess);

        // Assets: Kas (50 - 20 - 3 = 27jt) + Peralatan Bersih (20 - 2 = 18jt) = 45jt
        Assert.Equal(45_000_000m, bs.Value!.TotalAssets);
        // Liabilities: 0
        Assert.Equal(0m, bs.Value.TotalLiabilities);
        // Equity: Modal (50jt) + Prive (-3jt) + Rugi Berjalan (-2jt) = 45jt
        Assert.Equal(45_000_000m, bs.Value.TotalEquity);
        Assert.Equal(bs.Value.TotalAssets, bs.Value.TotalLiabilities + bs.Value.TotalEquity);
    }
}
