using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SIA.Application.Common.Constants;
using SIA.Domain.Entities.Accounting;
using SIA.Domain.Entities.System;
using SIA.Domain.Enums;

namespace SIA.Infrastructure.Data.Seed;

/// <summary>
/// Seeding data master wajib: konfigurasi nomor transaksi dan Chart of Accounts standar toko aki.
/// Idempoten: hanya menambah data yang belum ada (cek per <c>TransactionType</c> / per <c>Code</c>).
/// </summary>
public static class MasterDataSeeder
{
    public const string DefaultFormat = "{Prefix}-{Year}-{Seq:0000}";

    public sealed record TransactionNumberSeed(string TransactionType, string Prefix);

    public sealed record AccountSeed(string Code, string Name, AccountCategory Category, NormalBalance NormalBalance);

    public static readonly IReadOnlyList<TransactionNumberSeed> TransactionNumberConfigs = new[]
    {
        new TransactionNumberSeed(TransactionTypes.Sale, "INV"),
        new TransactionNumberSeed(TransactionTypes.PurchaseOrder, "PO"),
        new TransactionNumberSeed(TransactionTypes.JournalEntry, "JRN"),
        new TransactionNumberSeed(TransactionTypes.Payment, "PAY"),
        new TransactionNumberSeed(TransactionTypes.SaleReturn, "SRT"),
        new TransactionNumberSeed(TransactionTypes.PurchaseReturn, "PRT"),
    };

    public static readonly IReadOnlyList<AccountSeed> ChartOfAccounts = new[]
    {
        new AccountSeed("1-1001", "Kas", AccountCategory.AsetLancar, NormalBalance.Debit),
        new AccountSeed("1-1002", "Bank", AccountCategory.AsetLancar, NormalBalance.Debit),
        new AccountSeed("1-1003", "Piutang Usaha", AccountCategory.AsetLancar, NormalBalance.Debit),
        new AccountSeed("1-1004", "Persediaan Barang Dagang (Aki)", AccountCategory.AsetLancar, NormalBalance.Debit),
        new AccountSeed("1-1005", "Perlengkapan Toko", AccountCategory.AsetLancar, NormalBalance.Debit),
        new AccountSeed("1-2001", "Peralatan Toko", AccountCategory.AsetTetap, NormalBalance.Debit),
        new AccountSeed("1-2002", "Akumulasi Penyusutan Peralatan", AccountCategory.AsetTetap, NormalBalance.Credit),
        new AccountSeed("2-1001", "Utang Usaha", AccountCategory.LiabilitasJangkaPendek, NormalBalance.Credit),
        new AccountSeed("2-2001", "Utang Bank", AccountCategory.LiabilitasJangkaPanjang, NormalBalance.Credit),
        new AccountSeed("3-1001", "Modal Pemilik", AccountCategory.Ekuitas, NormalBalance.Credit),
        new AccountSeed("3-1002", "Prive Pemilik", AccountCategory.Ekuitas, NormalBalance.Debit),
        new AccountSeed("4-1001", "Penjualan", AccountCategory.Pendapatan, NormalBalance.Credit),
        new AccountSeed("4-1002", "Retur Penjualan", AccountCategory.Pendapatan, NormalBalance.Debit),
        new AccountSeed("4-2001", "Pendapatan Lain-lain", AccountCategory.PendapatanLainLain, NormalBalance.Credit),
        new AccountSeed("5-1001", "Harga Pokok Penjualan", AccountCategory.BebanPokokPenjualan, NormalBalance.Debit),
        new AccountSeed("5-1002", "Retur Pembelian", AccountCategory.BebanPokokPenjualan, NormalBalance.Credit),
        new AccountSeed("6-1001", "Beban Gaji", AccountCategory.BebanOperasional, NormalBalance.Debit),
        new AccountSeed("6-1002", "Beban Sewa", AccountCategory.BebanOperasional, NormalBalance.Debit),
        new AccountSeed("6-1003", "Beban Listrik, Air & Telepon", AccountCategory.BebanOperasional, NormalBalance.Debit),
        new AccountSeed("6-1004", "Beban Penyusutan", AccountCategory.BebanOperasional, NormalBalance.Debit),
        new AccountSeed("7-1001", "Beban Lain-lain", AccountCategory.BebanLainLain, NormalBalance.Debit),
    };

    /// <summary>
    /// Menjalankan semua seeding data master memakai <see cref="AppDbContext"/> dari container DI
    /// (dipanggil layer Web saat startup tanpa mengakses DbContext secara langsung).
    /// </summary>
    public static Task SeedAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken = default) =>
        SeedAsync(services.GetRequiredService<AppDbContext>(), logger, cancellationToken);

    /// <summary>Menjalankan semua seeding data master.</summary>
    public static async Task SeedAsync(AppDbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        await SeedTransactionNumberConfigsAsync(context, DateTime.Today.Year, logger, cancellationToken);
        await SeedChartOfAccountsAsync(context, logger, cancellationToken);
    }

    /// <summary>Menambah konfigurasi nomor transaksi yang belum ada (sequence 0, tahun = <paramref name="year"/>).</summary>
    public static async Task SeedTransactionNumberConfigsAsync(AppDbContext context, int year, ILogger logger, CancellationToken cancellationToken = default)
    {
        var existing = await context.TransactionNumberConfigs
            .Select(c => c.TransactionType)
            .ToListAsync(cancellationToken);

        var missing = TransactionNumberConfigs.Where(s => !existing.Contains(s.TransactionType)).ToList();
        if (missing.Count == 0) return;

        foreach (var seed in missing)
        {
            context.TransactionNumberConfigs.Add(new TransactionNumberConfig
            {
                TransactionType = seed.TransactionType,
                Prefix = seed.Prefix,
                Format = DefaultFormat,
                CurrentSequence = 0,
                Year = year,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("{Count} konfigurasi nomor transaksi ditambahkan.", missing.Count);
    }

    /// <summary>Menambah akun standar yang belum ada (termasuk mempertimbangkan akun yang di-soft-delete).</summary>
    public static async Task SeedChartOfAccountsAsync(AppDbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: kode akun terhapus tetap terkunci oleh unique index
        var existing = await context.Accounts.IgnoreQueryFilters()
            .Select(a => a.Code)
            .ToListAsync(cancellationToken);

        var missing = ChartOfAccounts.Where(s => !existing.Contains(s.Code)).ToList();
        if (missing.Count == 0) return;

        foreach (var seed in missing)
        {
            context.Accounts.Add(new Account
            {
                Code = seed.Code,
                Name = seed.Name,
                Category = seed.Category,
                NormalBalance = seed.NormalBalance,
                Balance = 0m,
                IsActive = true,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("{Count} akun standar ditambahkan ke Chart of Accounts.", missing.Count);
    }
}
