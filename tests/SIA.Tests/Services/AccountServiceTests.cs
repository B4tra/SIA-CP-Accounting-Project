using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.Accounting;
using SIA.Domain.Entities.Accounting;
using SIA.Domain.Enums;
using SIA.Infrastructure.Identity;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;

namespace SIA.Tests.Services;

public class AccountServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private AccountService CreateService(params string[] roles) => new(_db.CreateContext(), TestUser.InRole(roles));

    private static CreateAccountDto NewAccount(string code = "1-1001", string name = "Kas",
        AccountCategory category = AccountCategory.AsetLancar, NormalBalance normal = NormalBalance.Debit,
        int? parentId = null) => new()
        {
            Code = code,
            Name = name,
            Category = category,
            NormalBalance = normal,
            ParentAccountId = parentId,
        };

    private async Task<int> CreateAsAdminAsync(CreateAccountDto dto)
    {
        var result = await CreateService(Roles.Admin).CreateAsync(dto);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!.Id;
    }

    /// <summary>Menambahkan jurnal (user + header + baris) langsung ke database untuk keperluan test.</summary>
    private async Task AddJournalAsync(string number, params (int AccountId, decimal Debit, decimal Credit)[] lines)
    {
        await using var context = _db.CreateContext();
        if (!await context.Users.AnyAsync(u => u.Id == "user-test"))
        {
            context.Users.Add(new ApplicationUser { Id = "user-test", UserName = "tester@sia.local", Email = "tester@sia.local" });
        }

        var journal = new JournalEntry
        {
            JournalNumber = number,
            TransactionDate = new DateTime(2026, 1, 10),
            Description = "Jurnal uji",
            UserId = "user-test",
        };
        foreach (var (accountId, debit, credit) in lines)
            journal.Lines.Add(new JournalEntryLine { AccountId = accountId, Debit = debit, Credit = credit });

        context.JournalEntries.Add(journal);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_Admin_BerhasilDenganSaldoNol()
    {
        var result = await CreateService(Roles.Admin).CreateAsync(NewAccount(code: " 1-1001 ", name: " Kas "));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("1-1001", result.Value!.Code);
        Assert.Equal("Kas", result.Value.Name);
        Assert.Equal(0m, result.Value.Balance);
        Assert.True(result.Value.IsActive);
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Viewer)]
    public async Task Create_NonAdmin_Ditolak(string role)
    {
        var result = await CreateService(role).CreateAsync(NewAccount());

        Assert.True(result.IsFailure);
        await using var context = _db.CreateContext();
        Assert.False(await context.Accounts.AnyAsync());
    }

    [Fact]
    public async Task Create_KodeDuplikat_Ditolak()
    {
        await CreateAsAdminAsync(NewAccount());

        var result = await CreateService(Roles.Admin).CreateAsync(NewAccount(name: "Kas Kecil"));

        Assert.True(result.IsFailure);
        Assert.Contains("sudah digunakan", result.Error);
    }

    [Fact]
    public async Task Create_KodeMilikAkunTerhapus_TetapDitolak()
    {
        var id = await CreateAsAdminAsync(NewAccount());
        Assert.True((await CreateService(Roles.Admin).DeleteAsync(id)).IsSuccess);

        var result = await CreateService(Roles.Admin).CreateAsync(NewAccount());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Create_ParentTidakAda_Ditolak()
    {
        var result = await CreateService(Roles.Admin).CreateAsync(NewAccount(parentId: 999));

        Assert.True(result.IsFailure);
        Assert.Contains("Akun induk tidak ditemukan.", result.Errors);
    }

    [Fact]
    public async Task Update_Admin_MengubahDataTanpaMengubahSaldo()
    {
        var id = await CreateAsAdminAsync(NewAccount());
        await using (var context = _db.CreateContext())
        {
            var account = await context.Accounts.SingleAsync(a => a.Id == id);
            account.Balance = 500_000m;
            await context.SaveChangesAsync();
        }

        var result = await CreateService(Roles.Admin).UpdateAsync(new UpdateAccountDto
        {
            Id = id, Code = "1-1001", Name = "Kas Toko", Category = AccountCategory.AsetLancar,
            NormalBalance = NormalBalance.Debit, IsActive = false,
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Kas Toko", result.Value!.Name);
        Assert.False(result.Value.IsActive);
        Assert.Equal(500_000m, result.Value.Balance);
    }

    [Fact]
    public async Task Update_ParentDirinyaSendiri_Ditolak()
    {
        var id = await CreateAsAdminAsync(NewAccount());

        var result = await CreateService(Roles.Admin).UpdateAsync(new UpdateAccountDto
        {
            Id = id, Code = "1-1001", Name = "Kas", Category = AccountCategory.AsetLancar,
            NormalBalance = NormalBalance.Debit, ParentAccountId = id,
        });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Update_ParentMerupakanSubAkun_Ditolak()
    {
        var parentId = await CreateAsAdminAsync(NewAccount(code: "1-0000", name: "Aset"));
        var childId = await CreateAsAdminAsync(NewAccount(parentId: parentId));

        var result = await CreateService(Roles.Admin).UpdateAsync(new UpdateAccountDto
        {
            Id = parentId, Code = "1-0000", Name = "Aset", Category = AccountCategory.AsetLancar,
            NormalBalance = NormalBalance.Debit, ParentAccountId = childId,
        });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Update_NonAdmin_Ditolak()
    {
        var id = await CreateAsAdminAsync(NewAccount());

        var result = await CreateService(Roles.Viewer).UpdateAsync(new UpdateAccountDto
        {
            Id = id, Code = "1-1001", Name = "Diubah", Category = AccountCategory.AsetLancar, NormalBalance = NormalBalance.Debit,
        });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Delete_Admin_SoftDelete()
    {
        var id = await CreateAsAdminAsync(NewAccount());

        var result = await CreateService(Roles.Admin).DeleteAsync(id);

        Assert.True(result.IsSuccess, result.Error);
        await using var context = _db.CreateContext();
        Assert.False(await context.Accounts.AnyAsync());
        Assert.True((await context.Accounts.IgnoreQueryFilters().SingleAsync()).IsDeleted);
    }

    [Fact]
    public async Task Delete_NonAdmin_Ditolak()
    {
        var id = await CreateAsAdminAsync(NewAccount());

        Assert.True((await CreateService(Roles.Staff).DeleteAsync(id)).IsFailure);
    }

    [Fact]
    public async Task Delete_AkunSudahDipakaiJurnal_Ditolak()
    {
        var kas = await CreateAsAdminAsync(NewAccount());
        var modal = await CreateAsAdminAsync(NewAccount("3-1001", "Modal Pemilik", AccountCategory.Ekuitas, NormalBalance.Credit));
        await AddJournalAsync("JRN-2026-0001", (kas, 1_000_000m, 0m), (modal, 0m, 1_000_000m));

        var result = await CreateService(Roles.Admin).DeleteAsync(kas);

        Assert.True(result.IsFailure);
        Assert.Contains("jurnal", result.Error);
    }

    [Fact]
    public async Task Delete_AkunPunyaSubAkun_Ditolak()
    {
        var parentId = await CreateAsAdminAsync(NewAccount(code: "1-0000", name: "Aset"));
        await CreateAsAdminAsync(NewAccount(parentId: parentId));

        var result = await CreateService(Roles.Admin).DeleteAsync(parentId);

        Assert.True(result.IsFailure);
        Assert.Contains("sub-akun", result.Error);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Viewer)]
    public async Task GetAll_SemuaRole_Boleh_DanMenyaringNonaktif(string role)
    {
        await CreateAsAdminAsync(NewAccount());
        var inactiveId = await CreateAsAdminAsync(NewAccount("1-1002", "Bank"));
        await CreateService(Roles.Admin).UpdateAsync(new UpdateAccountDto
        {
            Id = inactiveId, Code = "1-1002", Name = "Bank", Category = AccountCategory.AsetLancar,
            NormalBalance = NormalBalance.Debit, IsActive = false,
        });

        var active = await CreateService(role).GetAllAsync();
        var all = await CreateService(role).GetAllAsync(includeInactive: true);

        Assert.True(active.IsSuccess);
        Assert.Single(active.Value!);
        Assert.Equal(2, all.Value!.Count);
    }

    [Fact]
    public async Task GetAll_TanpaRole_Ditolak()
    {
        Assert.True((await CreateService().GetAllAsync()).IsFailure);
    }

    [Fact]
    public async Task Recalculate_SaldoNormalDebitDanKredit()
    {
        var kas = await CreateAsAdminAsync(NewAccount());
        var modal = await CreateAsAdminAsync(NewAccount("3-1001", "Modal Pemilik", AccountCategory.Ekuitas, NormalBalance.Credit));
        var beban = await CreateAsAdminAsync(NewAccount("6-1002", "Beban Sewa", AccountCategory.BebanOperasional, NormalBalance.Debit));
        var unused = await CreateAsAdminAsync(NewAccount("1-1002", "Bank"));

        await AddJournalAsync("JRN-2026-0001", (kas, 10_000_000m, 0m), (modal, 0m, 10_000_000m));
        await AddJournalAsync("JRN-2026-0002", (beban, 1_500_000m, 0m), (kas, 0m, 1_500_000m));

        // Saldo tersimpan sengaja dibuat salah
        await using (var context = _db.CreateContext())
        {
            await context.Accounts.ForEachAsync(a => a.Balance = 123m);
            await context.SaveChangesAsync();
        }

        var result = await CreateService(Roles.Admin).RecalculateBalancesAsync();

        Assert.True(result.IsSuccess, result.Error);
        await using var verify = _db.CreateContext();
        var balances = await verify.Accounts.ToDictionaryAsync(a => a.Id, a => a.Balance);
        Assert.Equal(8_500_000m, balances[kas]);
        Assert.Equal(10_000_000m, balances[modal]);
        Assert.Equal(1_500_000m, balances[beban]);
        Assert.Equal(0m, balances[unused]);
    }

    [Fact]
    public async Task Recalculate_MengabaikanJurnalTerhapus()
    {
        var kas = await CreateAsAdminAsync(NewAccount());
        var modal = await CreateAsAdminAsync(NewAccount("3-1001", "Modal Pemilik", AccountCategory.Ekuitas, NormalBalance.Credit));
        await AddJournalAsync("JRN-2026-0001", (kas, 2_000_000m, 0m), (modal, 0m, 2_000_000m));
        await AddJournalAsync("JRN-2026-0002", (kas, 700_000m, 0m), (modal, 0m, 700_000m));

        await using (var context = _db.CreateContext())
        {
            var journal = await context.JournalEntries.SingleAsync(j => j.JournalNumber == "JRN-2026-0002");
            context.JournalEntries.Remove(journal); // soft delete header saja
            await context.SaveChangesAsync();
        }

        await CreateService(Roles.Admin).RecalculateBalancesAsync();

        await using var verify = _db.CreateContext();
        Assert.Equal(2_000_000m, (await verify.Accounts.SingleAsync(a => a.Id == kas)).Balance);
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Viewer)]
    public async Task Recalculate_NonAdmin_Ditolak(string role)
    {
        Assert.True((await CreateService(role).RecalculateBalancesAsync()).IsFailure);
    }
}
