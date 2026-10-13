using Microsoft.EntityFrameworkCore;
using SIA.Domain.Entities.Sales;
using SIA.Infrastructure.Repositories;

namespace SIA.Tests.Infrastructure;

public class UnitOfWorkTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Repository_DiCachePerTipe()
    {
        await using var context = _db.CreateContext();
        await using var uow = new UnitOfWork(context);

        Assert.Same(uow.Repository<Customer>(), uow.Repository<Customer>());
    }

    [Fact]
    public async Task SaveChangesAsync_MenyimpanData()
    {
        await using (var context = _db.CreateContext())
        await using (var uow = new UnitOfWork(context))
        {
            await uow.Repository<Customer>().AddAsync(new Customer { Name = "Budi" });
            Assert.True(await uow.SaveChangesAsync() > 0);
        }

        await using var verify = _db.CreateContext();
        Assert.Equal(1, await verify.Customers.CountAsync());
    }

    [Fact]
    public async Task Commit_MenyimpanPerubahanDalamTransaksi()
    {
        await using (var context = _db.CreateContext())
        await using (var uow = new UnitOfWork(context))
        {
            await uow.BeginTransactionAsync();
            await uow.Repository<Customer>().AddAsync(new Customer { Name = "Budi" });
            await uow.SaveChangesAsync();
            await uow.CommitTransactionAsync();
        }

        await using var verify = _db.CreateContext();
        Assert.Equal(1, await verify.Customers.CountAsync());
        Assert.Equal(1, await verify.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Rollback_MembatalkanPerubahan_TermasukAuditLog()
    {
        await using (var context = _db.CreateContext())
        await using (var uow = new UnitOfWork(context))
        {
            await uow.BeginTransactionAsync();
            await uow.Repository<Customer>().AddAsync(new Customer { Name = "Budi" });
            await uow.SaveChangesAsync();
            await uow.RollbackTransactionAsync();
        }

        await using var verify = _db.CreateContext();
        Assert.Equal(0, await verify.Customers.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await verify.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Commit_TanpaTransaksi_MelemparException()
    {
        await using var context = _db.CreateContext();
        await using var uow = new UnitOfWork(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.CommitTransactionAsync());
    }

    [Fact]
    public async Task BeginTransaction_DuaKali_MelemparException()
    {
        await using var context = _db.CreateContext();
        await using var uow = new UnitOfWork(context);

        await uow.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.BeginTransactionAsync());
        await uow.RollbackTransactionAsync();
    }

    [Fact]
    public async Task Rollback_TanpaTransaksi_TidakMelemparException()
    {
        await using var context = _db.CreateContext();
        await using var uow = new UnitOfWork(context);

        await uow.RollbackTransactionAsync();
    }
}
