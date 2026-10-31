using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SIA.Application.Common.Constants;
using SIA.Domain.Entities.Accounting;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data.Seed;
using SIA.Tests.Infrastructure;

namespace SIA.Tests.Services;

public class MasterDataSeederTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SeedTransactionNumberConfigs_MembuatEnamKonfigurasi()
    {
        await using (var context = _db.CreateContext())
        {
            await MasterDataSeeder.SeedTransactionNumberConfigsAsync(context, 2026, NullLogger.Instance);
        }

        await using var verify = _db.CreateContext();
        var configs = await verify.TransactionNumberConfigs.ToListAsync();
        Assert.Equal(6, configs.Count);
        Assert.Equal(TransactionTypes.All.OrderBy(t => t), configs.Select(c => c.TransactionType).OrderBy(t => t));
        Assert.All(configs, c =>
        {
            Assert.Equal("{Prefix}-{Year}-{Seq:0000}", c.Format);
            Assert.Equal(0, c.CurrentSequence);
            Assert.Equal(2026, c.Year);
        });
        Assert.Equal("INV", configs.Single(c => c.TransactionType == TransactionTypes.Sale).Prefix);
        Assert.Equal("PRT", configs.Single(c => c.TransactionType == TransactionTypes.PurchaseReturn).Prefix);
    }

    [Fact]
    public async Task SeedChartOfAccounts_MembuatSemuaAkunStandar()
    {
        await using (var context = _db.CreateContext())
        {
            await MasterDataSeeder.SeedChartOfAccountsAsync(context, NullLogger.Instance);
        }

        await using var verify = _db.CreateContext();
        var accounts = await verify.Accounts.ToListAsync();
        Assert.Equal(21, accounts.Count);
        Assert.Equal(MasterDataSeeder.ChartOfAccounts.Count, accounts.Count);
        Assert.All(accounts, a =>
        {
            Assert.Equal(0m, a.Balance);
            Assert.True(a.IsActive);
            Assert.Null(a.ParentAccountId);
        });

        var akumulasi = accounts.Single(a => a.Code == "1-2002");
        Assert.Equal(AccountCategory.AsetTetap, akumulasi.Category);
        Assert.Equal(NormalBalance.Credit, akumulasi.NormalBalance);
        Assert.Equal(NormalBalance.Debit, accounts.Single(a => a.Code == "3-1002").NormalBalance);
    }

    [Fact]
    public async Task Seed_DijalankanDuaKali_TidakDuplikat()
    {
        for (var i = 0; i < 2; i++)
        {
            await using var context = _db.CreateContext();
            await MasterDataSeeder.SeedAsync(context, NullLogger.Instance);
        }

        await using var verify = _db.CreateContext();
        Assert.Equal(6, await verify.TransactionNumberConfigs.CountAsync());
        Assert.Equal(21, await verify.Accounts.CountAsync());
    }

    [Fact]
    public async Task Seed_HanyaMenambahYangBelumAda_DanTidakMenimpaData()
    {
        await using (var context = _db.CreateContext())
        {
            context.Accounts.Add(new Account
            {
                Code = "1-1001", Name = "Kas Toko (diubah)", Category = AccountCategory.AsetLancar,
                NormalBalance = NormalBalance.Debit, Balance = 250_000m,
            });
            context.TransactionNumberConfigs.Add(new SIA.Domain.Entities.System.TransactionNumberConfig
            {
                TransactionType = TransactionTypes.Sale, Prefix = "FKT", Format = "{Prefix}{Seq}", CurrentSequence = 9, Year = 2026,
            });
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            await MasterDataSeeder.SeedAsync(context, NullLogger.Instance);
        }

        await using var verify = _db.CreateContext();
        var kas = await verify.Accounts.SingleAsync(a => a.Code == "1-1001");
        Assert.Equal("Kas Toko (diubah)", kas.Name);
        Assert.Equal(250_000m, kas.Balance);
        Assert.Equal(21, await verify.Accounts.CountAsync());

        var sale = await verify.TransactionNumberConfigs.SingleAsync(c => c.TransactionType == TransactionTypes.Sale);
        Assert.Equal("FKT", sale.Prefix);
        Assert.Equal(9, sale.CurrentSequence);
        Assert.Equal(6, await verify.TransactionNumberConfigs.CountAsync());
    }
}
