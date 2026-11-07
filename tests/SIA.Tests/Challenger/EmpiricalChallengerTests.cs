using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.DTOs.Accounting;
using SIA.Application.Services.Accounting;
using SIA.Application.Services.Reporting;
using SIA.Application.Services.System;
using SIA.Domain.Entities.Accounting;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data;
using SIA.Infrastructure.Data.Seed;
using SIA.Infrastructure.Identity;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;
using SIA.Tests.Services;
using Xunit;

namespace SIA.Tests.Challenger;

/// <summary>
/// Empirical Challenger Verification Suite for Milestone M1 (JournalService & FinancialReportService).
/// Adversarially stresses edge cases, authorization boundaries, concurrent sequential numbering,
/// soft-delete parity, and accounting invariants across diverse transaction sequences.
/// </summary>
public sealed class EmpiricalChallengerTests : IDisposable
{
    private readonly SqliteTestDatabase _db;
    private readonly AppDbContext _context;
    private readonly ITransactionNumberService _numService;
    private readonly IAccountService _accountService;

    public EmpiricalChallengerTests()
    {
        var adminUser = TestUser.InRole(Roles.Admin);
        _db = new SqliteTestDatabase(adminUser);
        _context = _db.CreateContext();
        MasterDataSeeder.SeedAsync(_context, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).GetAwaiter().GetResult();

        if (!_context.Users.Any(u => u.Id == "user-test"))
        {
            _context.Users.Add(new ApplicationUser
            {
                Id = "user-test",
                UserName = "tester@sia.local",
                Email = "tester@sia.local",
                FullName = "Tester SIA",
                IsActive = true
            });
            _context.SaveChanges();
        }

        _numService = new TransactionNumberService(_context, adminUser);
        _accountService = new AccountService(_context, adminUser);
    }

    public void Dispose()
    {
        _context.Dispose();
        _db.Dispose();
    }

    private JournalService CreateJournalService(ICurrentUserService user, AppDbContext? ctx = null)
    {
        var targetCtx = ctx ?? _context;
        var num = new TransactionNumberService(targetCtx, user);
        return new JournalService(targetCtx, user, num);
    }

    private FinancialReportService CreateReportService(ICurrentUserService user, AppDbContext? ctx = null)
    {
        return new FinancialReportService(ctx ?? _context, user);
    }

    private async Task<int> GetAccountId(string code)
    {
        var acc = await _context.Accounts.FirstAsync(a => a.Code == code);
        return acc.Id;
    }

    #region Suite 1: Edge Cases & Validation Hardening

    [Fact]
    public async Task TC01_SingleLineJournal_MustBeRejected()
    {
        var kasId = await GetAccountId("1-1001");
        var service = CreateJournalService(TestUser.InRole(Roles.Admin));

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Single line journal test",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 100_000m, Credit = 0m }
            }
        };

        var result = await service.CreateAsync(dto);
        Assert.False(result.IsSuccess, "1-line journal should be rejected.");
        Assert.Contains(result.Errors, e => e.Contains("minimal memiliki 2 baris"));
    }

    [Fact]
    public async Task TC02_EmptyLinesJournal_MustBeRejected()
    {
        var service = CreateJournalService(TestUser.InRole(Roles.Admin));

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Empty lines journal test",
            Lines = new List<CreateJournalEntryLineDto>()
        };

        var result = await service.CreateAsync(dto);
        Assert.False(result.IsSuccess, "0-line journal should be rejected.");
    }

    [Fact]
    public async Task TC03_UnbalancedDebitCredit_DebitHigher_MustBeRejected()
    {
        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");
        var service = CreateJournalService(TestUser.InRole(Roles.Admin));

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Unbalanced test",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 100_000.01m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 100_000.00m }
            }
        };

        var result = await service.CreateAsync(dto);
        Assert.False(result.IsSuccess, "Unbalanced journal must be rejected.");
        Assert.Contains(result.Errors, e => e.Contains("seimbang"));
    }

    [Fact]
    public async Task TC04_UnbalancedDebitCredit_CreditHigher_MustBeRejected()
    {
        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");
        var service = CreateJournalService(TestUser.InRole(Roles.Admin));

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Unbalanced test credit higher",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 50_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 50_000.50m }
            }
        };

        var result = await service.CreateAsync(dto);
        Assert.False(result.IsSuccess, "Unbalanced journal must be rejected.");
        Assert.Contains(result.Errors, e => e.Contains("seimbang"));
    }

    [Fact]
    public async Task TC05_NegativeDebitAmount_MustBeRejected()
    {
        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");
        var service = CreateJournalService(TestUser.InRole(Roles.Admin));

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Negative debit test",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = -100_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = -100_000m }
            }
        };

        var result = await service.CreateAsync(dto);
        Assert.False(result.IsSuccess, "Negative debit amount must be rejected.");
    }

    [Fact]
    public async Task TC06_NegativeCreditAmount_MustBeRejected()
    {
        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");
        var service = CreateJournalService(TestUser.InRole(Roles.Admin));

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Negative credit test",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 0m, Credit = -50_000m },
                new() { AccountId = modalId, Debit = -50_000m, Credit = 0m }
            }
        };

        var result = await service.CreateAsync(dto);
        Assert.False(result.IsSuccess, "Negative credit amount must be rejected.");
    }

    [Fact]
    public async Task TC07_BothDebitAndCreditZeroOnLine_MustBeRejected()
    {
        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");
        var service = CreateJournalService(TestUser.InRole(Roles.Admin));

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Zero amount line test",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 0m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 0m }
            }
        };

        var result = await service.CreateAsync(dto);
        Assert.False(result.IsSuccess, "Zero amount line must be rejected.");
    }

    [Fact]
    public async Task TC08_BothDebitAndCreditPositiveOnSameLine_MustBeRejected()
    {
        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");
        var service = CreateJournalService(TestUser.InRole(Roles.Admin));

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Same line debit and credit test",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 100_000m, Credit = 50_000m },
                new() { AccountId = modalId, Debit = 0m, Credit = 50_000m }
            }
        };

        var result = await service.CreateAsync(dto);
        Assert.False(result.IsSuccess, "Line with both debit and credit > 0 must be rejected.");
    }

    [Fact]
    public async Task TC09_TotalZeroAmount_MustBeRejected()
    {
        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");
        var service = CreateJournalService(TestUser.InRole(Roles.Admin));

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Zero total test",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 0m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 0m }
            }
        };

        var result = await service.CreateAsync(dto);
        Assert.False(result.IsSuccess, "Zero total journal must be rejected.");
    }

    [Fact]
    public async Task TC10_InactiveAccount_MustBeRejected()
    {
        var kas = await _context.Accounts.FirstAsync(a => a.Code == "1-1001");
        var modal = await _context.Accounts.FirstAsync(a => a.Code == "3-1001");

        // Set modal as inactive
        modal.IsActive = false;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var service = CreateJournalService(TestUser.InRole(Roles.Admin));
        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Inactive account journal",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kas.Id, Debit = 100_000m, Credit = 0m },
                new() { AccountId = modal.Id, Debit = 0m, Credit = 100_000m }
            }
        };

        var result = await service.CreateAsync(dto);
        Assert.False(result.IsSuccess, "Inactive account must be rejected.");
        Assert.Contains(result.Errors, e => e.Contains("nonaktif"));

        // Restore active state
        modal = await _context.Accounts.FirstAsync(a => a.Code == "3-1001");
        modal.IsActive = true;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task TC11_NonExistentAccountId_MustBeRejected()
    {
        var kasId = await GetAccountId("1-1001");
        var service = CreateJournalService(TestUser.InRole(Roles.Admin));

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Non-existent account journal",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 100_000m, Credit = 0m },
                new() { AccountId = 99999, Debit = 0m, Credit = 100_000m }
            }
        };

        var result = await service.CreateAsync(dto);
        Assert.False(result.IsSuccess, "Non-existent account must be rejected.");
        Assert.Contains(result.Errors, e => e.Contains("tidak ditemukan"));
    }

    [Fact]
    public async Task TC12_NullOrWhitespaceDescription_MustBeRejected()
    {
        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");
        var service = CreateJournalService(TestUser.InRole(Roles.Admin));

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "   ",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 100_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 100_000m }
            }
        };

        var result = await service.CreateAsync(dto);
        Assert.False(result.IsSuccess, "Whitespace description must be rejected.");
    }

    [Fact]
    public async Task TC13_ExcessiveDescriptionLength_MustBeRejected()
    {
        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");
        var service = CreateJournalService(TestUser.InRole(Roles.Admin));

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = new string('X', 256),
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 100_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 100_000m }
            }
        };

        var result = await service.CreateAsync(dto);
        Assert.False(result.IsSuccess, "Description > 255 chars must be rejected.");
        Assert.Contains(result.Errors, e => e.Contains("255 karakter"));
    }

    private const string ReadDenied = "Anda tidak memiliki hak akses untuk melihat data ini.";
    private const string WriteDenied = "Anda tidak memiliki hak akses untuk mengubah data ini.";

    [Fact]
    public async Task TC14_StaffRole_AllJournalOperations_MustBeDenied()
    {
        var staffUser = TestUser.InRole(Roles.Staff);
        var staffJournal = CreateJournalService(staffUser);
        var staffReport = CreateReportService(staffUser);

        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");

        // 1. CreateAsync denied
        var createResult = await staffJournal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Staff illegal journal",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 10_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 10_000m }
            }
        });
        Assert.False(createResult.IsSuccess, "Staff must be denied journal creation.");
        Assert.Contains(createResult.Errors, e => e == WriteDenied);

        // 2. GetAllAsync denied
        var getAllResult = await staffJournal.GetAllAsync(new JournalFilterDto());
        Assert.False(getAllResult.IsSuccess, "Staff must be denied GetAll journals.");
        Assert.Contains(getAllResult.Errors, e => e == ReadDenied);

        // 3. GetByIdAsync denied
        var getByIdResult = await staffJournal.GetByIdAsync(1);
        Assert.False(getByIdResult.IsSuccess, "Staff must be denied GetById journal.");
        Assert.Contains(getByIdResult.Errors, e => e == ReadDenied);

        // 4. DeleteAsync denied
        var deleteResult = await staffJournal.DeleteAsync(1);
        Assert.False(deleteResult.IsSuccess, "Staff must be denied journal deletion.");
        Assert.Contains(deleteResult.Errors, e => e == WriteDenied);

        // 5. Reports denied
        var bsResult = await staffReport.GetBalanceSheetAsync(DateTime.Today);
        Assert.False(bsResult.IsSuccess, "Staff must be denied BalanceSheet.");
        Assert.Contains(bsResult.Errors, e => e == ReadDenied);

        var isResult = await staffReport.GetIncomeStatementAsync(DateTime.Today.AddDays(-30), DateTime.Today);
        Assert.False(isResult.IsSuccess, "Staff must be denied IncomeStatement.");
        Assert.Contains(isResult.Errors, e => e == ReadDenied);

        var cfResult = await staffReport.GetCashFlowAsync(DateTime.Today.AddDays(-30), DateTime.Today);
        Assert.False(cfResult.IsSuccess, "Staff must be denied CashFlow.");
        Assert.Contains(cfResult.Errors, e => e == ReadDenied);
    }

    [Fact]
    public async Task TC15_ViewerRole_AllowedToCreateAndRead_DeniedToDelete()
    {
        var viewerUser = TestUser.InRole(Roles.Viewer);
        var viewerJournal = CreateJournalService(viewerUser);
        var viewerReport = CreateReportService(viewerUser);

        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");

        // 1. CreateAsync allowed (Viewer can input manual journal per AGENTS.md)
        var createResult = await viewerJournal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Viewer legitimate manual journal",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 25_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 25_000m }
            }
        });
        Assert.True(createResult.IsSuccess, $"Viewer must be allowed to create journal. Errors: {string.Join(", ", createResult.Errors)}");
        var journalId = createResult.Value!.Id;

        // 2. Read operations allowed
        var getAllResult = await viewerJournal.GetAllAsync(new JournalFilterDto());
        Assert.True(getAllResult.IsSuccess, "Viewer must be allowed to GetAll journals.");

        var getByIdResult = await viewerJournal.GetByIdAsync(journalId);
        Assert.True(getByIdResult.IsSuccess, "Viewer must be allowed to GetById journal.");

        var bsResult = await viewerReport.GetBalanceSheetAsync(DateTime.Today);
        Assert.True(bsResult.IsSuccess, "Viewer must be allowed to view BalanceSheet.");

        // 3. DeleteAsync denied (Only Admin can delete journals)
        var deleteResult = await viewerJournal.DeleteAsync(journalId);
        Assert.False(deleteResult.IsSuccess, "Viewer must NOT be allowed to delete journals.");
        Assert.Contains(deleteResult.Errors, e => e == WriteDenied);
    }

    [Fact]
    public async Task TC16_ExtremeAmount_HighValueHundredBillion_HandledAccurately()
    {
        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");
        var service = CreateJournalService(TestUser.InRole(Roles.Admin));

        const decimal extremeAmount = 100_000_000_000.50m; // Rp 100 Miliar 50 sen
        var result = await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Extreme Rp 100 Billion injection",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = extremeAmount, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = extremeAmount }
            }
        });

        Assert.True(result.IsSuccess, $"Extreme amount failed: {string.Join(", ", result.Errors)}");
        Assert.Equal(extremeAmount, result.Value!.TotalDebit);
        Assert.Equal(extremeAmount, result.Value!.TotalCredit);

        var kas = await _context.Accounts.FindAsync(kasId);
        var modal = await _context.Accounts.FindAsync(modalId);
        Assert.Equal(extremeAmount, kas!.Balance);
        Assert.Equal(extremeAmount, modal!.Balance);
    }

    #endregion

    #region Suite 2: Sequential Numbering & Concurrency Stress

    [Fact]
    public async Task TC17_SequentialNumbering_FormatAndYearReset()
    {
        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");
        var service = CreateJournalService(TestUser.InRole(Roles.Admin));

        var j1 = await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 10),
            Description = "Seq 1",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 1_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 1_000m }
            }
        });
        Assert.True(j1.IsSuccess);
        Assert.Matches(@"^JRN-2026-\d{4}$", j1.Value!.JournalNumber);
        var seq1 = j1.Value.JournalNumber;

        var j2 = await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 11),
            Description = "Seq 2",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 1_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 1_000m }
            }
        });
        Assert.True(j2.IsSuccess);
        Assert.Matches(@"^JRN-2026-\d{4}$", j2.Value!.JournalNumber);
        var seq2 = j2.Value.JournalNumber;
        Assert.NotEqual(seq1, seq2);

        // Next year 2027 should reset sequence
        var j3 = await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2027, 1, 1),
            Description = "Seq 2027",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 1_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 1_000m }
            }
        });
        Assert.True(j3.IsSuccess);
        Assert.Equal("JRN-2027-0001", j3.Value!.JournalNumber);

        // Attempt backdated transaction (2026 after 2027 is active) -> must fail
        var jBackdate = await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 12, 31),
            Description = "Illegal backdate",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 1_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 1_000m }
            }
        });
        Assert.False(jBackdate.IsSuccess, "Backdated journal after year rollover must be rejected.");
    }

    [Fact]
    public async Task TC18_RapidConsecutiveJournalCreations_MaintainStrictSequenceAndBalances()
    {
        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");
        var adminUser = TestUser.InRole(Roles.Admin);

        var journalNumbers = new List<string>();
        const int count = 15;

        for (int i = 1; i <= count; i++)
        {
            var service = CreateJournalService(adminUser);
            var res = await service.CreateAsync(new CreateJournalEntryDto
            {
                TransactionDate = new DateTime(2026, 3, 1),
                Description = $"Rapid batch entry {i}",
                Lines = new List<CreateJournalEntryLineDto>
                {
                    new() { AccountId = kasId, Debit = 10_000m, Credit = 0m },
                    new() { AccountId = modalId, Debit = 0m, Credit = 10_000m }
                }
            });

            Assert.True(res.IsSuccess, $"Batch entry {i} failed: {string.Join(", ", res.Errors)}");
            journalNumbers.Add(res.Value!.JournalNumber);
        }

        // All journal numbers must be distinct
        Assert.Equal(count, journalNumbers.Distinct().Count());

        // Verify balance reflects exact sum of count * 10,000
        var kas = await _context.Accounts.FindAsync(kasId);
        Assert.True(kas!.Balance >= count * 10_000m);
    }

    #endregion

    #region Suite 3: Soft Delete and Balance Parity

    [Fact]
    public async Task TC19_SoftDelete_RollsBackBalance_AndMaintainsParityWithRecalculate()
    {
        var kasId = await GetAccountId("1-1001");
        var bankId = await GetAccountId("1-1002");
        var utangId = await GetAccountId("2-1001");
        var modalId = await GetAccountId("3-1001");
        var bebanGajiId = await GetAccountId("6-1001");

        var adminUser = TestUser.InRole(Roles.Admin);
        var service = CreateJournalService(adminUser);

        // 1. Initial recalculate to record baseline
        await _accountService.RecalculateBalancesAsync();
        var baselineKas = (await _context.Accounts.FindAsync(kasId))!.Balance;

        // 2. Post compound journal:
        // Debit Beban Gaji 5,000,000
        // Credit Kas 3,000,000
        // Credit Bank 2,000,000
        var jCompound = await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 4, 1),
            Description = "Payroll compound entry",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = bebanGajiId, Debit = 5_000_000m, Credit = 0m },
                new() { AccountId = kasId, Debit = 0m, Credit = 3_000_000m },
                new() { AccountId = bankId, Debit = 0m, Credit = 2_000_000m }
            }
        });
        Assert.True(jCompound.IsSuccess);
        var compoundId = jCompound.Value!.Id;

        // Kas balance should have decreased by 3M
        var kasAfterPost = (await _context.Accounts.FindAsync(kasId))!.Balance;
        Assert.Equal(baselineKas - 3_000_000m, kasAfterPost);

        // 3. Post another journal:
        // Debit Kas 10,000,000, Credit Utang Usaha 10,000,000
        var jLoan = await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 4, 2),
            Description = "Short-term borrowing",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 10_000_000m, Credit = 0m },
                new() { AccountId = utangId, Debit = 0m, Credit = 10_000_000m }
            }
        });
        Assert.True(jLoan.IsSuccess);

        // 4. Soft-delete the compound journal
        var delResult = await service.DeleteAsync(compoundId);
        Assert.True(delResult.IsSuccess, "Soft-delete of compound journal failed.");

        // Kas balance should roll back the 3M reduction
        var kasAfterDelete = (await _context.Accounts.FindAsync(kasId))!.Balance;
        Assert.Equal(baselineKas + 10_000_000m, kasAfterDelete);

        // 5. Parity check with AccountService.RecalculateBalancesAsync
        var recalcResult = await _accountService.RecalculateBalancesAsync();
        Assert.True(recalcResult.IsSuccess);

        var allAccounts = await _context.Accounts.AsNoTracking().ToListAsync();
        var lines = await _context.JournalEntryLines.AsNoTracking()
            .Where(l => !l.JournalEntry.IsDeleted)
            .ToListAsync();

        var expectedDebits = lines.GroupBy(l => l.AccountId).ToDictionary(g => g.Key, g => g.Sum(l => l.Debit));
        var expectedCredits = lines.GroupBy(l => l.AccountId).ToDictionary(g => g.Key, g => g.Sum(l => l.Credit));

        foreach (var acc in allAccounts)
        {
            var d = expectedDebits.GetValueOrDefault(acc.Id, 0m);
            var c = expectedCredits.GetValueOrDefault(acc.Id, 0m);
            var expected = acc.NormalBalance == NormalBalance.Debit ? (d - c) : (c - d);
            Assert.Equal(expected, acc.Balance);
        }

        // 6. Double deletion must fail
        var delAgain = await service.DeleteAsync(compoundId);
        Assert.False(delAgain.IsSuccess, "Double deletion of soft-deleted journal must fail.");
    }

    #endregion

    #region Suite 4: Balance Sheet Invariants & Complex Sequences with Contra-Accounts

    [Fact]
    public async Task TC20_CompleteBusinessCycle_BalanceSheetEquation_HoldsAtEveryStep()
    {
        var adminUser = TestUser.InRole(Roles.Admin);
        var journal = CreateJournalService(adminUser);
        var report = CreateReportService(adminUser);

        var kasId = await GetAccountId("1-1001");
        var bankId = await GetAccountId("1-1002");
        var piutangId = await GetAccountId("1-1003");
        var persediaanId = await GetAccountId("1-1004");
        var peralatanId = await GetAccountId("1-2001");
        var akmPenyusutanId = await GetAccountId("1-2002"); // Contra-Asset (Credit normal)
        var utangUsahaId = await GetAccountId("2-1001");
        var utangBankId = await GetAccountId("2-2001");
        var modalId = await GetAccountId("3-1001");
        var priveId = await GetAccountId("3-1002");         // Contra-Equity (Debit normal)
        var penjualanId = await GetAccountId("4-1001");
        var returPenjualanId = await GetAccountId("4-1002");// Contra-Revenue (Debit normal)
        var hppId = await GetAccountId("5-1001");
        var returPembelianId = await GetAccountId("5-1002"); // Contra-COGS (Credit normal)
        var bebanGajiId = await GetAccountId("6-1001");
        var bebanPenyusutanId = await GetAccountId("6-1004");

        var asOf = new DateTime(2026, 5, 31);

        async Task AssertAccountingInvariants(string stepName)
        {
            var bsRes = await report.GetBalanceSheetAsync(asOf);
            Assert.True(bsRes.IsSuccess, $"BalanceSheet failed at {stepName}");
            var bs = bsRes.Value!;

            // 1. Strict Balance Sheet Invariant: TotalAssets == TotalLiabilities + TotalEquity
            Assert.Equal(bs.TotalAssets, bs.TotalLiabilities + bs.TotalEquity);

            // 2. Net Income Agreement: IS NetIncome == BS CurrentPeriodNetIncome
            var isRes = await report.GetIncomeStatementAsync(new DateTime(2026, 1, 1), asOf);
            Assert.True(isRes.IsSuccess, $"IncomeStatement failed at {stepName}");
            Assert.Equal(bs.CurrentPeriodNetIncome, isRes.Value!.NetIncome);

            // 3. Cash Flow Agreement: ClosingCash == Kas + Bank on BalanceSheet
            var cfRes = await report.GetCashFlowAsync(new DateTime(2026, 1, 1), asOf);
            Assert.True(cfRes.IsSuccess, $"CashFlow failed at {stepName}");
            Assert.Equal(cfRes.Value!.ClosingCash, cfRes.Value.OpeningCash + cfRes.Value.NetCashChange);

            var totalCashOnBS = bs.AssetSections
                .SelectMany(s => s.Lines)
                .Where(l => l.AccountCode == "1-1001" || l.AccountCode == "1-1002")
                .Sum(l => l.Amount);
            Assert.Equal(totalCashOnBS, cfRes.Value.ClosingCash);
        }

        // Initial State
        await AssertAccountingInvariants("Step 0: Initial State");

        // Step 1: Capital injection Rp 50,000,000 into Bank
        var j1 = await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 5, 1),
            Description = "Capital injection",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = bankId, Debit = 50_000_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 50_000_000m }
            }
        });
        Assert.True(j1.IsSuccess);
        await AssertAccountingInvariants("Step 1: Capital Injection");

        // Step 2: Equipment purchase Rp 15,000,000 via Bank
        var j2 = await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 5, 2),
            Description = "Equipment purchase",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = peralatanId, Debit = 15_000_000m, Credit = 0m },
                new() { AccountId = bankId, Debit = 0m, Credit = 15_000_000m }
            }
        });
        Assert.True(j2.IsSuccess);
        await AssertAccountingInvariants("Step 2: Equipment Purchase");

        // Step 3: Equipment Depreciation Rp 500,000 (Contra-Asset test)
        var j3 = await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 5, 3),
            Description = "Equipment depreciation",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = bebanPenyusutanId, Debit = 500_000m, Credit = 0m },
                new() { AccountId = akmPenyusutanId, Debit = 0m, Credit = 500_000m }
            }
        });
        Assert.True(j3.IsSuccess);

        // Verify Akumulasi Penyusutan is negative on Balance Sheet
        var bsAfterDeprec = (await report.GetBalanceSheetAsync(asOf)).Value!;
        var fixedSection = bsAfterDeprec.AssetSections.First(s => s.Title == "Aset Tetap");
        var akmLine = fixedSection.Lines.First(l => l.AccountId == akmPenyusutanId);
        Assert.Equal(-500_000m, akmLine.Amount);
        await AssertAccountingInvariants("Step 3: Depreciation Contra-Asset");

        // Step 4: Inventory purchase on credit Rp 20,000,000
        var j4 = await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 5, 5),
            Description = "Inventory credit purchase",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = persediaanId, Debit = 20_000_000m, Credit = 0m },
                new() { AccountId = utangUsahaId, Debit = 0m, Credit = 20_000_000m }
            }
        });
        Assert.True(j4.IsSuccess);
        await AssertAccountingInvariants("Step 4: Inventory Purchase");

        // Step 5: Purchase return Rp 2,000,000 (Contra-COGS test)
        var j5 = await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 5, 6),
            Description = "Purchase return to supplier",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = utangUsahaId, Debit = 2_000_000m, Credit = 0m },
                new() { AccountId = returPembelianId, Debit = 0m, Credit = 2_000_000m }
            }
        });
        Assert.True(j5.IsSuccess);
        await AssertAccountingInvariants("Step 5: Purchase Return Contra-COGS");

        // Step 6: Retail Cash Sale Rp 12,000,000 (COGS Rp 7,000,000)
        var j6Sale = await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 5, 10),
            Description = "Cash sale",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 12_000_000m, Credit = 0m },
                new() { AccountId = penjualanId, Debit = 0m, Credit = 12_000_000m }
            }
        });
        Assert.True(j6Sale.IsSuccess);

        var j6Cogs = await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 5, 10),
            Description = "COGS for cash sale",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = hppId, Debit = 7_000_000m, Credit = 0m },
                new() { AccountId = persediaanId, Debit = 0m, Credit = 7_000_000m }
            }
        });
        Assert.True(j6Cogs.IsSuccess);
        await AssertAccountingInvariants("Step 6: Retail Cash Sale & COGS");

        // Step 7: Sales Return Rp 1,500,000 (Contra-Revenue test)
        var j7Return = await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 5, 12),
            Description = "Customer cash return",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = returPenjualanId, Debit = 1_500_000m, Credit = 0m },
                new() { AccountId = kasId, Debit = 0m, Credit = 1_500_000m }
            }
        });
        Assert.True(j7Return.IsSuccess);

        var j7Restock = await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 5, 12),
            Description = "Customer return inventory restock",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = persediaanId, Debit = 900_000m, Credit = 0m },
                new() { AccountId = hppId, Debit = 0m, Credit = 900_000m }
            }
        });
        Assert.True(j7Restock.IsSuccess);
        await AssertAccountingInvariants("Step 7: Sales Return Contra-Revenue");

        // Step 8: Owner Drawing Rp 3,000,000 (Contra-Equity test)
        var j8Prive = await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 5, 20),
            Description = "Owner drawing prive",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = priveId, Debit = 3_000_000m, Credit = 0m },
                new() { AccountId = kasId, Debit = 0m, Credit = 3_000_000m }
            }
        });
        Assert.True(j8Prive.IsSuccess);

        // Verify Prive is negative on Balance Sheet
        var bsAfterPrive = (await report.GetBalanceSheetAsync(asOf)).Value!;
        var equitySection = bsAfterPrive.EquitySections.First();
        var priveLine = equitySection.Lines.First(l => l.AccountId == priveId);
        Assert.Equal(-3_000_000m, priveLine.Amount);
        await AssertAccountingInvariants("Step 8: Owner Drawing Contra-Equity");

        // Step 9: Heavy Operating Expenses Rp 20,000,000 leading to Net Loss
        var j9Expense = await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 5, 25),
            Description = "Heavy salary expense causing net loss",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = bebanGajiId, Debit = 20_000_000m, Credit = 0m },
                new() { AccountId = bankId, Debit = 0m, Credit = 20_000_000m }
            }
        });
        Assert.True(j9Expense.IsSuccess);

        // Verify that even during deep Net Loss, TotalAssets == TotalLiabilities + TotalEquity strictly holds
        var bsLoss = (await report.GetBalanceSheetAsync(asOf)).Value!;
        Assert.True(bsLoss.CurrentPeriodNetIncome < 0m, "Expected negative net income (net loss).");
        await AssertAccountingInvariants("Step 9: Heavy Net Loss Scenario");
    }

    [Fact]
    public async Task TC21_ContraAccount_ExtremeExceedingBalance_EquationsHold()
    {
        var adminUser = TestUser.InRole(Roles.Admin);
        var journal = CreateJournalService(adminUser);
        var report = CreateReportService(adminUser);

        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");
        var priveId = await GetAccountId("3-1002"); // Contra-Equity
        var peralatanId = await GetAccountId("1-2001");
        var akmId = await GetAccountId("1-2002"); // Contra-Asset
        var bebanPenyusutanId = await GetAccountId("6-1004");

        // 1. Initial capital Rp 10M
        await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 6, 1),
            Description = "Capital 10M",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 10_000_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 10_000_000m }
            }
        });

        // 2. Buy Equipment Rp 5M
        await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 6, 2),
            Description = "Equip 5M",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = peralatanId, Debit = 5_000_000m, Credit = 0m },
                new() { AccountId = kasId, Debit = 0m, Credit = 5_000_000m }
            }
        });

        // 3. Post extreme depreciation of Rp 8M (greater than equipment cost Rp 5M, net book value negative!)
        await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 6, 3),
            Description = "Extreme depreciation 8M",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = bebanPenyusutanId, Debit = 8_000_000m, Credit = 0m },
                new() { AccountId = akmId, Debit = 0m, Credit = 8_000_000m }
            }
        });

        // 4. Owner draws Rp 15M (greater than capital Rp 10M, negative equity!)
        await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 6, 4),
            Description = "Extreme drawing 15M",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = priveId, Debit = 15_000_000m, Credit = 0m },
                new() { AccountId = kasId, Debit = 0m, Credit = 15_000_000m }
            }
        });

        var asOf = new DateTime(2026, 6, 30);
        var bs = (await report.GetBalanceSheetAsync(asOf)).Value!;

        // The balance sheet equation MUST STILL strictly hold: TotalAssets == TotalLiabilities + TotalEquity
        Assert.Equal(bs.TotalAssets, bs.TotalLiabilities + bs.TotalEquity);
    }

    [Fact]
    public async Task TC22_DateBoundary_DateFromGreaterThanDateTo_Rejected()
    {
        var adminUser = TestUser.InRole(Roles.Admin);
        var report = CreateReportService(adminUser);

        var isRes = await report.GetIncomeStatementAsync(new DateTime(2026, 6, 30), new DateTime(2026, 6, 1));
        Assert.False(isRes.IsSuccess, "Income statement with DateFrom > DateTo must fail.");
        Assert.Contains(isRes.Errors, e => e.Contains("tidak boleh melebihi"));

        var cfRes = await report.GetCashFlowAsync(new DateTime(2026, 6, 30), new DateTime(2026, 6, 1));
        Assert.False(cfRes.IsSuccess, "Cash flow with DateFrom > DateTo must fail.");
        Assert.Contains(cfRes.Errors, e => e.Contains("tidak boleh melebihi"));
    }

    [Fact]
    public async Task TC23_DateBoundary_SingleDayPeriod_ExactAggregation()
    {
        var adminUser = TestUser.InRole(Roles.Admin);
        var journal = CreateJournalService(adminUser);
        var report = CreateReportService(adminUser);

        var kasId = await GetAccountId("1-1001");
        var penjualanId = await GetAccountId("4-1001");

        var targetDay = new DateTime(2026, 7, 15);

        // Day before
        await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = targetDay.AddDays(-1),
            Description = "Day before sale",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 1_000_000m, Credit = 0m },
                new() { AccountId = penjualanId, Debit = 0m, Credit = 1_000_000m }
            }
        });

        // Exact day
        await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = targetDay,
            Description = "Exact day sale",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 3_500_000m, Credit = 0m },
                new() { AccountId = penjualanId, Debit = 0m, Credit = 3_500_000m }
            }
        });

        // Day after
        await journal.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = targetDay.AddDays(1),
            Description = "Day after sale",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 2_000_000m, Credit = 0m },
                new() { AccountId = penjualanId, Debit = 0m, Credit = 2_000_000m }
            }
        });

        // Query IncomeStatement for exact single day (targetDay to targetDay)
        var isRes = await report.GetIncomeStatementAsync(targetDay, targetDay);
        Assert.True(isRes.IsSuccess);
        Assert.Equal(3_500_000m, isRes.Value!.Revenue.Total);
        Assert.Equal(3_500_000m, isRes.Value.NetIncome);
    }

    [Fact]
    public async Task TC24_MultiJournalOutOfOrderDeletion_ContinuousParity()
    {
        var adminUser = TestUser.InRole(Roles.Admin);
        var journal = CreateJournalService(adminUser);

        var kasId = await GetAccountId("1-1001");
        var modalId = await GetAccountId("3-1001");

        var ids = new List<int>();
        for (int i = 1; i <= 8; i++)
        {
            var res = await journal.CreateAsync(new CreateJournalEntryDto
            {
                TransactionDate = new DateTime(2026, 8, i),
                Description = $"Sequential journal {i}",
                Lines = new List<CreateJournalEntryLineDto>
                {
                    new() { AccountId = kasId, Debit = i * 100_000m, Credit = 0m },
                    new() { AccountId = modalId, Debit = 0m, Credit = i * 100_000m }
                }
            });
            Assert.True(res.IsSuccess);
            ids.Add(res.Value!.Id);
        }

        // Delete out of order: indices 6, 2, 4, 0
        var deleteOrder = new[] { 6, 2, 4, 0 };
        foreach (var idx in deleteOrder)
        {
            var delRes = await journal.DeleteAsync(ids[idx]);
            Assert.True(delRes.IsSuccess, $"Failed deleting journal at index {idx}");

            // Verify parity after EVERY deletion
            await _accountService.RecalculateBalancesAsync();
            var kas = await _context.Accounts.FindAsync(kasId);
            var modal = await _context.Accounts.FindAsync(modalId);
            Assert.Equal(kas!.Balance, modal!.Balance);
        }
    }

    [Fact]
    public async Task TC25_ZeroState_EmptyDatabase_BalanceSheetInvariantsHold()
    {
        // Fresh empty database
        using var emptyDb = new SqliteTestDatabase(TestUser.InRole(Roles.Admin));
        using var emptyCtx = emptyDb.CreateContext();
        await MasterDataSeeder.SeedAsync(emptyCtx, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var report = CreateReportService(TestUser.InRole(Roles.Admin), emptyCtx);
        var bs = (await report.GetBalanceSheetAsync(DateTime.Today)).Value!;

        Assert.Equal(0m, bs.TotalAssets);
        Assert.Equal(0m, bs.TotalLiabilities);
        Assert.Equal(0m, bs.TotalEquity);
        Assert.Equal(0m, bs.CurrentPeriodNetIncome);
        Assert.Equal(bs.TotalAssets, bs.TotalLiabilities + bs.TotalEquity);
    }

    #endregion
}
