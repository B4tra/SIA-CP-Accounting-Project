using Microsoft.EntityFrameworkCore;
using SIA.Domain.Common;
using SIA.Domain.Entities.Accounting;
using SIA.Domain.Entities.Inventory;
using SIA.Domain.Entities.Payments;
using SIA.Domain.Entities.Purchasing;
using SIA.Domain.Entities.Sales;
using SIA.Domain.Entities.System;
using SIA.Infrastructure.Identity;

namespace SIA.Tests.Infrastructure;

public class AppDbContextTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Add_MengisiCreatedAt_DanUpdatedAtNull()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        await using var context = _db.CreateContext();

        var customer = new Customer { Name = "Budi" };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        Assert.True(customer.Id > 0);
        Assert.True(customer.CreatedAt >= before);
        Assert.Null(customer.UpdatedAt);
        Assert.False(customer.IsDeleted);
    }

    [Fact]
    public async Task Update_MengisiUpdatedAt_TanpaMengubahCreatedAt()
    {
        int id;
        DateTime createdAt;
        await using (var context = _db.CreateContext())
        {
            var customer = new Customer { Name = "Budi" };
            context.Customers.Add(customer);
            await context.SaveChangesAsync();
            id = customer.Id;
            createdAt = customer.CreatedAt;
        }

        await using (var context = _db.CreateContext())
        {
            var customer = await context.Customers.SingleAsync(c => c.Id == id);
            customer.Name = "Budi Santoso";
            customer.CreatedAt = DateTime.UtcNow.AddYears(-5); // harus diabaikan
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var customer = await context.Customers.SingleAsync(c => c.Id == id);
            Assert.Equal("Budi Santoso", customer.Name);
            Assert.NotNull(customer.UpdatedAt);
            Assert.Equal(createdAt, customer.CreatedAt);
        }
    }

    [Fact]
    public async Task Remove_MenjadiSoftDelete_BarisTetapAda()
    {
        int id;
        await using (var context = _db.CreateContext())
        {
            var customer = new Customer { Name = "Budi" };
            context.Customers.Add(customer);
            await context.SaveChangesAsync();
            id = customer.Id;

            context.Customers.Remove(customer);
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var raw = await context.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == id);
            Assert.True(raw.IsDeleted);
            Assert.NotNull(raw.DeletedAt);
        }
    }

    [Fact]
    public async Task GlobalQueryFilter_MenyembunyikanDataTerhapus()
    {
        await using (var context = _db.CreateContext())
        {
            context.Customers.AddRange(new Customer { Name = "Aktif" }, new Customer { Name = "Dihapus" });
            await context.SaveChangesAsync();

            var deleted = await context.Customers.SingleAsync(c => c.Name == "Dihapus");
            context.Customers.Remove(deleted);
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var visible = await context.Customers.ToListAsync();
            Assert.Single(visible);
            Assert.Equal("Aktif", visible[0].Name);
            Assert.Equal(2, await context.Customers.IgnoreQueryFilters().CountAsync());
        }
    }

    [Fact]
    public void Model_SemuaBaseEntityMemilikiQueryFilter()
    {
        using var context = _db.CreateContext();

        var baseEntityTypes = context.Model.GetEntityTypes()
            .Where(t => typeof(BaseEntity).IsAssignableFrom(t.ClrType))
            .ToList();

        Assert.Equal(16, baseEntityTypes.Count);
        Assert.All(baseEntityTypes, t => Assert.NotNull(t.GetQueryFilter()));
    }

    [Fact]
    public void Model_Memuat19Entity()
    {
        using var context = _db.CreateContext();
        var expected = new[]
        {
            typeof(ApplicationUser), typeof(Account), typeof(JournalEntry), typeof(JournalEntryLine),
            typeof(Product), typeof(StockCard), typeof(Customer), typeof(Sale), typeof(SaleDetail),
            typeof(SaleReturn), typeof(SaleReturnDetail), typeof(Supplier), typeof(PurchaseOrder),
            typeof(PurchaseOrderDetail), typeof(PurchaseReturn), typeof(PurchaseReturnDetail),
            typeof(Payment), typeof(AuditLog), typeof(TransactionNumberConfig)
        };

        Assert.All(expected, type => Assert.NotNull(context.Model.FindEntityType(type)));
    }

    [Fact]
    public void Model_PaymentTanpaForeignKey_DenganIndexReferensi()
    {
        using var context = _db.CreateContext();
        var payment = context.Model.FindEntityType(typeof(Payment))!;

        Assert.Empty(payment.GetForeignKeys());
        Assert.Contains(payment.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Payment.ReferenceType), nameof(Payment.ReferenceId) }));
    }

    [Fact]
    public void Model_UangMemakaiPresisi18Koma2()
    {
        using var context = _db.CreateContext();
        var decimalProps = context.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(decimal))
            .ToList();

        Assert.NotEmpty(decimalProps);
        Assert.All(decimalProps, p =>
        {
            Assert.Equal(18, p.GetPrecision());
            Assert.Equal(2, p.GetScale());
        });
    }
}
