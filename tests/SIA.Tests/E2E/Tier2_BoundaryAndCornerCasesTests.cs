using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.Accounting;
using SIA.Application.DTOs.Reporting;

namespace SIA.Tests.E2E;

/// <summary>
/// TIER 2: BOUNDARY & CORNER CASES
/// Comprehensive requirement-driven opaque-box tests covering edge cases,
/// extreme values, zero amounts, negative numbers, invalid combinations,
/// boundary dates, and soft-delete states (>=5 test cases per feature).
/// </summary>
public class Tier2_BoundaryAndCornerCasesTests : IDisposable
{
    private readonly E2ETestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private async Task InitializeAsync() => await _fixture.InitializeAsync();

    // =========================================================================
    // FEATURE 1: JOURNAL VALIDATIONS & BOUNDARY VIOLATIONS
    // =========================================================================

    [Fact]
    public async Task T2_Validation_FewerThanTwoLines_Rejected()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");

        // 1 baris saja
        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Jurnal hanya 1 baris",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 500_000m, Credit = 0m }
            }
        };

        var result = await journalService.CreateAsync(dto);
        Assert.True(result.IsFailure);
        Assert.Contains("minimal memiliki 2 baris", result.Error);
    }

    [Fact]
    public async Task T2_Validation_UnbalancedDebitAndCredit_Rejected()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        // Debit 1jt != Credit 900rb
        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Tidak Seimbang",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 1_000_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 900_000m }
            }
        };

        var result = await journalService.CreateAsync(dto);
        Assert.True(result.IsFailure);
        Assert.Contains("tidak seimbang", result.Error);
    }

    [Fact]
    public async Task T2_Validation_ZeroTotalAmount_Rejected()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        // Total Debit == 0 dan Total Credit == 0
        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Nominal Nol",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 0m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 0m }
            }
        };

        var result = await journalService.CreateAsync(dto);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task T2_Validation_BothDebitAndCreditPositiveOnSameLine_Rejected()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        // Baris 1 memiliki Debit 500rb DAN Credit 500rb
        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Debit dan Kredit Positif Sama Baris",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 500_000m, Credit = 500_000m },
                new() { AccountId = modalId, Debit = 0m, Credit = 0m }
            }
        };

        var result = await journalService.CreateAsync(dto);
        Assert.True(result.IsFailure);
        Assert.Contains("tepat salah satu Debit atau Kredit lebih besar dari 0", result.Error);
    }

    [Fact]
    public async Task T2_Validation_NegativeAmount_Rejected()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Jumlah Negatif",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = -1_000_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = -1_000_000m }
            }
        };

        var result = await journalService.CreateAsync(dto);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task T2_Validation_NonExistentAccountId_Rejected()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        const int nonExistentAccountId = 999999;

        var dto = E2ETestFixture.Create2LineJournal(kasId, nonExistentAccountId, 1_000_000m);
        var result = await journalService.CreateAsync(dto);

        Assert.True(result.IsFailure);
        Assert.Contains("tidak ditemukan", result.Error);
    }

    [Fact]
    public async Task T2_Validation_InactiveAccount_Rejected()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var bankId = _fixture.GetAccountId("1-1002");

        // Nonaktifkan akun Bank di DB
        await using (var context = _fixture.CreateContext())
        {
            var bank = await context.Accounts.SingleAsync(a => a.Id == bankId);
            bank.IsActive = false;
            await context.SaveChangesAsync();
        }

        var dto = E2ETestFixture.Create2LineJournal(kasId, bankId, 500_000m);
        var result = await journalService.CreateAsync(dto);

        Assert.True(result.IsFailure);
        Assert.Contains("nonaktif", result.Error);
    }

    [Fact]
    public async Task T2_Validation_DescriptionExceeding255Chars_Rejected()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var longDesc = new string('A', 256);
        var dto = E2ETestFixture.Create2LineJournal(kasId, modalId, 1_000_000m, desc: longDesc);
        var result = await journalService.CreateAsync(dto);

        Assert.True(result.IsFailure);
        Assert.Contains("maksimal 255 karakter", result.Error);
    }

    // =========================================================================
    // FEATURE 2: EXTREME FINANCIAL AMOUNTS & DECIMAL PRECISION
    // =========================================================================

    [Fact]
    public async Task T2_Extreme_TenBillionRupiah_HandledAccurately()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        const decimal tenBillion = 10_000_000_000m;
        var result = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, tenBillion, desc: "Investasi Raksasa 10 Miliar"));

        Assert.True(result.IsSuccess);
        Assert.Equal(tenBillion, result.Value!.TotalDebit);
        Assert.Equal(tenBillion, await _fixture.GetAccountBalanceAsync(kasId));

        var bs = await reportService.GetBalanceSheetAsync(DateTime.Today);
        Assert.Equal(tenBillion, bs.Value!.TotalAssets);
        Assert.Equal(tenBillion, bs.Value.TotalEquity);
    }

    [Fact]
    public async Task T2_Extreme_FractionalCentsPreserved()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        const decimal centAmount = 150_250.75m;
        var result = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, centAmount, desc: "Presisi Pecahan Sen"));

        Assert.True(result.IsSuccess);
        Assert.Equal(centAmount, result.Value!.TotalDebit);
        Assert.Equal(centAmount, await _fixture.GetAccountBalanceAsync(kasId));
    }

    [Fact]
    public async Task T2_Extreme_IdenticalAccountOnDebitAndCredit_ValidatesCorrectly()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");

        // Reklasifikasi kas ke kas (debit kas 1jt, kredit kas 1jt)
        var dto = E2ETestFixture.Create2LineJournal(kasId, kasId, 1_000_000m, desc: "Koreksi Kas Mandiri");
        var result = await journalService.CreateAsync(dto);

        Assert.True(result.IsSuccess);
        // Saldo kas: 0 + 1jt - 1jt = 0
        Assert.Equal(0m, await _fixture.GetAccountBalanceAsync(kasId));
    }

    [Fact]
    public async Task T2_Extreme_AsymmetricMultiLineSplit_FourCreditsOneDebit()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var bebanGajiId = _fixture.GetAccountId("6-1001");
        var bebanSewaId = _fixture.GetAccountId("6-1002");
        var bebanListrikId = _fixture.GetAccountId("6-1003");
        var modalId = _fixture.GetAccountId("3-1001");

        // Setor modal dulu 20jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 20_000_000m));

        // Pembayaran paket beban: Gaji 4jt, Sewa 3jt, Listrik 1jt dari Kas 8jt
        var dto = E2ETestFixture.CreateMultiLineJournal(DateTime.Today, "Pembayaran Beban Serentak",
            (bebanGajiId, 4_000_000m, 0m),
            (bebanSewaId, 3_000_000m, 0m),
            (bebanListrikId, 1_000_000m, 0m),
            (kasId, 0m, 8_000_000m));

        var result = await journalService.CreateAsync(dto);
        Assert.True(result.IsSuccess);
        Assert.Equal(8_000_000m, result.Value!.TotalDebit);
        Assert.Equal(12_000_000m, await _fixture.GetAccountBalanceAsync(kasId)); // 20jt - 8jt = 12jt
    }

    [Fact]
    public async Task T2_Extreme_MultipleBalancedMutations_ZeroNetBalance()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        // Masuk 5jt, Keluar 5jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 5_000_000m));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(modalId, kasId, 5_000_000m));

        Assert.Equal(0m, await _fixture.GetAccountBalanceAsync(kasId));
        Assert.Equal(0m, await _fixture.GetAccountBalanceAsync(modalId));
    }

    // =========================================================================
    // FEATURE 3: REPORTING DATE BOUNDARIES & EXTREMES
    // =========================================================================

    [Fact]
    public async Task T2_Date_DateFromGreaterThanDateTo_Rejected()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);

        var dateFrom = new DateTime(2026, 5, 20);
        var dateTo = new DateTime(2026, 5, 10);

        var isResult = await reportService.GetIncomeStatementAsync(dateFrom, dateTo);
        var cfResult = await reportService.GetCashFlowAsync(dateFrom, dateTo);

        Assert.True(isResult.IsFailure);
        Assert.Contains("Tanggal awal tidak boleh melebihi tanggal akhir", isResult.Error);
        Assert.True(cfResult.IsFailure);
        Assert.Contains("Tanggal awal tidak boleh melebihi tanggal akhir", cfResult.Error);
    }

    [Fact]
    public async Task T2_Date_SingleDayReporting_ComputesExactDay()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");

        var targetDay = new DateTime(2026, 6, 15);
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 1_000_000m, date: targetDay.AddDays(-1)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 3_000_000m, date: targetDay));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 5_000_000m, date: targetDay.AddDays(1)));

        var isResult = await reportService.GetIncomeStatementAsync(targetDay, targetDay);
        Assert.True(isResult.IsSuccess);
        Assert.Equal(3_000_000m, isResult.Value!.Revenue.Total);
    }

    [Fact]
    public async Task T2_Date_AsOfDateInFarPast_ProducesBalancedZeroReport()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        // Transaksi di tahun 2026
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 50_000_000m, date: new DateTime(2026, 1, 15)));

        // Neraca per 31 Desember 2020 (sebelum ada transaksi)
        var bsResult = await reportService.GetBalanceSheetAsync(new DateTime(2020, 12, 31));
        Assert.True(bsResult.IsSuccess);
        Assert.Equal(0m, bsResult.Value!.TotalAssets);
        Assert.Equal(0m, bsResult.Value.TotalLiabilities);
        Assert.Equal(0m, bsResult.Value.TotalEquity);
    }

    [Fact]
    public async Task T2_Date_AsOfDateInFarFuture_IncludesAllTransactions()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 10_000_000m, date: new DateTime(2026, 1, 10)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 20_000_000m, date: new DateTime(2026, 5, 20)));

        var bsResult = await reportService.GetBalanceSheetAsync(new DateTime(2030, 12, 31));
        Assert.True(bsResult.IsSuccess);
        Assert.Equal(30_000_000m, bsResult.Value!.TotalAssets);
    }

    [Fact]
    public async Task T2_Date_LeapYearHandling_Feb29()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var leapDate = new DateTime(2028, 2, 29);
        var result = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 7_000_000m, date: leapDate));
        Assert.True(result.IsSuccess);

        var bs = await reportService.GetBalanceSheetAsync(leapDate);
        Assert.Equal(7_000_000m, bs.Value!.TotalAssets);
    }

    // =========================================================================
    // FEATURE 4: CASH FLOW EXTREMES & CORNER CASES
    // =========================================================================

    [Fact]
    public async Task T2_CashFlow_PureInternalCashTransfer_ZeroNetCashChange()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var bankId = _fixture.GetAccountId("1-1002");
        var modalId = _fixture.GetAccountId("3-1001");

        // Saldo awal 50jt di Kas pada 1 Januari
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 50_000_000m, date: new DateTime(2026, 1, 1)));

        // Transfer internal antar kas: Setor tunai Kas ke Bank 20jt pada 15 Januari
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bankId, kasId, 20_000_000m, date: new DateTime(2026, 1, 15)));

        var cf = await reportService.GetCashFlowAsync(new DateTime(2026, 1, 10), new DateTime(2026, 1, 20));
        Assert.True(cf.IsSuccess);
        Assert.Equal(50_000_000m, cf.Value!.OpeningCash);
        Assert.Equal(0m, cf.Value.NetCashChange);
        Assert.Equal(50_000_000m, cf.Value.ClosingCash);
    }

    [Fact]
    public async Task T2_CashFlow_PureNonCashJournal_NoActivityRecorded()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var bebanPenyusutanId = _fixture.GetAccountId("6-1004");
        var akumulasiId = _fixture.GetAccountId("1-2002");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanPenyusutanId, akumulasiId, 2_000_000m));

        var cf = await reportService.GetCashFlowAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
        Assert.True(cf.IsSuccess);
        Assert.Equal(0m, cf.Value!.OpeningCash);
        Assert.Equal(0m, cf.Value.NetCashChange);
        Assert.Equal(0m, cf.Value.ClosingCash);
        Assert.Empty(cf.Value.OperatingActivities.Lines);
        Assert.Empty(cf.Value.InvestingActivities.Lines);
        Assert.Empty(cf.Value.FinancingActivities.Lines);
    }

    [Fact]
    public async Task T2_CashFlow_MultiOffsetLines_AttributedAccurately()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");
        var pendapatanLainId = _fixture.GetAccountId("4-2001");

        // Penerimaan kas 15jt: 10jt dari Penjualan Aki, 5jt dari Pendapatan Jasa Pasang Aki
        var dto = E2ETestFixture.CreateMultiLineJournal(DateTime.Today, "Penerimaan Multi Offset",
            (kasId, 15_000_000m, 0m),
            (penjualanId, 0m, 10_000_000m),
            (pendapatanLainId, 0m, 5_000_000m));

        await journalService.CreateAsync(dto);

        var cf = await reportService.GetCashFlowAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
        Assert.True(cf.IsSuccess);
        Assert.Equal(15_000_000m, cf.Value!.NetCashChange);
        Assert.Equal(15_000_000m, cf.Value.OperatingActivities.Total);
    }

    [Fact]
    public async Task T2_CashFlow_NegativeNetCashChange_CashDeficitHandled()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");
        var bebanSewaId = _fixture.GetAccountId("6-1002");

        // Saldo awal 10jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 10_000_000m, date: new DateTime(2026, 1, 5)));
        // Bayar sewa 12jt (Kas defisit / negatif)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanSewaId, kasId, 12_000_000m, date: new DateTime(2026, 1, 20)));

        var cf = await reportService.GetCashFlowAsync(new DateTime(2026, 1, 10), new DateTime(2026, 1, 31));
        Assert.True(cf.IsSuccess);
        Assert.Equal(10_000_000m, cf.Value!.OpeningCash);
        Assert.Equal(-12_000_000m, cf.Value.NetCashChange);
        Assert.Equal(-2_000_000m, cf.Value.ClosingCash);
    }

    // =========================================================================
    // FEATURE 5: SOFT DELETE BOUNDARIES & CONCURRENCY
    // =========================================================================

    [Fact]
    public async Task T2_SoftDelete_NonExistentJournalId_Rejected()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);

        var result = await journalService.DeleteAsync(999999);
        Assert.True(result.IsFailure);
        Assert.Contains("tidak ditemukan", result.Error);
    }

    [Fact]
    public async Task T2_SoftDelete_AlreadyDeletedJournal_Rejected()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var createRes = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 1_000_000m));
        var del1 = await journalService.DeleteAsync(createRes.Value!.Id);
        Assert.True(del1.IsSuccess);

        // Hapus kedua kali
        var del2 = await journalService.DeleteAsync(createRes.Value.Id);
        Assert.True(del2.IsFailure);
        Assert.Contains("tidak ditemukan", del2.Error);
    }

    [Fact]
    public async Task T2_SoftDelete_CompoundJournal_ReversesAllFourAccounts()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var bankId = _fixture.GetAccountId("1-1002");
        var penjualanId = _fixture.GetAccountId("4-1001");
        var pendapatanLainId = _fixture.GetAccountId("4-2001");

        var dto = E2ETestFixture.CreateMultiLineJournal(DateTime.Today, "Compound 4 Akun",
            (kasId, 3_000_000m, 0m),
            (bankId, 2_000_000m, 0m),
            (penjualanId, 0m, 4_000_000m),
            (pendapatanLainId, 0m, 1_000_000m));

        var createRes = await journalService.CreateAsync(dto);
        Assert.True(createRes.IsSuccess);

        await journalService.DeleteAsync(createRes.Value!.Id);

        Assert.Equal(0m, await _fixture.GetAccountBalanceAsync(kasId));
        Assert.Equal(0m, await _fixture.GetAccountBalanceAsync(bankId));
        Assert.Equal(0m, await _fixture.GetAccountBalanceAsync(penjualanId));
        Assert.Equal(0m, await _fixture.GetAccountBalanceAsync(pendapatanLainId));
    }

    [Fact]
    public async Task T2_SoftDelete_LinesIgnoredInRecalculateBalances()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var accountService = _fixture.CreateAccountService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var res1 = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 5_000_000m));
        var res2 = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 3_000_000m));

        // Hapus res2
        await journalService.DeleteAsync(res2.Value!.Id);

        // Rekalkulasi saldo dari seluruh baris jurnal
        var recalcRes = await accountService.RecalculateBalancesAsync();
        Assert.True(recalcRes.IsSuccess);

        Assert.Equal(5_000_000m, await _fixture.GetAccountBalanceAsync(kasId));
        Assert.Equal(5_000_000m, await _fixture.GetAccountBalanceAsync(modalId));
    }

    [Fact]
    public async Task T2_Query_DateBounds_ExactInclusiveEquality()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var targetDate = new DateTime(2026, 4, 15);
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 1_000_000m, date: targetDate));

        var filter = new JournalFilterDto
        {
            DateFrom = targetDate,
            DateTo = targetDate
        };
        var listRes = await journalService.GetAllAsync(filter);

        Assert.Single(listRes.Value!);
        Assert.Equal(targetDate.Date, listRes.Value![0].TransactionDate.Date);
    }
}
