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

public class JournalServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private JournalService CreateService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);
        var numbers = new TransactionNumberService(context, user);
        return new JournalService(context, user, numbers);
    }

    private AccountService CreateAccountService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);
        return new AccountService(context, user);
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

    #region CreateAsync Tests

    [Fact]
    public async Task Create_Admin_BalancedJournal_SuccessWithSequentialNumberAndBalanceUpdate()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var modalId = accounts["3-1001"];

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 15),
            Description = "Setoran modal awal kas",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 25_000_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 25_000_000m }
            }
        };

        var service = CreateService(Roles.Admin);
        var result = await service.CreateAsync(dto);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        Assert.Equal("JRN-2026-0001", result.Value.JournalNumber);
        Assert.Equal(new DateTime(2026, 1, 15), result.Value.TransactionDate);
        Assert.Equal("Setoran modal awal kas", result.Value.Description);
        Assert.Equal("user-test", result.Value.UserId);
        Assert.Equal("tester@sia.local", result.Value.UserName);
        Assert.Equal(25_000_000m, result.Value.TotalDebit);
        Assert.Equal(25_000_000m, result.Value.TotalCredit);
        Assert.Equal(2, result.Value.Lines.Count);

        // Verifikasi saldo akun di database
        await using var verifyContext = _db.CreateContext();
        var kas = await verifyContext.Accounts.SingleAsync(a => a.Id == kasId);
        var modal = await verifyContext.Accounts.SingleAsync(a => a.Id == modalId);

        // Kas (Normal: Debit) bertambah 25 jt
        Assert.Equal(25_000_000m, kas.Balance);
        // Modal (Normal: Credit) bertambah 25 jt
        Assert.Equal(25_000_000m, modal.Balance);
    }

    [Fact]
    public async Task Create_Viewer_AllowedToCreateJournal()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var pendapatanId = accounts["4-1001"];

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 2, 1),
            Description = "Penjualan aki tunai oleh viewer",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 1_500_000m, Credit = 0m },
                new() { AccountId = pendapatanId, Debit = 0m, Credit = 1_500_000m }
            }
        };

        var service = CreateService(Roles.Viewer);
        var result = await service.CreateAsync(dto);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("JRN-2026-0001", result.Value!.JournalNumber);
    }

    [Fact]
    public async Task Create_Staff_Denied()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var modalId = accounts["3-1001"];

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 10),
            Description = "Upaya input jurnal oleh staff",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 10_000_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 10_000_000m }
            }
        };

        var service = CreateService(Roles.Staff);
        var result = await service.CreateAsync(dto);

        Assert.True(result.IsFailure);
        Assert.Contains("hak akses", result.Error);

        await using var verifyContext = _db.CreateContext();
        Assert.False(await verifyContext.JournalEntries.AnyAsync());
    }

    [Fact]
    public async Task Create_Anonymous_Denied()
    {
        var accounts = await SeedFixtureAsync();
        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Anonymous attempt",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = accounts["1-1001"], Debit = 100_000m, Credit = 0m },
                new() { AccountId = accounts["3-1001"], Debit = 0m, Credit = 100_000m }
            }
        };

        var service = CreateService(); // Tanpa role
        var result = await service.CreateAsync(dto);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Create_SequentialNumbering_IncrementsCorrectly()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var bankId = accounts["1-1002"];
        var modalId = accounts["3-1001"];
        var service = CreateService(Roles.Admin);

        var res1 = await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 1),
            Description = "Jurnal pertama",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 5_000_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 5_000_000m }
            }
        });

        var res2 = await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 2),
            Description = "Jurnal kedua (transfer kas ke bank)",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = bankId, Debit = 2_000_000m, Credit = 0m },
                new() { AccountId = kasId, Debit = 0m, Credit = 2_000_000m }
            }
        });

        Assert.True(res1.IsSuccess);
        Assert.True(res2.IsSuccess);
        Assert.Equal("JRN-2026-0001", res1.Value!.JournalNumber);
        Assert.Equal("JRN-2026-0002", res2.Value!.JournalNumber);
    }

    [Fact]
    public async Task Create_CompoundMultiLineJournal_MutatesAllAccountBalancesCorrectly()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var piutangId = accounts["1-1003"];
        var pendapatanId = accounts["4-1001"];
        var pendapatanLainId = accounts["4-2001"];

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 20),
            Description = "Penjualan aki kombinasi kas dan tempo serta pendapatan jasa pasang",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 3_000_000m, Credit = 0m },
                new() { AccountId = piutangId, Debit = 2_000_000m, Credit = 0m },
                new() { AccountId = pendapatanId, Debit = 0m, Credit = 4_500_000m },
                new() { AccountId = pendapatanLainId, Debit = 0m, Credit = 500_000m }
            }
        };

        var service = CreateService(Roles.Admin);
        var result = await service.CreateAsync(dto);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(5_000_000m, result.Value!.TotalDebit);
        Assert.Equal(5_000_000m, result.Value!.TotalCredit);

        await using var verifyContext = _db.CreateContext();
        var kas = await verifyContext.Accounts.SingleAsync(a => a.Id == kasId);
        var piutang = await verifyContext.Accounts.SingleAsync(a => a.Id == piutangId);
        var pendapatan = await verifyContext.Accounts.SingleAsync(a => a.Id == pendapatanId);
        var pendapatanLain = await verifyContext.Accounts.SingleAsync(a => a.Id == pendapatanLainId);

        Assert.Equal(3_000_000m, kas.Balance);
        Assert.Equal(2_000_000m, piutang.Balance);
        Assert.Equal(4_500_000m, pendapatan.Balance);
        Assert.Equal(500_000m, pendapatanLain.Balance);
    }

    #endregion

    #region Validation Rejection Tests

    [Fact]
    public async Task Create_UnbalancedDebitsAndCredits_Rejected()
    {
        var accounts = await SeedFixtureAsync();
        var service = CreateService(Roles.Admin);

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Tidak balance",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = accounts["1-1001"], Debit = 1_000_000m, Credit = 0m },
                new() { AccountId = accounts["3-1001"], Debit = 0m, Credit = 900_000m }
            }
        };

        var result = await service.CreateAsync(dto);

        Assert.True(result.IsFailure);
        Assert.Contains("tidak seimbang", result.Error);
    }

    [Fact]
    public async Task Create_FewerThanTwoLines_Rejected()
    {
        var accounts = await SeedFixtureAsync();
        var service = CreateService(Roles.Admin);

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Hanya 1 baris",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = accounts["1-1001"], Debit = 1_000_000m, Credit = 0m }
            }
        };

        var result = await service.CreateAsync(dto);

        Assert.True(result.IsFailure);
        Assert.Contains("minimal memiliki 2 baris", result.Error);
    }

    [Fact]
    public async Task Create_ZeroTotalAmount_Rejected()
    {
        var accounts = await SeedFixtureAsync();
        var service = CreateService(Roles.Admin);

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Total nol",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = accounts["1-1001"], Debit = 0m, Credit = 0m },
                new() { AccountId = accounts["3-1001"], Debit = 0m, Credit = 0m }
            }
        };

        var result = await service.CreateAsync(dto);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Create_NegativeAmounts_Rejected()
    {
        var accounts = await SeedFixtureAsync();
        var service = CreateService(Roles.Admin);

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Nilai negatif",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = accounts["1-1001"], Debit = -500_000m, Credit = 0m },
                new() { AccountId = accounts["3-1001"], Debit = 0m, Credit = -500_000m }
            }
        };

        var result = await service.CreateAsync(dto);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Create_BothDebitAndCreditOnSameLine_Rejected()
    {
        var accounts = await SeedFixtureAsync();
        var service = CreateService(Roles.Admin);

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Debit dan kredit di baris yang sama",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = accounts["1-1001"], Debit = 1_000_000m, Credit = 500_000m },
                new() { AccountId = accounts["3-1001"], Debit = 0m, Credit = 500_000m }
            }
        };

        var result = await service.CreateAsync(dto);

        Assert.True(result.IsFailure);
        Assert.Contains("tepat salah satu Debit atau Kredit", result.Error);
    }

    [Fact]
    public async Task Create_MissingAccount_Rejected()
    {
        await SeedFixtureAsync();
        var service = CreateService(Roles.Admin);

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Akun tidak ada",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = 99999, Debit = 500_000m, Credit = 0m },
                new() { AccountId = 88888, Debit = 0m, Credit = 500_000m }
            }
        };

        var result = await service.CreateAsync(dto);

        Assert.True(result.IsFailure);
        Assert.Contains("Akun tidak ditemukan", result.Error);
    }

    [Fact]
    public async Task Create_InactiveAccount_Rejected()
    {
        var accounts = await SeedFixtureAsync();
        var bankId = accounts["1-1002"];
        var modalId = accounts["3-1001"];

        // Nonaktifkan akun Bank
        await using (var context = _db.CreateContext())
        {
            var bank = await context.Accounts.SingleAsync(a => a.Id == bankId);
            bank.IsActive = false;
            await context.SaveChangesAsync();
        }

        var service = CreateService(Roles.Admin);
        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Pakai akun nonaktif",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = bankId, Debit = 1_000_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 1_000_000m }
            }
        };

        var result = await service.CreateAsync(dto);

        Assert.True(result.IsFailure);
        Assert.Contains("nonaktif", result.Error);
    }

    [Fact]
    public async Task Create_EmptyDescription_Rejected()
    {
        var accounts = await SeedFixtureAsync();
        var service = CreateService(Roles.Admin);

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "   ",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = accounts["1-1001"], Debit = 1_000_000m, Credit = 0m },
                new() { AccountId = accounts["3-1001"], Debit = 0m, Credit = 1_000_000m }
            }
        };

        var result = await service.CreateAsync(dto);

        Assert.True(result.IsFailure);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task Delete_Admin_SoftDeletesAndRollsBackBalances()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var modalId = accounts["3-1001"];
        var service = CreateService(Roles.Admin);

        var createResult = await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 10),
            Description = "Jurnal yang akan dihapus",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 10_000_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 10_000_000m }
            }
        });
        Assert.True(createResult.IsSuccess);
        var journalId = createResult.Value!.Id;

        // Verifikasi saldo sebelum dihapus
        await using (var verifyContext = _db.CreateContext())
        {
            Assert.Equal(10_000_000m, (await verifyContext.Accounts.SingleAsync(a => a.Id == kasId)).Balance);
            Assert.Equal(10_000_000m, (await verifyContext.Accounts.SingleAsync(a => a.Id == modalId)).Balance);
        }

        // Hapus jurnal sebagai Admin
        var deleteResult = await service.DeleteAsync(journalId);
        Assert.True(deleteResult.IsSuccess, deleteResult.Error);

        // Verifikasi soft-delete dan saldo telah di-rollback
        await using (var verifyContext = _db.CreateContext())
        {
            // Jurnal tidak terlihat di query biasa (global query filter IsDeleted)
            Assert.False(await verifyContext.JournalEntries.AnyAsync(j => j.Id == journalId));

            // Periksa dengan IgnoreQueryFilters
            var deletedJournal = await verifyContext.JournalEntries
                .IgnoreQueryFilters()
                .Include(j => j.Lines)
                .SingleAsync(j => j.Id == journalId);

            Assert.True(deletedJournal.IsDeleted);
            Assert.NotNull(deletedJournal.DeletedAt);
            Assert.All(deletedJournal.Lines, l => Assert.True(l.IsDeleted));

            // Saldo akun kembali ke 0
            var kas = await verifyContext.Accounts.SingleAsync(a => a.Id == kasId);
            var modal = await verifyContext.Accounts.SingleAsync(a => a.Id == modalId);
            Assert.Equal(0m, kas.Balance);
            Assert.Equal(0m, modal.Balance);
        }
    }

    [Theory]
    [InlineData(Roles.Viewer)]
    [InlineData(Roles.Staff)]
    public async Task Delete_NonAdmin_Denied(string role)
    {
        var accounts = await SeedFixtureAsync();
        var adminService = CreateService(Roles.Admin);

        var created = await adminService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = DateTime.Today,
            Description = "Jurnal uji delete unauthorized",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = accounts["1-1001"], Debit = 1_000_000m, Credit = 0m },
                new() { AccountId = accounts["3-1001"], Debit = 0m, Credit = 1_000_000m }
            }
        });
        Assert.True(created.IsSuccess);

        var nonAdminService = CreateService(role);
        var result = await nonAdminService.DeleteAsync(created.Value!.Id);

        Assert.True(result.IsFailure);
        Assert.Contains("hak akses", result.Error);

        await using var verifyContext = _db.CreateContext();
        Assert.True(await verifyContext.JournalEntries.AnyAsync(j => j.Id == created.Value.Id));
    }

    [Fact]
    public async Task Delete_NonExistentJournal_ReturnsFailure()
    {
        await SeedFixtureAsync();
        var service = CreateService(Roles.Admin);

        var result = await service.DeleteAsync(99999);

        Assert.True(result.IsFailure);
        Assert.Contains("tidak ditemukan", result.Error);
    }

    #endregion

    #region Parity with AccountService.RecalculateBalancesAsync

    [Fact]
    public async Task Parity_AccountBalanceMatchesRecalculation_AfterMultipleCreationsAndDeletions()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var bankId = accounts["1-1002"];
        var piutangId = accounts["1-1003"];
        var utangId = accounts["2-1001"];
        var modalId = accounts["3-1001"];
        var pendapatanId = accounts["4-1001"];
        var bebanGajiId = accounts["6-1001"];

        var journalService = CreateService(Roles.Admin);
        var accountService = CreateAccountService(Roles.Admin);

        // J1: Setoran modal ke Kas dan Bank
        var j1 = await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 1),
            Description = "Modal awal Kas dan Bank",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 20_000_000m, Credit = 0m },
                new() { AccountId = bankId, Debit = 30_000_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 50_000_000m }
            }
        });
        Assert.True(j1.IsSuccess);

        // J2: Penjualan tunai & kredit
        var j2 = await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 5),
            Description = "Penjualan aki",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 5_000_000m, Credit = 0m },
                new() { AccountId = piutangId, Debit = 3_000_000m, Credit = 0m },
                new() { AccountId = pendapatanId, Debit = 0m, Credit = 8_000_000m }
            }
        });
        Assert.True(j2.IsSuccess);

        // J3: Bayar beban gaji tunai
        var j3 = await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 10),
            Description = "Beban gaji karyawan",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = bebanGajiId, Debit = 4_000_000m, Credit = 0m },
                new() { AccountId = kasId, Debit = 0m, Credit = 4_000_000m }
            }
        });
        Assert.True(j3.IsSuccess);

        // J4: Utang usaha pembelian perlengkapan (akan dihapus nanti)
        var j4 = await journalService.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 12),
            Description = "Utang pembelian perlengkapan (keliru)",
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = kasId, Debit = 2_000_000m, Credit = 0m },
                new() { AccountId = utangId, Debit = 0m, Credit = 2_000_000m }
            }
        });
        Assert.True(j4.IsSuccess);

        // Cek paritas sebelum delete
        await using (var ctx = _db.CreateContext())
        {
            var balancesBefore = await ctx.Accounts.ToDictionaryAsync(a => a.Id, a => a.Balance);
            await accountService.RecalculateBalancesAsync();
        }
        await using (var ctx = _db.CreateContext())
        {
            var balancesAfter = await ctx.Accounts.ToDictionaryAsync(a => a.Id, a => a.Balance);
            Assert.Equal(23_000_000m, balancesAfter[kasId]);
            Assert.Equal(30_000_000m, balancesAfter[bankId]);
            Assert.Equal(3_000_000m, balancesAfter[piutangId]);
            Assert.Equal(50_000_000m, balancesAfter[modalId]);
            Assert.Equal(8_000_000m, balancesAfter[pendapatanId]);
            Assert.Equal(4_000_000m, balancesAfter[bebanGajiId]);
            Assert.Equal(2_000_000m, balancesAfter[utangId]);
        }

        // Hapus J4
        var delResult = await journalService.DeleteAsync(j4.Value!.Id);
        Assert.True(delResult.IsSuccess);

        // Jalankan rekalkulasi saldo dan pastikan saldo Account.Balance pasca delete identik dengan hasil rekalkulasi
        var recalcResult = await accountService.RecalculateBalancesAsync();
        Assert.True(recalcResult.IsSuccess);

        await using (var ctx = _db.CreateContext())
        {
            var finalBalances = await ctx.Accounts.ToDictionaryAsync(a => a.Id, a => a.Balance);

            // Kas: 20 jt + 5 jt - 4 jt = 21 jt (J4 2 jt telah dibatalkan)
            Assert.Equal(21_000_000m, finalBalances[kasId]);
            Assert.Equal(30_000_000m, finalBalances[bankId]);
            Assert.Equal(3_000_000m, finalBalances[piutangId]);
            Assert.Equal(50_000_000m, finalBalances[modalId]);
            Assert.Equal(8_000_000m, finalBalances[pendapatanId]);
            Assert.Equal(4_000_000m, finalBalances[bebanGajiId]);
            Assert.Equal(0m, finalBalances[utangId]);
        }
    }

    #endregion

    #region GetAllAsync and GetByIdAsync Queries and Filtering

    [Fact]
    public async Task GetAll_DateRangeFilter_ReturnsOnlyMatchingJournals()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var modalId = accounts["3-1001"];
        var service = CreateService(Roles.Admin);

        await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 5),
            Description = "Januari 5",
            Lines = new() { new() { AccountId = kasId, Debit = 100m }, new() { AccountId = modalId, Credit = 100m } }
        });
        await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 15),
            Description = "Januari 15",
            Lines = new() { new() { AccountId = kasId, Debit = 200m }, new() { AccountId = modalId, Credit = 200m } }
        });
        await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 25),
            Description = "Januari 25",
            Lines = new() { new() { AccountId = kasId, Debit = 300m }, new() { AccountId = modalId, Credit = 300m } }
        });

        var filtered = await service.GetAllAsync(new JournalFilterDto
        {
            DateFrom = new DateTime(2026, 1, 10),
            DateTo = new DateTime(2026, 1, 20)
        });

        Assert.True(filtered.IsSuccess);
        Assert.Single(filtered.Value!);
        Assert.Equal("Januari 15", filtered.Value![0].Description);
    }

    [Fact]
    public async Task GetAll_AccountIdFilter_ReturnsOnlyJournalsContainingAccount()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var bankId = accounts["1-1002"];
        var modalId = accounts["3-1001"];
        var service = CreateService(Roles.Admin);

        await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 1),
            Description = "Kas and Modal",
            Lines = new() { new() { AccountId = kasId, Debit = 100m }, new() { AccountId = modalId, Credit = 100m } }
        });
        await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 2),
            Description = "Bank and Modal",
            Lines = new() { new() { AccountId = bankId, Debit = 200m }, new() { AccountId = modalId, Credit = 200m } }
        });

        var result = await service.GetAllAsync(new JournalFilterDto { AccountId = bankId });

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("Bank and Modal", result.Value![0].Description);
    }

    [Fact]
    public async Task GetAll_SearchFilter_MatchesJournalNumberOrDescription()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var modalId = accounts["3-1001"];
        var service = CreateService(Roles.Admin);

        var j1 = await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 1),
            Description = "Penerimaan piutang toko",
            Lines = new() { new() { AccountId = kasId, Debit = 100m }, new() { AccountId = modalId, Credit = 100m } }
        });
        await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 2),
            Description = "Beban listrik bulanan",
            Lines = new() { new() { AccountId = kasId, Debit = 200m }, new() { AccountId = modalId, Credit = 200m } }
        });

        // Cari berdasarkan deskripsi
        var searchDesc = await service.GetAllAsync(new JournalFilterDto { Search = "listrik" });
        Assert.True(searchDesc.IsSuccess);
        Assert.Single(searchDesc.Value!);
        Assert.Equal("Beban listrik bulanan", searchDesc.Value![0].Description);

        // Cari berdasarkan nomor jurnal
        var searchNum = await service.GetAllAsync(new JournalFilterDto { Search = j1.Value!.JournalNumber });
        Assert.True(searchNum.IsSuccess);
        Assert.Single(searchNum.Value!);
        Assert.Equal(j1.Value.JournalNumber, searchNum.Value![0].JournalNumber);
    }

    [Fact]
    public async Task GetAll_OrderedDescendingByDateAndId()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var modalId = accounts["3-1001"];
        var service = CreateService(Roles.Admin);

        await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 5),
            Description = "Tanggal 5",
            Lines = new() { new() { AccountId = kasId, Debit = 100m }, new() { AccountId = modalId, Credit = 100m } }
        });
        await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 15),
            Description = "Tanggal 15 #1",
            Lines = new() { new() { AccountId = kasId, Debit = 200m }, new() { AccountId = modalId, Credit = 200m } }
        });
        await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 15),
            Description = "Tanggal 15 #2",
            Lines = new() { new() { AccountId = kasId, Debit = 300m }, new() { AccountId = modalId, Credit = 300m } }
        });

        var result = await service.GetAllAsync(new JournalFilterDto());

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        // Urutan tanggal 15 #2, lalu 15 #1, lalu tanggal 5
        Assert.Equal("Tanggal 15 #2", result.Value[0].Description);
        Assert.Equal("Tanggal 15 #1", result.Value[1].Description);
        Assert.Equal("Tanggal 5", result.Value[2].Description);
    }

    [Fact]
    public async Task GetById_ReturnsFullDtoWithLinesAndUserName()
    {
        var accounts = await SeedFixtureAsync();
        var kasId = accounts["1-1001"];
        var modalId = accounts["3-1001"];
        var service = CreateService(Roles.Admin);

        var created = await service.CreateAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 10),
            Description = "Jurnal detail",
            Lines = new()
            {
                new() { AccountId = kasId, Debit = 750_000m, Credit = 0m },
                new() { AccountId = modalId, Debit = 0m, Credit = 750_000m }
            }
        });
        Assert.True(created.IsSuccess);

        var result = await service.GetByIdAsync(created.Value!.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(created.Value.Id, result.Value!.Id);
        Assert.Equal("JRN-2026-0001", result.Value.JournalNumber);
        Assert.Equal("Jurnal detail", result.Value.Description);
        Assert.Equal("tester@sia.local", result.Value.UserName);
        Assert.Equal(2, result.Value.Lines.Count);
        Assert.Equal("1-1001", result.Value.Lines[0].AccountCode);
        Assert.Equal("Kas", result.Value.Lines[0].AccountName);
        Assert.Equal(750_000m, result.Value.Lines[0].Debit);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsFailure()
    {
        await SeedFixtureAsync();
        var service = CreateService(Roles.Admin);

        var result = await service.GetByIdAsync(99999);

        Assert.True(result.IsFailure);
        Assert.Contains("tidak ditemukan", result.Error);
    }

    [Fact]
    public async Task GetAllAndGetById_Staff_Denied()
    {
        await SeedFixtureAsync();
        var staffService = CreateService(Roles.Staff);

        var getAllResult = await staffService.GetAllAsync(new JournalFilterDto());
        var getByIdResult = await staffService.GetByIdAsync(1);

        Assert.True(getAllResult.IsFailure);
        Assert.Contains("hak akses", getAllResult.Error);

        Assert.True(getByIdResult.IsFailure);
        Assert.Contains("hak akses", getByIdResult.Error);
    }

    #endregion

    #region In-Process Test Runner

    internal static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("    RUNNING JOURNAL SERVICE TESTS (R3 SUITE)     ");
        Console.WriteLine("=================================================");

        var journalTests = new (string Name, Func<JournalServiceTests, Task> Action)[]
        {
            ("Create_Admin_BalancedJournal_SuccessWithSequentialNumberAndBalanceUpdate", t => t.Create_Admin_BalancedJournal_SuccessWithSequentialNumberAndBalanceUpdate()),
            ("Create_Viewer_AllowedToCreateJournal", t => t.Create_Viewer_AllowedToCreateJournal()),
            ("Create_Staff_Denied", t => t.Create_Staff_Denied()),
            ("Create_Anonymous_Denied", t => t.Create_Anonymous_Denied()),
            ("Create_SequentialNumbering_IncrementsCorrectly", t => t.Create_SequentialNumbering_IncrementsCorrectly()),
            ("Create_CompoundMultiLineJournal_MutatesAllAccountBalancesCorrectly", t => t.Create_CompoundMultiLineJournal_MutatesAllAccountBalancesCorrectly()),
            ("Create_UnbalancedDebitsAndCredits_Rejected", t => t.Create_UnbalancedDebitsAndCredits_Rejected()),
            ("Create_FewerThanTwoLines_Rejected", t => t.Create_FewerThanTwoLines_Rejected()),
            ("Create_ZeroTotalAmount_Rejected", t => t.Create_ZeroTotalAmount_Rejected()),
            ("Create_NegativeAmounts_Rejected", t => t.Create_NegativeAmounts_Rejected()),
            ("Create_BothDebitAndCreditOnSameLine_Rejected", t => t.Create_BothDebitAndCreditOnSameLine_Rejected()),
            ("Create_MissingAccount_Rejected", t => t.Create_MissingAccount_Rejected()),
            ("Create_InactiveAccount_Rejected", t => t.Create_InactiveAccount_Rejected()),
            ("Create_EmptyDescription_Rejected", t => t.Create_EmptyDescription_Rejected()),
            ("Delete_Admin_SoftDeletesAndRollsBackBalances", t => t.Delete_Admin_SoftDeletesAndRollsBackBalances()),
            ("Delete_NonAdmin_Denied_Viewer", t => t.Delete_NonAdmin_Denied(Roles.Viewer)),
            ("Delete_NonAdmin_Denied_Staff", t => t.Delete_NonAdmin_Denied(Roles.Staff)),
            ("Delete_NonExistentJournal_ReturnsFailure", t => t.Delete_NonExistentJournal_ReturnsFailure()),
            ("Parity_AccountBalanceMatchesRecalculation_AfterMultipleCreationsAndDeletions", t => t.Parity_AccountBalanceMatchesRecalculation_AfterMultipleCreationsAndDeletions()),
            ("GetAll_DateRangeFilter_ReturnsOnlyMatchingJournals", t => t.GetAll_DateRangeFilter_ReturnsOnlyMatchingJournals()),
            ("GetAll_AccountIdFilter_ReturnsOnlyJournalsContainingAccount", t => t.GetAll_AccountIdFilter_ReturnsOnlyJournalsContainingAccount()),
            ("GetAll_SearchFilter_MatchesJournalNumberOrDescription", t => t.GetAll_SearchFilter_MatchesJournalNumberOrDescription()),
            ("GetAll_OrderedDescendingByDateAndId", t => t.GetAll_OrderedDescendingByDateAndId()),
            ("GetById_ReturnsFullDtoWithLinesAndUserName", t => t.GetById_ReturnsFullDtoWithLinesAndUserName()),
            ("GetById_NonExistent_ReturnsFailure", t => t.GetById_NonExistent_ReturnsFailure()),
            ("GetAllAndGetById_Staff_Denied", t => t.GetAllAndGetById_Staff_Denied()),
        };

        int passed = 0;
        int failed = 0;

        foreach (var (name, action) in journalTests)
        {
            using var fixture = new JournalServiceTests();
            try
            {
                await action(fixture);
                passed++;
                Console.WriteLine($"  [PASS] {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] {name} - Error: {ex.Message}");
                Console.ResetColor();
            }
        }

        Console.WriteLine("\n=================================================");
        Console.WriteLine(" RUNNING FINANCIAL REPORT SERVICE TESTS (R3)    ");
        Console.WriteLine("=================================================");

        var reportTests = new (string Name, Func<FinancialReportServiceTests, Task> Action)[]
        {
            ("Reports_Admin_Allowed", t => t.Reports_AdminAndViewer_Allowed(Roles.Admin)),
            ("Reports_Viewer_Allowed", t => t.Reports_AdminAndViewer_Allowed(Roles.Viewer)),
            ("Reports_Staff_Denied", t => t.Reports_Staff_Denied()),
            ("Reports_Anonymous_Denied", t => t.Reports_Anonymous_Denied()),
            ("IncomeStatement_DateFromGreaterThanDateTo_Rejected", t => t.IncomeStatement_DateFromGreaterThanDateTo_Rejected()),
            ("CashFlow_DateFromGreaterThanDateTo_Rejected", t => t.CashFlow_DateFromGreaterThanDateTo_Rejected()),
            ("BalanceSheet_ZeroState_ReturnsBalancedReport", t => t.BalanceSheet_ZeroState_ReturnsBalancedReport()),
            ("BalanceSheet_SectionsAndContraAccounts_CalculatedCorrectly", t => t.BalanceSheet_SectionsAndContraAccounts_CalculatedCorrectly()),
            ("BalanceSheet_ExcludesFutureTransactionsAndSoftDeletedJournals", t => t.BalanceSheet_ExcludesFutureTransactionsAndSoftDeletedJournals()),
            ("IncomeStatement_MultiTierProfit_CalculatedAccurately", t => t.IncomeStatement_MultiTierProfit_CalculatedAccurately()),
            ("IncomeStatement_DateFilter_ExcludesTransactionsOutsidePeriod", t => t.IncomeStatement_DateFilter_ExcludesTransactionsOutsidePeriod()),
            ("CashFlow_OpeningCash_Categorization_AndReconciliation", t => t.CashFlow_OpeningCash_Categorization_AndReconciliation()),
            ("CashFlow_NonCashJournal_ExcludedFromActivities", t => t.CashFlow_NonCashJournal_ExcludedFromActivities()),
        };

        foreach (var (name, action) in reportTests)
        {
            using var fixture = new FinancialReportServiceTests();
            try
            {
                await action(fixture);
                passed++;
                Console.WriteLine($"  [PASS] {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] {name} - Error: {ex.Message}");
                Console.ResetColor();
            }
        }

        Console.WriteLine("\n=================================================");
        Console.WriteLine($"TOTAL MILESTONE M2 TESTS: {passed + failed} | PASSED: {passed} | FAILED: {failed}");
        Console.WriteLine("=================================================");
        return failed > 0 ? 1 : 0;
    }

    #endregion
}

