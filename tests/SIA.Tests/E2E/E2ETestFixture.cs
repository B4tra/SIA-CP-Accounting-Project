using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.Accounting;
using SIA.Application.Services.Accounting;
using SIA.Application.Services.Reporting;
using SIA.Domain.Entities.Accounting;
using SIA.Infrastructure.Data;
using SIA.Infrastructure.Data.Seed;
using SIA.Infrastructure.Identity;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;
using SIA.Tests.Services;

namespace SIA.Tests.E2E;

/// <summary>
/// Fixture bersama untuk E2E & Scenario tests modul Jurnal Umum & Laporan Keuangan.
/// Menyediakan SQLite database terisolasi, data master akun standar Toko Aki,
/// dan pabrik instansiasi service untuk berbagai peran pengguna.
/// </summary>
public sealed class E2ETestFixture : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private Dictionary<string, int>? _accountCodeMap;

    public AppDbContext CreateContext() => _db.CreateContext();

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// Menyiapkan database dengan konfigurasi penomoran transaksi dan COA Toko Aki.
    /// </summary>
    public async Task InitializeAsync()
    {
        await using var context = _db.CreateContext();

        // 1. Seed nomor transaksi dan 21 akun standar
        await MasterDataSeeder.SeedAsync(context, NullLogger.Instance);

        // 2. Tambahkan user uji jika belum ada
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

        // 3. Cache pemetaan kode akun ke Id
        var accounts = await context.Accounts.AsNoTracking().ToListAsync();
        _accountCodeMap = accounts.ToDictionary(a => a.Code, a => a.Id);
    }

    /// <summary>
    /// Mengambil Id akun berdasarkan kode standar (contoh: "1-1001" untuk Kas).
    /// </summary>
    public int GetAccountId(string code)
    {
        if (_accountCodeMap == null)
            throw new InvalidOperationException("E2ETestFixture belum diinisialisasi. Panggil InitializeAsync() terlebih dahulu.");

        if (!_accountCodeMap.TryGetValue(code, out var id))
            throw new KeyNotFoundException($"Kode akun '{code}' tidak ditemukan di daftar akun master.");

        return id;
    }

    /// <summary>
    /// Membuat instance IJournalService menggunakan peran tertentu.
    /// Menggunakan dynamic assembly resolution untuk mendukung opaque-box testing
    /// yang toleran terhadap siklus kompilasi paralel.
    /// </summary>
    public IJournalService CreateJournalService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);
        var txService = new TransactionNumberService(context, user);

        var infraAssembly = typeof(AccountService).Assembly;
        var journalType = infraAssembly.GetType("SIA.Infrastructure.Services.JournalService");
        if (journalType == null)
            throw new InvalidOperationException("Kelas 'SIA.Infrastructure.Services.JournalService' tidak ditemukan di SIA.Infrastructure.");

        return (IJournalService)Activator.CreateInstance(journalType, context, user, txService)!;
    }

    /// <summary>
    /// Membuat instance IFinancialReportService menggunakan peran tertentu.
    /// </summary>
    public IFinancialReportService CreateFinancialReportService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);

        var infraAssembly = typeof(AccountService).Assembly;
        var reportType = infraAssembly.GetType("SIA.Infrastructure.Services.FinancialReportService");
        if (reportType == null)
            throw new InvalidOperationException("Kelas 'SIA.Infrastructure.Services.FinancialReportService' tidak ditemukan di SIA.Infrastructure.");

        return (IFinancialReportService)Activator.CreateInstance(reportType, context, user)!;
    }

    /// <summary>
    /// Membuat instance AccountService menggunakan peran tertentu.
    /// </summary>
    public AccountService CreateAccountService(params string[] roles)
    {
        return new AccountService(_db.CreateContext(), TestUser.InRole(roles));
    }

    /// <summary>
    /// Membaca saldo akun tersimpan langsung dari database.
    /// </summary>
    public async Task<decimal> GetAccountBalanceAsync(int accountId)
    {
        await using var context = _db.CreateContext();
        var account = await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
        return account.Balance;
    }

    /// <summary>
    /// Helper untuk menyusun DTO jurnal 2 baris berimbang.
    /// </summary>
    public static CreateJournalEntryDto Create2LineJournal(int debitAccId, int creditAccId, decimal amount, DateTime? date = null, string desc = "Jurnal 2 Baris")
    {
        return new CreateJournalEntryDto
        {
            TransactionDate = date ?? DateTime.Today,
            Description = desc,
            Lines = new List<CreateJournalEntryLineDto>
            {
                new() { AccountId = debitAccId, Debit = amount, Credit = 0m },
                new() { AccountId = creditAccId, Debit = 0m, Credit = amount }
            }
        };
    }

    /// <summary>
    /// Helper untuk menyusun DTO jurnal multi-baris.
    /// </summary>
    public static CreateJournalEntryDto CreateMultiLineJournal(DateTime date, string desc, params (int accountId, decimal debit, decimal credit)[] lines)
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
}
