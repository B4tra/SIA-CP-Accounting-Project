using Microsoft.EntityFrameworkCore;
using SIA.Domain.Entities.Sales;
using SIA.Infrastructure.Repositories;

namespace SIA.Tests.Infrastructure;

public class RepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task AddAsync_DanGetByIdAsync_MengembalikanEntity()
    {
        await using var context = _db.CreateContext();
        var repo = new Repository<Customer>(context);

        var customer = new Customer { Name = "Budi" };
        await repo.AddAsync(customer);
        await context.SaveChangesAsync();

        var found = await repo.GetByIdAsync(customer.Id);
        Assert.NotNull(found);
        Assert.Equal("Budi", found!.Name);
    }

    [Fact]
    public async Task GetByIdAsync_IdTidakAda_MengembalikanNull()
    {
        await using var context = _db.CreateContext();
        var repo = new Repository<Customer>(context);

        Assert.Null(await repo.GetByIdAsync(999));
    }

    [Fact]
    public async Task Delete_SoftDelete_GetByIdDanGetAllTidakMengembalikan()
    {
        await using var context = _db.CreateContext();
        var repo = new Repository<Customer>(context);

        var customer = new Customer { Name = "Budi" };
        await repo.AddAsync(customer);
        await repo.AddAsync(new Customer { Name = "Ani" });
        await context.SaveChangesAsync();

        repo.Delete(customer);
        await context.SaveChangesAsync();

        // Entity masih dilacak oleh context yang sama — tetap harus dianggap tidak ada
        Assert.Null(await repo.GetByIdAsync(customer.Id));
        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("Ani", all[0].Name);
        Assert.Equal(2, await context.Customers.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task FindAsync_MemfilterDenganPredicate()
    {
        await using var context = _db.CreateContext();
        var repo = new Repository<Customer>(context);

        await repo.AddAsync(new Customer { Name = "Budi", Phone = "0811" });
        await repo.AddAsync(new Customer { Name = "Ani" });
        await context.SaveChangesAsync();

        var result = await repo.FindAsync(c => c.Phone != null);
        Assert.Single(result);
        Assert.Equal("Budi", result[0].Name);
    }

    [Fact]
    public async Task Update_MenyimpanPerubahan()
    {
        int id;
        await using (var context = _db.CreateContext())
        {
            var repo = new Repository<Customer>(context);
            var customer = new Customer { Name = "Budi" };
            await repo.AddAsync(customer);
            await context.SaveChangesAsync();
            id = customer.Id;
        }

        await using (var context = _db.CreateContext())
        {
            var repo = new Repository<Customer>(context);
            var customer = (await repo.GetByIdAsync(id))!;
            customer.Address = "Jl. Merdeka 1";
            repo.Update(customer);
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            Assert.Equal("Jl. Merdeka 1", (await context.Customers.SingleAsync(c => c.Id == id)).Address);
        }
    }
}
