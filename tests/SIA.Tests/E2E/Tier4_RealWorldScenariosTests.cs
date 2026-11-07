using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.Accounting;
using SIA.Application.DTOs.Reporting;

namespace SIA.Tests.E2E;

/// <summary>
/// TIER 4: REAL-WORLD APPLICATION SCENARIOS
/// End-to-end accounting workflows modeling actual business operations of a
/// vehicle battery store (Toko Aki "PT Berkah Aki Motor") across a full monthly cycle:
/// Owner Equity -> Battery Procurement on Credit -> Retail & Fleet Sales -> Returns ->
/// Operating Expenses -> Depreciation & Drawings -> Full 3-Way Reconciliation.
/// </summary>
public class Tier4_RealWorldScenariosTests : IDisposable
{
    private readonly E2ETestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private async Task InitializeAsync() => await _fixture.InitializeAsync();

    [Fact]
    public async Task T4_Scenario1_OwnerCapitalization_AndStoreEquipmentProcurement()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);

        var kasId = _fixture.GetAccountId("1-1001");
        var bankId = _fixture.GetAccountId("1-1002");
        var modalId = _fixture.GetAccountId("3-1001");
        var peralatanId = _fixture.GetAccountId("1-2001");
        var perlengkapanId = _fixture.GetAccountId("1-1005");

        // 2 Jan: Setoran modal awal 150jt (100jt Kas, 50jt Bank)
        var dtoModal = E2ETestFixture.CreateMultiLineJournal(new DateTime(2026, 1, 2), "Setoran Modal Awal Pemilik Toko Aki",
            (kasId, 100_000_000m, 0m),
            (bankId, 50_000_000m, 0m),
            (modalId, 0m, 150_000_000m));
        var resModal = await journalService.CreateAsync(dtoModal);
        Assert.True(resModal.IsSuccess);

        // 3 Jan: Beli rak etalase & battery tester 20jt bayar via Bank
        var dtoPeralatan = E2ETestFixture.Create2LineJournal(peralatanId, bankId, 20_000_000m,
            date: new DateTime(2026, 1, 3), desc: "Pembelian Rak Display & Tester Aki");
        var resPeralatan = await journalService.CreateAsync(dtoPeralatan);
        Assert.True(resPeralatan.IsSuccess);

        // 4 Jan: Beli perlengkapan toko tunai 3jt
        var dtoPerlengkapan = E2ETestFixture.Create2LineJournal(perlengkapanId, kasId, 3_000_000m,
            date: new DateTime(2026, 1, 4), desc: "Beli Perlengkapan Toko Tunai");
        var resPerlengkapan = await journalService.CreateAsync(dtoPerlengkapan);
        Assert.True(resPerlengkapan.IsSuccess);

        // Verifikasi Neraca per 5 Jan
        var bs = await reportService.GetBalanceSheetAsync(new DateTime(2026, 1, 5));
        Assert.True(bs.IsSuccess);

        Assert.Equal(97_000_000m, await _fixture.GetAccountBalanceAsync(kasId)); // 100jt - 3jt = 97jt
        Assert.Equal(30_000_000m, await _fixture.GetAccountBalanceAsync(bankId)); // 50jt - 20jt = 30jt
        Assert.Equal(3_000_000m, await _fixture.GetAccountBalanceAsync(perlengkapanId));
        Assert.Equal(20_000_000m, await _fixture.GetAccountBalanceAsync(peralatanId));
        Assert.Equal(150_000_000m, await _fixture.GetAccountBalanceAsync(modalId));

        Assert.Equal(150_000_000m, bs.Value!.TotalAssets);
        Assert.Equal(0m, bs.Value.TotalLiabilities);
        Assert.Equal(150_000_000m, bs.Value.TotalEquity);
    }

    [Fact]
    public async Task T4_Scenario2_BatteryInventoryProcurement_CreditWithAdvanceAndReturn()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);

        var bankId = _fixture.GetAccountId("1-1002");
        var modalId = _fixture.GetAccountId("3-1001");
        var persediaanId = _fixture.GetAccountId("1-1004");
        var utangUsahaId = _fixture.GetAccountId("2-1001");
        var returBeliId = _fixture.GetAccountId("5-1002");

        // Modal di Bank 50jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bankId, modalId, 50_000_000m, date: new DateTime(2026, 1, 5)));

        // 8 Jan: Pembelian aki 100 unit tempo dari supplier 50jt
        var dtoBeli = E2ETestFixture.Create2LineJournal(persediaanId, utangUsahaId, 50_000_000m,
            date: new DateTime(2026, 1, 8), desc: "Beli Aki GS Astra & Yuasa 100 Unit Tempo");
        var resBeli = await journalService.CreateAsync(dtoBeli);
        Assert.True(resBeli.IsSuccess);

        // 9 Jan: Pembayaran uang muka / termin 1 via Bank 25jt
        var dtoBayar = E2ETestFixture.Create2LineJournal(utangUsahaId, bankId, 25_000_000m,
            date: new DateTime(2026, 1, 9), desc: "Pembayaran DP Aki ke Supplier");
        var resBayar = await journalService.CreateAsync(dtoBayar);
        Assert.True(resBayar.IsSuccess);

        // 10 Jan: Retur 2 aki cacat pabrik ke supplier 1jt (Utang Debit, Retur Beli Credit)
        var dtoRetur = E2ETestFixture.Create2LineJournal(utangUsahaId, returBeliId, 1_000_000m,
            date: new DateTime(2026, 1, 10), desc: "Retur 2 Unit Aki Cacat ke Supplier");
        var resRetur = await journalService.CreateAsync(dtoRetur);
        Assert.True(resRetur.IsSuccess);

        var bs = await reportService.GetBalanceSheetAsync(new DateTime(2026, 1, 11));
        Assert.True(bs.IsSuccess);

        Assert.Equal(50_000_000m, await _fixture.GetAccountBalanceAsync(persediaanId));
        Assert.Equal(24_000_000m, await _fixture.GetAccountBalanceAsync(utangUsahaId)); // 50 - 25 - 1 = 24jt
        Assert.Equal(25_000_000m, await _fixture.GetAccountBalanceAsync(bankId)); // 50 - 25 = 25jt
        Assert.Equal(1_000_000m, await _fixture.GetAccountBalanceAsync(returBeliId));

        Assert.Equal(bs.Value!.TotalAssets, bs.Value.TotalLiabilities + bs.Value.TotalEquity);
    }

    [Fact]
    public async Task T4_Scenario3_RetailCashAndFleetCreditSales_WithCustomerReturn()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);

        var kasId = _fixture.GetAccountId("1-1001");
        var bankId = _fixture.GetAccountId("1-1002");
        var piutangId = _fixture.GetAccountId("1-1003");
        var persediaanId = _fixture.GetAccountId("1-1004");
        var modalId = _fixture.GetAccountId("3-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");
        var returJualId = _fixture.GetAccountId("4-1002");
        var hppId = _fixture.GetAccountId("5-1001");

        // Setup awal: Kas 20jt, Persediaan 40jt, Modal 60jt
        var dtoSetup = E2ETestFixture.CreateMultiLineJournal(new DateTime(2026, 1, 1), "Setup",
            (kasId, 20_000_000m, 0m),
            (persediaanId, 40_000_000m, 0m),
            (modalId, 0m, 60_000_000m));
        await journalService.CreateAsync(dtoSetup);

        // 15 Jan: Penjualan eceran aki tunai 30 unit (22.5jt) & catat HPP (15jt)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 22_500_000m,
            date: new DateTime(2026, 1, 15), desc: "Penjualan Tunai 30 Unit Aki"));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(hppId, persediaanId, 15_000_000m,
            date: new DateTime(2026, 1, 15), desc: "Beban Pokok Penjualan 30 Unit Aki"));

        // 18 Jan: Penjualan tempo ke armada taksi 20 unit (16jt) & catat HPP (10jt)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(piutangId, penjualanId, 16_000_000m,
            date: new DateTime(2026, 1, 18), desc: "Penjualan Tempo Armada Taksi 20 Unit Aki"));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(hppId, persediaanId, 10_000_000m,
            date: new DateTime(2026, 1, 18), desc: "Beban Pokok Penjualan Taksi 20 Unit Aki"));

        // 20 Jan: Pelanggan eceran retur 1 unit aki salah kutub (750rb kas) & kembalikan ke stok (500rb)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(returJualId, kasId, 750_000m,
            date: new DateTime(2026, 1, 20), desc: "Retur Penjualan 1 Unit Aki Salah Kutub"));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(persediaanId, hppId, 500_000m,
            date: new DateTime(2026, 1, 20), desc: "Pemulihan Stok Aki dari Retur Pelanggan"));

        // 22 Jan: Armada taksi transfer pelunasan 50% piutang (8jt ke Bank)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bankId, piutangId, 8_000_000m,
            date: new DateTime(2026, 1, 22), desc: "Penerimaan Pelunasan 50% Piutang Taksi"));

        // Verifikasi Laporan Laba Rugi
        var isResult = await reportService.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        Assert.True(isResult.IsSuccess);

        // Revenue: Penjualan 38.5jt - Retur 750rb = 37.75jt
        Assert.Equal(37_750_000m, isResult.Value!.Revenue.Total);
        // COGS: 15jt + 10jt - 500rb = 24.5jt
        Assert.Equal(24_500_000m, isResult.Value.CostOfGoodsSold.Total);
        // Gross Profit: 37.75jt - 24.5jt = 13.25jt
        Assert.Equal(13_250_000m, isResult.Value.GrossProfit);
    }

    [Fact]
    public async Task T4_Scenario4_OperatingExpenses_MonthEndDepreciation_AndOwnerDrawing()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);

        var kasId = _fixture.GetAccountId("1-1001");
        var bankId = _fixture.GetAccountId("1-1002");
        var modalId = _fixture.GetAccountId("3-1001");
        var bebanListrikId = _fixture.GetAccountId("6-1003");
        var bebanGajiId = _fixture.GetAccountId("6-1001");
        var pendapatanLainId = _fixture.GetAccountId("4-2001");
        var priveId = _fixture.GetAccountId("3-1002");
        var bebanPenyusutanId = _fixture.GetAccountId("6-1004");
        var akumulasiId = _fixture.GetAccountId("1-2002");

        // Modal awal Kas 20jt, Bank 20jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 20_000_000m));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bankId, modalId, 20_000_000m));

        // 26 Jan: Bayar listrik & air tunai 1.5jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanListrikId, kasId, 1_500_000m,
            date: new DateTime(2026, 1, 26), desc: "Bayar Listrik & Air Toko"));

        // 28 Jan: Bayar gaji 2 staf via Bank 6jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanGajiId, bankId, 6_000_000m,
            date: new DateTime(2026, 1, 28), desc: "Gaji 2 Staf Toko Aki"));

        // 29 Jan: Komisi scrap aki bekas (Pendapatan Lain-lain) 800rb kas
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, pendapatanLainId, 800_000m,
            date: new DateTime(2026, 1, 29), desc: "Komisi Penjualan Aki Bekas / Trade-in"));

        // 30 Jan: Prive pemilik 4jt kas
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(priveId, kasId, 4_000_000m,
            date: new DateTime(2026, 1, 30), desc: "Pengambilan Prive Pemilik Toko"));

        // 31 Jan: Penyusutan peralatan toko 500rb
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanPenyusutanId, akumulasiId, 500_000m,
            date: new DateTime(2026, 1, 31), desc: "Penyusutan Peralatan Toko Januari"));

        var isRes = await reportService.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        var bsRes = await reportService.GetBalanceSheetAsync(new DateTime(2026, 1, 31));

        Assert.True(isRes.IsSuccess);
        Assert.True(bsRes.IsSuccess);

        // Beban Operasional: Listrik 1.5jt + Gaji 6jt + Penyusutan 500rb = 8jt
        Assert.Equal(8_000_000m, isRes.Value!.OperatingExpenses.Total);
        Assert.Equal(800_000m, isRes.Value.OtherRevenue.Total);
        // Net Income = 0 - 8jt + 800rb = -7.2jt (Rugi bersih aktivitas operasional non-sales)
        Assert.Equal(-7_200_000m, isRes.Value.NetIncome);

        // Keseimbangan neraca tetap terjaga
        Assert.Equal(bsRes.Value!.TotalAssets, bsRes.Value.TotalLiabilities + bsRes.Value.TotalEquity);
    }

    [Fact]
    public async Task T4_Scenario5_MonthEnd_ThreeWayFinancialStatementReconciliation()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var accountService = _fixture.CreateAccountService(Roles.Admin);

        // Ambil ID semua akun yang dipakai di siklus 1 bulan Toko Aki
        var kasId = _fixture.GetAccountId("1-1001");
        var bankId = _fixture.GetAccountId("1-1002");
        var piutangId = _fixture.GetAccountId("1-1003");
        var persediaanId = _fixture.GetAccountId("1-1004");
        var perlengkapanId = _fixture.GetAccountId("1-1005");
        var peralatanId = _fixture.GetAccountId("1-2001");
        var akumulasiId = _fixture.GetAccountId("1-2002"); // Contra Asset
        var utangUsahaId = _fixture.GetAccountId("2-1001");
        var modalId = _fixture.GetAccountId("3-1001");
        var priveId = _fixture.GetAccountId("3-1002"); // Contra Equity
        var penjualanId = _fixture.GetAccountId("4-1001");
        var returJualId = _fixture.GetAccountId("4-1002"); // Contra Revenue
        var pendapatanLainId = _fixture.GetAccountId("4-2001");
        var hppId = _fixture.GetAccountId("5-1001");
        var returBeliId = _fixture.GetAccountId("5-1002"); // Contra COGS
        var bebanGajiId = _fixture.GetAccountId("6-1001");
        var bebanListrikId = _fixture.GetAccountId("6-1003");
        var bebanPenyusutanId = _fixture.GetAccountId("6-1004");

        // ---------------------------------------------------------------------
        // SIKLUS TRANSAKSI LENGKAP 1 BULAN (JANUARI 2026)
        // ---------------------------------------------------------------------

        // 1. Jan 2: Setoran modal 150jt (100jt Kas, 50jt Bank)
        await journalService.CreateAsync(E2ETestFixture.CreateMultiLineJournal(new DateTime(2026, 1, 2), "Setoran Modal",
            (kasId, 100_000_000m, 0m),
            (bankId, 50_000_000m, 0m),
            (modalId, 0m, 150_000_000m)));

        // 2. Jan 3: Beli peralatan toko 20jt dari Bank
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(peralatanId, bankId, 20_000_000m,
            date: new DateTime(2026, 1, 3), desc: "Beli Peralatan Toko"));

        // 3. Jan 4: Beli perlengkapan toko 3jt tunai
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(perlengkapanId, kasId, 3_000_000m,
            date: new DateTime(2026, 1, 4), desc: "Beli Perlengkapan"));

        // 4. Jan 8: Beli stok aki kredit 50jt dari supplier
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(persediaanId, utangUsahaId, 50_000_000m,
            date: new DateTime(2026, 1, 8), desc: "Beli Stok Aki"));

        // 5. Jan 9: Bayar DP ke supplier 25jt dari Bank
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(utangUsahaId, bankId, 25_000_000m,
            date: new DateTime(2026, 1, 9), desc: "Bayar DP Supplier"));

        // 6. Jan 10: Retur 2 aki cacat ke supplier 1jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(utangUsahaId, returBeliId, 1_000_000m,
            date: new DateTime(2026, 1, 10), desc: "Retur Aki ke Supplier"));

        // 7. Jan 15: Penjualan tunai 22.5jt & HPP 15jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 22_500_000m,
            date: new DateTime(2026, 1, 15), desc: "Penjualan Tunai"));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(hppId, persediaanId, 15_000_000m,
            date: new DateTime(2026, 1, 15), desc: "HPP Penjualan Tunai"));

        // 8. Jan 18: Penjualan tempo ke taksi 16jt & HPP 10jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(piutangId, penjualanId, 16_000_000m,
            date: new DateTime(2026, 1, 18), desc: "Penjualan Tempo Armada"));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(hppId, persediaanId, 10_000_000m,
            date: new DateTime(2026, 1, 18), desc: "HPP Penjualan Tempo"));

        // 9. Jan 20: Retur penjualan dari pelanggan 750rb & pemulihan stok 500rb
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(returJualId, kasId, 750_000m,
            date: new DateTime(2026, 1, 20), desc: "Retur Penjualan Pelanggan"));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(persediaanId, hppId, 500_000m,
            date: new DateTime(2026, 1, 20), desc: "Pemulihan Stok Retur"));

        // 10. Jan 22: Penerimaan pelunasan taksi 8jt via Bank
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bankId, piutangId, 8_000_000m,
            date: new DateTime(2026, 1, 22), desc: "Pelunasan Sebagian Taksi"));

        // 11. Jan 26: Listrik & air 1.5jt tunai
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanListrikId, kasId, 1_500_000m,
            date: new DateTime(2026, 1, 26), desc: "Listrik Toko"));

        // 12. Jan 28: Gaji staf 6jt dari Bank
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanGajiId, bankId, 6_000_000m,
            date: new DateTime(2026, 1, 28), desc: "Gaji Staf"));

        // 13. Jan 29: Komisi scrap aki 800rb tunai
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, pendapatanLainId, 800_000m,
            date: new DateTime(2026, 1, 29), desc: "Komisi Scrap Aki"));

        // 14. Jan 30: Prive pemilik 4jt tunai
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(priveId, kasId, 4_000_000m,
            date: new DateTime(2026, 1, 30), desc: "Prive Pemilik"));

        // 15. Jan 31: Penyusutan peralatan 500rb
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanPenyusutanId, akumulasiId, 500_000m,
            date: new DateTime(2026, 1, 31), desc: "Penyusutan Peralatan"));

        // ---------------------------------------------------------------------
        // REKONSILIASI AKUNTANSI 3-ARAH (3-WAY RECONCILIATION)
        // ---------------------------------------------------------------------
        var periodStart = new DateTime(2026, 1, 1);
        var periodEnd = new DateTime(2026, 1, 31);

        var isResult = await reportService.GetIncomeStatementAsync(periodStart, periodEnd);
        var bsResult = await reportService.GetBalanceSheetAsync(periodEnd);
        var cfResult = await reportService.GetCashFlowAsync(periodStart, periodEnd);

        Assert.True(isResult.IsSuccess, isResult.Error);
        Assert.True(bsResult.IsSuccess, bsResult.Error);
        Assert.True(cfResult.IsSuccess, cfResult.Error);

        var inc = isResult.Value!;
        var bal = bsResult.Value!;
        var cas = cfResult.Value!;

        // 1. UJI LAPORAN LABA RUGI:
        // Revenue: Penjualan 38.5jt - Retur 750rb = 37.75jt
        Assert.Equal(37_750_000m, inc.Revenue.Total);
        // COGS: HPP 24.5jt - Retur Pembelian 1jt = 23.5jt
        Assert.Equal(23_500_000m, inc.CostOfGoodsSold.Total);
        // Gross Profit: 37.75jt - 23.5jt = 14.25jt
        Assert.Equal(14_250_000m, inc.GrossProfit);
        // Operating Expenses: Listrik 1.5jt + Gaji 6jt + Penyusutan 500rb = 8jt
        Assert.Equal(8_000_000m, inc.OperatingExpenses.Total);
        // Operating Profit: 14.25jt - 8jt = 6.25jt
        Assert.Equal(6_250_000m, inc.OperatingProfit);
        // Other Revenue = 800rb
        Assert.Equal(800_000m, inc.OtherRevenue.Total);
        // Net Income: 6.25jt + 800rb = 7.05jt
        const decimal expectedNetIncome = 7_050_000m;
        Assert.Equal(expectedNetIncome, inc.NetIncome);

        // 2. REKONSILIASI LABA RUGI -> NERACA:
        // CurrentPeriodNetIncome di Neraca HARUS TEPAT SAMA dengan NetIncome di Laba Rugi!
        Assert.Equal(expectedNetIncome, bal.CurrentPeriodNetIncome);

        // 3. UJI PERSAMAAN DASAR AKUNTANSI (BALANCE SHEET INVARIANT):
        // Total Assets = Rp 177,050,000
        const decimal expectedTotalAssets = 177_050_000m;
        const decimal expectedTotalLiabilities = 24_000_000m;
        const decimal expectedTotalEquity = 153_050_000m; // Modal 150M - Prive 4M + Laba 7.05M
        Assert.Equal(expectedTotalAssets, bal.TotalAssets);
        Assert.Equal(expectedTotalLiabilities, bal.TotalLiabilities);
        Assert.Equal(expectedTotalEquity, bal.TotalEquity);
        // Strictly: TotalAssets == TotalLiabilities + TotalEquity
        Assert.Equal(bal.TotalAssets, bal.TotalLiabilities + bal.TotalEquity);

        // 4. REKONSILIASI ARUS KAS -> NERACA:
        // Saldo akhir kas di Laporan Arus Kas HARUS TEPAT SAMA dengan Total Kas + Bank di Neraca!
        var assetLines = bal.AssetSections.SelectMany(s => s.Lines).ToList();
        var kasBalance = assetLines.Single(l => l.AccountCode == "1-1001").Amount;
        var bankBalance = assetLines.Single(l => l.AccountCode == "1-1002").Amount;
        var totalCashInBalanceSheet = kasBalance + bankBalance;

        Assert.Equal(0m, cas.OpeningCash);
        Assert.Equal(totalCashInBalanceSheet, cas.ClosingCash);
        Assert.Equal(cas.ClosingCash, cas.OpeningCash + cas.NetCashChange);

        // 5. INTEGRITAS REKALKULASI SALDO:
        // Jalankan rekalkulasi saldo dan pastikan integritas data 100% konsisten
        var recalcResult = await accountService.RecalculateBalancesAsync();
        Assert.True(recalcResult.IsSuccess);

        Assert.Equal(kasBalance, await _fixture.GetAccountBalanceAsync(kasId));
        Assert.Equal(bankBalance, await _fixture.GetAccountBalanceAsync(bankId));
    }
}
