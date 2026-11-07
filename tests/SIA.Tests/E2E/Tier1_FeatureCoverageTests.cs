using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.Accounting;
using SIA.Application.DTOs.Reporting;
using SIA.Domain.Enums;
using SIA.Infrastructure.Services;

namespace SIA.Tests.E2E;

/// <summary>
/// TIER 1: FEATURE COVERAGE (HAPPY PATHS IN ISOLATION)
/// Comprehensive requirement-driven opaque-box tests covering all primary features
/// of General Journal and Financial Reports in isolation (>=5 test cases per feature).
/// </summary>
public class Tier1_FeatureCoverageTests : IDisposable
{
    private readonly E2ETestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private async Task InitializeAsync() => await _fixture.InitializeAsync();

    // =========================================================================
    // FEATURE 1: JOURNAL POSTING & DOUBLE-ENTRY VALIDATION (F01)
    // =========================================================================

    [Fact]
    public async Task F01_Create_TwoLineBalancedJournal_Success()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");

        var dto = E2ETestFixture.Create2LineJournal(kasId, penjualanId, 1_500_000m, desc: "Penjualan aki tunai");
        var result = await journalService.CreateAsync(dto);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Id > 0);
        Assert.StartsWith("JRN-", result.Value.JournalNumber);
        Assert.Equal(1_500_000m, result.Value.TotalDebit);
        Assert.Equal(1_500_000m, result.Value.TotalCredit);
        Assert.Equal(2, result.Value.Lines.Count);
    }

    [Fact]
    public async Task F01_Create_MultiLineCompoundJournal_Success()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var piutangId = _fixture.GetAccountId("1-1003");
        var penjualanId = _fixture.GetAccountId("4-1001");

        // Penjualan aki 5jt: 3jt tunai (Kas Debit), 2jt tempo (Piutang Debit), 5jt Penjualan Kredit
        var dto = E2ETestFixture.CreateMultiLineJournal(DateTime.Today, "Penjualan aki sebagian tempo",
            (kasId, 3_000_000m, 0m),
            (piutangId, 2_000_000m, 0m),
            (penjualanId, 0m, 5_000_000m));

        var result = await journalService.CreateAsync(dto);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(5_000_000m, result.Value!.TotalDebit);
        Assert.Equal(5_000_000m, result.Value.TotalCredit);
        Assert.Equal(3, result.Value.Lines.Count);
    }

    [Fact]
    public async Task F01_Create_ViewerRole_Allowed()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Viewer);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var dto = E2ETestFixture.Create2LineJournal(kasId, modalId, 10_000_000m, desc: "Setoran modal oleh Viewer");
        var result = await journalService.CreateAsync(dto);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(10_000_000m, result.Value!.TotalDebit);
    }

    [Fact]
    public async Task F01_Create_AdminRole_Allowed()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var bankId = _fixture.GetAccountId("1-1002");
        var modalId = _fixture.GetAccountId("3-1001");

        var dto = E2ETestFixture.Create2LineJournal(bankId, modalId, 25_000_000m, desc: "Setoran modal via Admin");
        var result = await journalService.CreateAsync(dto);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(25_000_000m, result.Value!.TotalDebit);
    }

    [Fact]
    public async Task F01_Create_PreservesDateAndDescription()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");
        var targetDate = new DateTime(2026, 3, 15);
        const string expectedDesc = "Investasi awal cabang pembantu";

        var dto = E2ETestFixture.Create2LineJournal(kasId, modalId, 5_000_000m, date: targetDate, desc: expectedDesc);
        var result = await journalService.CreateAsync(dto);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(targetDate.Date, result.Value!.TransactionDate.Date);
        Assert.Equal(expectedDesc, result.Value.Description);
    }

    [Fact]
    public async Task F01_Create_UserIdAndUserName_Assigned()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var dto = E2ETestFixture.Create2LineJournal(kasId, modalId, 2_000_000m);
        var result = await journalService.CreateAsync(dto);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("user-test", result.Value!.UserId);
        Assert.Equal("tester@sia.local", result.Value.UserName);
    }

    // =========================================================================
    // FEATURE 2: SEQUENTIAL JOURNAL NUMBERING & PERSISTENCE (F02)
    // =========================================================================

    [Fact]
    public async Task F02_Numbering_InitialFormat_StartsWith0001()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var dto = E2ETestFixture.Create2LineJournal(kasId, modalId, 1_000_000m, date: new DateTime(2026, 1, 10));
        var result = await journalService.CreateAsync(dto);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("JRN-2026-0001", result.Value!.JournalNumber);
    }

    [Fact]
    public async Task F02_Numbering_SequentialIncrement()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var res1 = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 100_000m));
        var res2 = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 200_000m));
        var res3 = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 300_000m));

        Assert.Equal("JRN-2026-0001", res1.Value!.JournalNumber);
        Assert.Equal("JRN-2026-0002", res2.Value!.JournalNumber);
        Assert.Equal("JRN-2026-0003", res3.Value!.JournalNumber);
    }

    [Fact]
    public async Task F02_Persistence_SavedDirectlyToDatabase()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var result = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 750_000m, desc: "Cek DB Langsung"));
        Assert.True(result.IsSuccess);

        await using var context = _fixture.CreateContext();
        var dbEntry = await context.JournalEntries.Include(j => j.Lines).SingleOrDefaultAsync(j => j.Id == result.Value!.Id);
        Assert.NotNull(dbEntry);
        Assert.Equal("JRN-2026-0001", dbEntry.JournalNumber);
        Assert.Equal(2, dbEntry.Lines.Count);
        Assert.False(dbEntry.IsDeleted);
    }

    [Fact]
    public async Task F02_Persistence_LinesProperlyLinked()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var result = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 450_000m));
        Assert.True(result.IsSuccess);

        await using var context = _fixture.CreateContext();
        var lines = await context.JournalEntryLines.Where(l => l.JournalEntryId == result.Value!.Id).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.AccountId == kasId && l.Debit == 450_000m && l.Credit == 0m);
        Assert.Contains(lines, l => l.AccountId == modalId && l.Debit == 0m && l.Credit == 450_000m);
    }

    [Fact]
    public async Task F02_Persistence_MultipleAccountsInLines()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var bankId = _fixture.GetAccountId("1-1002");
        var modalId = _fixture.GetAccountId("3-1001");

        var dto = E2ETestFixture.CreateMultiLineJournal(DateTime.Today, "Setoran Modal Campuran",
            (kasId, 2_000_000m, 0m),
            (bankId, 3_000_000m, 0m),
            (modalId, 0m, 5_000_000m));

        var result = await journalService.CreateAsync(dto);
        Assert.True(result.IsSuccess);

        await using var context = _fixture.CreateContext();
        var lines = await context.JournalEntryLines.Where(l => l.JournalEntryId == result.Value!.Id).ToListAsync();
        Assert.Equal(3, lines.Count);
        Assert.Equal(3, lines.Select(l => l.AccountId).Distinct().Count());
    }

    // =========================================================================
    // FEATURE 3: REAL-TIME ACCOUNT BALANCE UPDATE (F03)
    // =========================================================================

    [Fact]
    public async Task F03_BalanceUpdate_DebitAccount_IncreasedByDebit()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001"); // Debit normal
        var modalId = _fixture.GetAccountId("3-1001");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 1_200_000m));

        var kasBalance = await _fixture.GetAccountBalanceAsync(kasId);
        Assert.Equal(1_200_000m, kasBalance);
    }

    [Fact]
    public async Task F03_BalanceUpdate_DebitAccount_DecreasedByCredit()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");
        var bebanGajiId = _fixture.GetAccountId("6-1001");

        // Kas awal 5jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 5_000_000m));
        // Bayar gaji 2jt (Beban Gaji Debit, Kas Credit)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanGajiId, kasId, 2_000_000m));

        var kasBalance = await _fixture.GetAccountBalanceAsync(kasId);
        Assert.Equal(3_000_000m, kasBalance);
    }

    [Fact]
    public async Task F03_BalanceUpdate_CreditAccount_IncreasedByCredit()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var persediaanId = _fixture.GetAccountId("1-1004");
        var utangUsahaId = _fixture.GetAccountId("2-1001"); // Credit normal

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(persediaanId, utangUsahaId, 4_000_000m));

        var utangBalance = await _fixture.GetAccountBalanceAsync(utangUsahaId);
        Assert.Equal(4_000_000m, utangBalance);
    }

    [Fact]
    public async Task F03_BalanceUpdate_CreditAccount_DecreasedByDebit()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var persediaanId = _fixture.GetAccountId("1-1004");
        var utangUsahaId = _fixture.GetAccountId("2-1001");

        // Beli barang kredit 10jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(persediaanId, utangUsahaId, 10_000_000m));
        // Pelunasan utang 4jt (Utang Debit, Kas Credit)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(utangUsahaId, kasId, 4_000_000m));

        var utangBalance = await _fixture.GetAccountBalanceAsync(utangUsahaId);
        Assert.Equal(6_000_000m, utangBalance);
    }

    [Fact]
    public async Task F03_BalanceUpdate_CompoundJournal_UpdatesAllAccounts()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var piutangId = _fixture.GetAccountId("1-1003");
        var penjualanId = _fixture.GetAccountId("4-1001");

        var dto = E2ETestFixture.CreateMultiLineJournal(DateTime.Today, "Compound",
            (kasId, 2_000_000m, 0m),
            (piutangId, 1_000_000m, 0m),
            (penjualanId, 0m, 3_000_000m));
        await journalService.CreateAsync(dto);

        Assert.Equal(2_000_000m, await _fixture.GetAccountBalanceAsync(kasId));
        Assert.Equal(1_000_000m, await _fixture.GetAccountBalanceAsync(piutangId));
        Assert.Equal(3_000_000m, await _fixture.GetAccountBalanceAsync(penjualanId));
    }

    [Fact]
    public async Task F03_BalanceUpdate_ConsistentWithAccountServiceRecalculate()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var accountService = _fixture.CreateAccountService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var bankId = _fixture.GetAccountId("1-1002");
        var modalId = _fixture.GetAccountId("3-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 5_000_000m));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bankId, penjualanId, 3_000_000m));

        var kasBeforeRecalc = await _fixture.GetAccountBalanceAsync(kasId);
        var bankBeforeRecalc = await _fixture.GetAccountBalanceAsync(bankId);

        var recalcRes = await accountService.RecalculateBalancesAsync();
        Assert.True(recalcRes.IsSuccess);

        Assert.Equal(kasBeforeRecalc, await _fixture.GetAccountBalanceAsync(kasId));
        Assert.Equal(bankBeforeRecalc, await _fixture.GetAccountBalanceAsync(bankId));
    }

    // =========================================================================
    // FEATURE 4: JOURNAL SOFT DELETE & BALANCE ROLLBACK (F04)
    // =========================================================================

    [Fact]
    public async Task F04_Delete_Admin_SoftDeletesJournalEntry()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var createRes = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 1_000_000m));
        var delRes = await journalService.DeleteAsync(createRes.Value!.Id);

        Assert.True(delRes.IsSuccess, delRes.Error);

        await using var context = _fixture.CreateContext();
        var deletedJournal = await context.JournalEntries.IgnoreQueryFilters().SingleOrDefaultAsync(j => j.Id == createRes.Value.Id);
        Assert.NotNull(deletedJournal);
        Assert.True(deletedJournal.IsDeleted);
        Assert.NotNull(deletedJournal.DeletedAt);
    }

    [Fact]
    public async Task F04_Delete_SoftDeletesJournalEntryLines()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var createRes = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 1_000_000m));
        await journalService.DeleteAsync(createRes.Value!.Id);

        await using var context = _fixture.CreateContext();
        var lines = await context.JournalEntryLines.IgnoreQueryFilters().Where(l => l.JournalEntryId == createRes.Value.Id).ToListAsync();
        Assert.NotEmpty(lines);
        Assert.All(lines, l => Assert.True(l.IsDeleted));
    }

    [Fact]
    public async Task F04_Delete_ReversesDebitAccountBalance()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var createRes = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 2_500_000m));
        Assert.Equal(2_500_000m, await _fixture.GetAccountBalanceAsync(kasId));

        await journalService.DeleteAsync(createRes.Value!.Id);
        Assert.Equal(0m, await _fixture.GetAccountBalanceAsync(kasId));
    }

    [Fact]
    public async Task F04_Delete_ReversesCreditAccountBalance()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var createRes = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 3_000_000m));
        Assert.Equal(3_000_000m, await _fixture.GetAccountBalanceAsync(modalId));

        await journalService.DeleteAsync(createRes.Value!.Id);
        Assert.Equal(0m, await _fixture.GetAccountBalanceAsync(modalId));
    }

    [Fact]
    public async Task F04_Delete_ExcludedFromActiveQueries()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var createRes = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 1_000_000m));
        await journalService.DeleteAsync(createRes.Value!.Id);

        var getRes = await journalService.GetByIdAsync(createRes.Value.Id);
        Assert.True(getRes.IsFailure);

        var listRes = await journalService.GetAllAsync(new JournalFilterDto());
        Assert.DoesNotContain(listRes.Value!, j => j.Id == createRes.Value.Id);
    }

    [Fact]
    public async Task F04_Delete_RecalculateBalances_MatchesRollback()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var accountService = _fixture.CreateAccountService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var res1 = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 4_000_000m));
        var res2 = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 2_000_000m));

        // Hapus jurnal kedua
        await journalService.DeleteAsync(res2.Value!.Id);

        var kasAfterDel = await _fixture.GetAccountBalanceAsync(kasId);
        Assert.Equal(4_000_000m, kasAfterDel);

        await accountService.RecalculateBalancesAsync();
        Assert.Equal(4_000_000m, await _fixture.GetAccountBalanceAsync(kasId));
    }

    // =========================================================================
    // FEATURE 5: JOURNAL QUERIES & FILTERING (F05)
    // =========================================================================

    [Fact]
    public async Task F05_GetAll_DescendingOrderByDateAndId()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 100_000m, date: new DateTime(2026, 1, 5)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 200_000m, date: new DateTime(2026, 1, 15)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 300_000m, date: new DateTime(2026, 1, 10)));

        var result = await journalService.GetAllAsync(new JournalFilterDto());
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        Assert.Equal(new DateTime(2026, 1, 15), result.Value[0].TransactionDate.Date);
        Assert.Equal(new DateTime(2026, 1, 10), result.Value[1].TransactionDate.Date);
        Assert.Equal(new DateTime(2026, 1, 5), result.Value[2].TransactionDate.Date);
    }

    [Fact]
    public async Task F05_GetAll_DateRangeFilter()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 100_000m, date: new DateTime(2026, 2, 1)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 200_000m, date: new DateTime(2026, 2, 15)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 300_000m, date: new DateTime(2026, 3, 1)));

        var filter = new JournalFilterDto
        {
            DateFrom = new DateTime(2026, 2, 10),
            DateTo = new DateTime(2026, 2, 20)
        };
        var result = await journalService.GetAllAsync(filter);

        Assert.Single(result.Value!);
        Assert.Equal(200_000m, result.Value![0].TotalDebit);
    }

    [Fact]
    public async Task F05_GetAll_AccountIdFilter()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var bankId = _fixture.GetAccountId("1-1002");
        var modalId = _fixture.GetAccountId("3-1001");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 100_000m));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bankId, modalId, 200_000m));

        var filter = new JournalFilterDto { AccountId = bankId };
        var result = await journalService.GetAllAsync(filter);

        Assert.Single(result.Value!);
        Assert.Equal(200_000m, result.Value![0].TotalDebit);
    }

    [Fact]
    public async Task F05_GetAll_SearchByJournalNumber()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var res1 = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 100_000m));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 200_000m));

        var filter = new JournalFilterDto { Search = res1.Value!.JournalNumber };
        var result = await journalService.GetAllAsync(filter);

        Assert.Single(result.Value!);
        Assert.Equal(res1.Value.JournalNumber, result.Value![0].JournalNumber);
    }

    [Fact]
    public async Task F05_GetAll_SearchByDescription()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 100_000m, desc: "Pembayaran sewa toko"));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 200_000m, desc: "Beli aki motor"));

        var filter = new JournalFilterDto { Search = "sewa" };
        var result = await journalService.GetAllAsync(filter);

        Assert.Single(result.Value!);
        Assert.Contains("sewa", result.Value![0].Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task F05_GetById_ReturnsCompleteDetailWithLines()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var createRes = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 1_800_000m, desc: "Cek detail GetById"));
        var getRes = await journalService.GetByIdAsync(createRes.Value!.Id);

        Assert.True(getRes.IsSuccess);
        Assert.NotNull(getRes.Value);
        Assert.Equal("JRN-2026-0001", getRes.Value.JournalNumber);
        Assert.Equal(2, getRes.Value.Lines.Count);
        Assert.Equal("1-1001", getRes.Value.Lines[0].AccountCode);
        Assert.Equal("Kas", getRes.Value.Lines[0].AccountName);
        Assert.Equal("tester@sia.local", getRes.Value.UserName);
    }

    // =========================================================================
    // FEATURE 6: BALANCE SHEET SECTION GROUPING & CONTRA-ACCOUNTS (F06)
    // =========================================================================

    [Fact]
    public async Task F06_BalanceSheet_GroupedIntoStandardSections()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 5_000_000m));

        var result = await reportService.GetBalanceSheetAsync(DateTime.Today);
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEmpty(result.Value!.AssetSections);
        Assert.NotEmpty(result.Value.LiabilitySections);
        Assert.NotEmpty(result.Value.EquitySections);
    }

    [Fact]
    public async Task F06_BalanceSheet_ContraAccount_AccumulatedDepreciation_Negative()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var peralatanId = _fixture.GetAccountId("1-2001"); // Aset Tetap, Debit
        var akumPenyusutanId = _fixture.GetAccountId("1-2002"); // Aset Tetap, Contra (Credit)
        var kasId = _fixture.GetAccountId("1-1001");
        var bebanPenyusutanId = _fixture.GetAccountId("6-1004");

        // Beli peralatan 10jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(peralatanId, kasId, 10_000_000m));
        // Catat akumulasi penyusutan 1jt (Beban Debit, Akumulasi Credit)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanPenyusutanId, akumPenyusutanId, 1_000_000m));

        var result = await reportService.GetBalanceSheetAsync(DateTime.Today);
        Assert.True(result.IsSuccess);

        var fixedAssetSection = result.Value!.AssetSections.Single(s => s.Title.Contains("Tetap"));
        var lineAkum = fixedAssetSection.Lines.Single(l => l.AccountCode == "1-2002");
        Assert.Equal(-1_000_000m, lineAkum.Amount);
        Assert.Equal(9_000_000m, fixedAssetSection.Total); // 10jt - 1jt = 9jt
    }

    [Fact]
    public async Task F06_BalanceSheet_ContraAccount_OwnerDrawing_Negative()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");
        var priveId = _fixture.GetAccountId("3-1002"); // Ekuitas, Contra (Debit)

        // Modal 20jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 20_000_000m));
        // Tarik prive 3jt (Prive Debit, Kas Credit)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(priveId, kasId, 3_000_000m));

        var result = await reportService.GetBalanceSheetAsync(DateTime.Today);
        Assert.True(result.IsSuccess);

        var equitySection = result.Value!.EquitySections.Single();
        var priveLine = equitySection.Lines.Single(l => l.AccountCode == "3-1002");
        Assert.Equal(-3_000_000m, priveLine.Amount);
        Assert.Equal(17_000_000m, equitySection.Total); // 20jt - 3jt = 17jt
    }

    [Fact]
    public async Task F06_BalanceSheet_AsOfDateFilter_ExcludesFutureTransactions()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 5_000_000m, date: new DateTime(2026, 1, 15)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 3_000_000m, date: new DateTime(2026, 2, 1)));

        var bsJan = await reportService.GetBalanceSheetAsync(new DateTime(2026, 1, 31));
        Assert.Equal(5_000_000m, bsJan.Value!.TotalAssets);

        var bsFeb = await reportService.GetBalanceSheetAsync(new DateTime(2026, 2, 28));
        Assert.Equal(8_000_000m, bsFeb.Value!.TotalAssets);
    }

    [Fact]
    public async Task F06_BalanceSheet_ZeroState_ReturnsBalancedReport()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);

        var result = await reportService.GetBalanceSheetAsync(new DateTime(2026, 1, 1));
        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value!.TotalAssets);
        Assert.Equal(0m, result.Value.TotalLiabilities);
        Assert.Equal(0m, result.Value.TotalEquity);
    }

    // =========================================================================
    // FEATURE 7: CURRENT PERIOD NET INCOME & BALANCE SHEET INVARIANT (F07)
    // =========================================================================

    [Fact]
    public async Task F07_BalanceSheet_ProfitableOperations_IncreasesEquity()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");
        var bebanGajiId = _fixture.GetAccountId("6-1001");

        // Modal 10jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 10_000_000m));
        // Penjualan 6jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 6_000_000m));
        // Beban gaji 2jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanGajiId, kasId, 2_000_000m));

        var result = await reportService.GetBalanceSheetAsync(DateTime.Today);
        Assert.True(result.IsSuccess);
        Assert.Equal(4_000_000m, result.Value!.CurrentPeriodNetIncome); // 6jt - 2jt = 4jt
        Assert.Equal(14_000_000m, result.Value.TotalEquity); // Modal 10jt + Laba 4jt = 14jt
        Assert.Equal(14_000_000m, result.Value.TotalAssets); // Kas = 10 + 6 - 2 = 14jt
    }

    [Fact]
    public async Task F07_BalanceSheet_LossOperations_DecreasesEquity()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");
        var bebanSewaId = _fixture.GetAccountId("6-1002");

        // Modal 10jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 10_000_000m));
        // Beban sewa 3jt (Rugi 3jt)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanSewaId, kasId, 3_000_000m));

        var result = await reportService.GetBalanceSheetAsync(DateTime.Today);
        Assert.True(result.IsSuccess);
        Assert.Equal(-3_000_000m, result.Value!.CurrentPeriodNetIncome);
        Assert.Equal(7_000_000m, result.Value.TotalEquity); // 10jt - 3jt = 7jt
        Assert.Equal(7_000_000m, result.Value.TotalAssets);
    }

    [Fact]
    public async Task F07_BalanceSheet_StrictInvariant_TotalAssetsEqualsLiabilitiesPlusEquity()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var utangUsahaId = _fixture.GetAccountId("2-1001");
        var persediaanId = _fixture.GetAccountId("1-1004");
        var modalId = _fixture.GetAccountId("3-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");
        var hppId = _fixture.GetAccountId("5-1001");

        // Serangkaian transaksi
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 50_000_000m));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(persediaanId, utangUsahaId, 20_000_000m));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 15_000_000m));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(hppId, persediaanId, 9_000_000m));

        var result = await reportService.GetBalanceSheetAsync(DateTime.Today);
        Assert.True(result.IsSuccess);
        Assert.Equal(result.Value!.TotalAssets, result.Value.TotalLiabilities + result.Value.TotalEquity);
    }

    [Fact]
    public async Task F07_BalanceSheet_MultiPeriodAccumulation_UpToAsOfDate()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 1_000_000m, date: new DateTime(2026, 1, 5)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 2_000_000m, date: new DateTime(2026, 1, 20)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 4_000_000m, date: new DateTime(2026, 2, 10)));

        var bsJan = await reportService.GetBalanceSheetAsync(new DateTime(2026, 1, 31));
        Assert.Equal(3_000_000m, bsJan.Value!.CurrentPeriodNetIncome);

        var bsFeb = await reportService.GetBalanceSheetAsync(new DateTime(2026, 2, 28));
        Assert.Equal(7_000_000m, bsFeb.Value!.CurrentPeriodNetIncome);
    }

    [Fact]
    public async Task F07_BalanceSheet_NominalAccountsExcludedFromDirectSections()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");
        var bebanGajiId = _fixture.GetAccountId("6-1001");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 5_000_000m));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanGajiId, kasId, 2_000_000m));

        var result = await reportService.GetBalanceSheetAsync(DateTime.Today);
        Assert.True(result.IsSuccess);

        var allLines = result.Value!.AssetSections.SelectMany(s => s.Lines)
            .Concat(result.Value.LiabilitySections.SelectMany(s => s.Lines))
            .Concat(result.Value.EquitySections.SelectMany(s => s.Lines));

        Assert.DoesNotContain(allLines, l => l.AccountCode == "4-1001");
        Assert.DoesNotContain(allLines, l => l.AccountCode == "6-1001");
    }

    // =========================================================================
    // FEATURE 8: INCOME STATEMENT COMPUTATION (F08)
    // =========================================================================

    [Fact]
    public async Task F08_IncomeStatement_RevenueAggregation()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 5_000_000m, date: new DateTime(2026, 1, 10)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 7_000_000m, date: new DateTime(2026, 1, 20)));

        var result = await reportService.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        Assert.True(result.IsSuccess);
        Assert.Equal(12_000_000m, result.Value!.Revenue.Total);
    }

    [Fact]
    public async Task F08_IncomeStatement_SalesReturn_DeductsFromRevenue()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");
        var returPenjualanId = _fixture.GetAccountId("4-1002"); // Contra Revenue, Debit

        // Penjualan 10jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 10_000_000m, date: new DateTime(2026, 1, 10)));
        // Retur penjualan 1.5jt (Retur Debit, Kas Credit)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(returPenjualanId, kasId, 1_500_000m, date: new DateTime(2026, 1, 15)));

        var result = await reportService.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        Assert.True(result.IsSuccess);
        Assert.Equal(8_500_000m, result.Value!.Revenue.Total); // 10jt - 1.5jt = 8.5jt
    }

    [Fact]
    public async Task F08_IncomeStatement_CostOfGoodsSold_Aggregation()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var persediaanId = _fixture.GetAccountId("1-1004");
        var hppId = _fixture.GetAccountId("5-1001");
        var returBeliId = _fixture.GetAccountId("5-1002"); // Contra COGS, Credit
        var utangUsahaId = _fixture.GetAccountId("2-1001");

        // HPP 6jt (HPP Debit, Persediaan Credit)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(hppId, persediaanId, 6_000_000m, date: new DateTime(2026, 1, 10)));
        // Retur pembelian 500rb (Utang Debit, Retur Beli Credit)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(utangUsahaId, returBeliId, 500_000m, date: new DateTime(2026, 1, 15)));

        var result = await reportService.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        Assert.True(result.IsSuccess);
        Assert.Equal(5_500_000m, result.Value!.CostOfGoodsSold.Total); // 6jt - 500rb = 5.5jt
    }

    [Fact]
    public async Task F08_IncomeStatement_OperatingExpenses_Aggregation()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var bebanGajiId = _fixture.GetAccountId("6-1001");
        var bebanListrikId = _fixture.GetAccountId("6-1003");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanGajiId, kasId, 3_000_000m, date: new DateTime(2026, 1, 10)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanListrikId, kasId, 800_000m, date: new DateTime(2026, 1, 15)));

        var result = await reportService.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        Assert.True(result.IsSuccess);
        Assert.Equal(3_800_000m, result.Value!.OperatingExpenses.Total);
    }

    [Fact]
    public async Task F08_IncomeStatement_MultiTierProfit_CalculatedAccurately()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");
        var hppId = _fixture.GetAccountId("5-1001");
        var persediaanId = _fixture.GetAccountId("1-1004");
        var bebanGajiId = _fixture.GetAccountId("6-1001");
        var pendapatanLainId = _fixture.GetAccountId("4-2001");
        var bebanLainId = _fixture.GetAccountId("7-1001");

        // Penjualan 20jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 20_000_000m, date: new DateTime(2026, 1, 5)));
        // HPP 12jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(hppId, persediaanId, 12_000_000m, date: new DateTime(2026, 1, 10)));
        // Beban Gaji 4jt
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanGajiId, kasId, 4_000_000m, date: new DateTime(2026, 1, 15)));
        // Pendapatan Lain-lain 1jt (Kas Debit, Pendapatan Lain Credit)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, pendapatanLainId, 1_000_000m, date: new DateTime(2026, 1, 20)));
        // Beban Lain-lain 300rb (Beban Lain Debit, Kas Credit)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanLainId, kasId, 300_000m, date: new DateTime(2026, 1, 25)));

        var result = await reportService.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        Assert.True(result.IsSuccess);
        Assert.Equal(8_000_000m, result.Value!.GrossProfit); // 20jt - 12jt = 8jt
        Assert.Equal(4_000_000m, result.Value.OperatingProfit); // 8jt - 4jt = 4jt
        Assert.Equal(4_700_000m, result.Value.NetIncome); // 4jt + 1jt - 300rb = 4.7jt
    }

    [Fact]
    public async Task F08_IncomeStatement_DateRangeFiltering()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");

        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 10_000_000m, date: new DateTime(2026, 1, 15)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 15_000_000m, date: new DateTime(2026, 2, 10)));

        var resJan = await reportService.GetIncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        Assert.Equal(10_000_000m, resJan.Value!.Revenue.Total);

        var resFeb = await reportService.GetIncomeStatementAsync(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));
        Assert.Equal(15_000_000m, resFeb.Value!.Revenue.Total);
    }

    // =========================================================================
    // FEATURE 9: CASH FLOW STATEMENT COMPUTATION (F09)
    // =========================================================================

    [Fact]
    public async Task F09_CashFlow_OpeningCash_CalculatedPriorToDateFrom()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        // Setoran modal di bulan Januari
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 20_000_000m, date: new DateTime(2026, 1, 15)));

        // Laporan periode Februari
        var result = await reportService.GetCashFlowAsync(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));
        Assert.True(result.IsSuccess);
        Assert.Equal(20_000_000m, result.Value!.OpeningCash);
    }

    [Fact]
    public async Task F09_CashFlow_OperatingActivities_Classified()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");
        var bebanGajiId = _fixture.GetAccountId("6-1001");

        // Penjualan tunai 5jt (Kas masuk operasi)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 5_000_000m));
        // Beban gaji 1.5jt (Kas keluar operasi)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanGajiId, kasId, 1_500_000m));

        var result = await reportService.GetCashFlowAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
        Assert.True(result.IsSuccess);
        Assert.Equal(3_500_000m, result.Value!.OperatingActivities.Total); // 5jt - 1.5jt = 3.5jt
    }

    [Fact]
    public async Task F09_CashFlow_InvestingActivities_Classified()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var peralatanId = _fixture.GetAccountId("1-2001"); // Aset Tetap

        // Beli peralatan toko tunai 8jt (Kas keluar investasi)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(peralatanId, kasId, 8_000_000m));

        var result = await reportService.GetCashFlowAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
        Assert.True(result.IsSuccess);
        Assert.Equal(-8_000_000m, result.Value!.InvestingActivities.Total);
    }

    [Fact]
    public async Task F09_CashFlow_FinancingActivities_Classified()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var bankId = _fixture.GetAccountId("1-1002");
        var utangBankId = _fixture.GetAccountId("2-2001"); // Liabilitas Jangka Panjang
        var priveId = _fixture.GetAccountId("3-1002"); // Ekuitas (Prive)

        // Terima pinjaman bank 50jt (Kas masuk pendanaan)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bankId, utangBankId, 50_000_000m));
        // Tarik prive 5jt (Kas keluar pendanaan)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(priveId, bankId, 5_000_000m));

        var result = await reportService.GetCashFlowAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
        Assert.True(result.IsSuccess);
        Assert.Equal(45_000_000m, result.Value!.FinancingActivities.Total); // 50jt - 5jt = 45jt
    }

    [Fact]
    public async Task F09_CashFlow_ReconciliationInvariant_ClosingEqualsOpeningPlusNetChange()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");
        var penjualanId = _fixture.GetAccountId("4-1001");
        var peralatanId = _fixture.GetAccountId("1-2001");

        // Saldo awal di Januari
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 30_000_000m, date: new DateTime(2026, 1, 10)));

        // Transaksi di Februari
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, penjualanId, 10_000_000m, date: new DateTime(2026, 2, 5)));
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(peralatanId, kasId, 5_000_000m, date: new DateTime(2026, 2, 12)));

        var result = await reportService.GetCashFlowAsync(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));
        Assert.True(result.IsSuccess);
        Assert.Equal(30_000_000m, result.Value!.OpeningCash);
        Assert.Equal(5_000_000m, result.Value.NetCashChange); // +10jt operasi - 5jt investasi = +5jt
        Assert.Equal(35_000_000m, result.Value.ClosingCash);
        Assert.Equal(result.Value.ClosingCash, result.Value.OpeningCash + result.Value.NetCashChange);
    }

    [Fact]
    public async Task F09_CashFlow_NonCashJournal_Excluded()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var bebanPenyusutanId = _fixture.GetAccountId("6-1004");
        var akumPenyusutanId = _fixture.GetAccountId("1-2002");

        // Jurnal non-kas penyusutan
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bebanPenyusutanId, akumPenyusutanId, 1_000_000m));

        var result = await reportService.GetCashFlowAsync(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value!.NetCashChange);
        Assert.Equal(0m, result.Value.OperatingActivities.Total);
    }

    [Fact]
    public async Task F09_CashFlow_InternalCashTransfer_ZeroNetChange()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Admin);
        var journalService = _fixture.CreateJournalService(Roles.Admin);
        var kasId = _fixture.GetAccountId("1-1001");
        var bankId = _fixture.GetAccountId("1-1002");
        var modalId = _fixture.GetAccountId("3-1001");

        // Setor modal ke Kas 10jt di awal
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 10_000_000m, date: new DateTime(2026, 1, 1)));

        // Transfer dari Kas ke Bank (Debit Bank, Kredit Kas)
        await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(bankId, kasId, 4_000_000m, date: new DateTime(2026, 1, 15)));

        var result = await reportService.GetCashFlowAsync(new DateTime(2026, 1, 10), new DateTime(2026, 1, 20));
        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value!.NetCashChange); // Transfer internal tidak mengubah total kas
        Assert.Equal(result.Value.OpeningCash, result.Value.ClosingCash);
    }

    private const string ReadDenied = "Anda tidak memiliki hak akses untuk melihat data ini.";
    private const string WriteDenied = "Anda tidak memiliki hak akses untuk mengubah data ini.";

    // =========================================================================
    // FEATURE 10: SERVER-SIDE AUTHORIZATION ENFORCEMENT (F10)
    // =========================================================================

    [Fact]
    public async Task F10_Auth_Journal_StaffCreate_Rejected()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Staff);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var result = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 1_000_000m));
        Assert.True(result.IsFailure);
        Assert.Equal(WriteDenied, result.Error);
    }

    [Fact]
    public async Task F10_Auth_Journal_StaffGetAll_Rejected()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Staff);

        var result = await journalService.GetAllAsync(new JournalFilterDto());
        Assert.True(result.IsFailure);
        Assert.Equal(ReadDenied, result.Error);
    }

    [Fact]
    public async Task F10_Auth_Journal_ViewerCreate_Allowed()
    {
        await InitializeAsync();
        var journalService = _fixture.CreateJournalService(Roles.Viewer);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var result = await journalService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 1_000_000m));
        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task F10_Auth_Journal_ViewerDelete_Rejected()
    {
        await InitializeAsync();
        var adminService = _fixture.CreateJournalService(Roles.Admin);
        var viewerService = _fixture.CreateJournalService(Roles.Viewer);
        var kasId = _fixture.GetAccountId("1-1001");
        var modalId = _fixture.GetAccountId("3-1001");

        var createRes = await adminService.CreateAsync(E2ETestFixture.Create2LineJournal(kasId, modalId, 1_000_000m));

        var deleteRes = await viewerService.DeleteAsync(createRes.Value!.Id);
        Assert.True(deleteRes.IsFailure);
        Assert.Equal(WriteDenied, deleteRes.Error);
    }

    [Fact]
    public async Task F10_Auth_Reports_Staff_Rejected()
    {
        await InitializeAsync();
        var reportService = _fixture.CreateFinancialReportService(Roles.Staff);

        var bs = await reportService.GetBalanceSheetAsync(DateTime.Today);
        var is_ = await reportService.GetIncomeStatementAsync(DateTime.Today, DateTime.Today);
        var cf = await reportService.GetCashFlowAsync(DateTime.Today, DateTime.Today);

        Assert.True(bs.IsFailure);
        Assert.Equal(ReadDenied, bs.Error);
        Assert.True(is_.IsFailure);
        Assert.Equal(ReadDenied, is_.Error);
        Assert.True(cf.IsFailure);
        Assert.Equal(ReadDenied, cf.Error);
    }

    [Fact]
    public async Task F10_Auth_Reports_ViewerAndAdmin_Allowed()
    {
        await InitializeAsync();
        var adminReports = _fixture.CreateFinancialReportService(Roles.Admin);
        var viewerReports = _fixture.CreateFinancialReportService(Roles.Viewer);

        Assert.True((await adminReports.GetBalanceSheetAsync(DateTime.Today)).IsSuccess);
        Assert.True((await viewerReports.GetBalanceSheetAsync(DateTime.Today)).IsSuccess);
        Assert.True((await adminReports.GetIncomeStatementAsync(DateTime.Today, DateTime.Today)).IsSuccess);
        Assert.True((await viewerReports.GetIncomeStatementAsync(DateTime.Today, DateTime.Today)).IsSuccess);
        Assert.True((await adminReports.GetCashFlowAsync(DateTime.Today, DateTime.Today)).IsSuccess);
        Assert.True((await viewerReports.GetCashFlowAsync(DateTime.Today, DateTime.Today)).IsSuccess);
    }
}
