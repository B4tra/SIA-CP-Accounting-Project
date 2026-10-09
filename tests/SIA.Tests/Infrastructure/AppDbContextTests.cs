using Microsoft.EntityFrameworkCore;
using Moq;
using SIA.Application.Common.Interfaces;
using SIA.Domain.Entities.Inventory;
using SIA.Domain.Entities.Sales;
using SIA.Domain.Entities.System;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data;
using SIA.Infrastructure.Data.Interceptors;
using SIA.Infrastructure.Repositories;

namespace SIA.Tests.Infrastructure;

public class AppDbContextTests
{
    private static (AppDbContext Context, Mock<ICurrentUserService> UserMock) CreateInMemoryDbContext()
    {
        var userMock = new Mock<ICurrentUserService>();
        userMock.Setup(u => u.UserId).Returns("test-user-id");
        userMock.Setup(u => u.IpAddress).Returns("127.0.0.1");

        var interceptor = new AuditSaveChangesInterceptor(userMock.Object);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        var context = new AppDbContext(options);
        return (context, userMock);
    }

    [Fact]
    public async Task AddEntity_AutomaticallySetsCreatedAt_AndIsDeletedFalse()
    {
        var (context, _) = CreateInMemoryDbContext();

        var customer = new Customer
        {
            Name = "Toko Berkah Motor",
            Phone = "08123456789"
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        Assert.True(customer.Id > 0);
        Assert.False(customer.IsDeleted);
        Assert.Null(customer.DeletedAt);
        Assert.True(customer.CreatedAt <= DateTime.UtcNow);
        Assert.Null(customer.UpdatedAt);
    }

    [Fact]
    public async Task UpdateEntity_AutomaticallySetsUpdatedAt()
    {
        var (context, _) = CreateInMemoryDbContext();

        var product = new Product
        {
            Name = "Aki GS Astra 12V 45Ah",
            SellingPrice = 850000,
            PurchasePrice = 700000,
            Stock = 10,
            Brand = "GS Astra",
            BatteryType = BatteryType.Kering
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        var initialCreatedAt = product.CreatedAt;

        product.SellingPrice = 875000;
        await context.SaveChangesAsync();

        Assert.NotNull(product.UpdatedAt);
        Assert.Equal(initialCreatedAt, product.CreatedAt);
    }

    [Fact]
    public async Task DeleteEntity_PerformsSoftDelete_AndExcludesFromNormalQueries()
    {
        var (context, _) = CreateInMemoryDbContext();

        var customer = new Customer
        {
            Name = "Pelanggan A",
            Phone = "08111111111"
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        // Hapus entitas
        context.Customers.Remove(customer);
        await context.SaveChangesAsync();

        // Cek entitas di memory
        Assert.True(customer.IsDeleted);
        Assert.NotNull(customer.DeletedAt);

        // Query biasa harus mengabaikan entitas yang sudah soft delete (Global Query Filter)
        var queryNormal = await context.Customers.ToListAsync();
        Assert.Empty(queryNormal);

        // Query dengan IgnoreQueryFilters harus tetap menemukan entitas
        var queryAll = await context.Customers.IgnoreQueryFilters().ToListAsync();
        Assert.Single(queryAll);
        Assert.True(queryAll[0].IsDeleted);
    }

    [Fact]
    public async Task SaveChanges_WithAuditInterceptor_CreatesAuditLogs()
    {
        var (context, _) = CreateInMemoryDbContext();

        var customer = new Customer
        {
            Name = "Pelanggan Audit Test",
            Phone = "08222222222"
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        // Cek apakah AuditLog tercatat
        var auditLogs = await context.AuditLogs.ToListAsync();
        Assert.NotEmpty(auditLogs);
        Assert.Contains(auditLogs, a => a.EntityName == nameof(Customer) && a.Action == "Create");
    }

    [Fact]
    public async Task UnitOfWork_And_Repository_WorkCorrectly()
    {
        var (context, _) = CreateInMemoryDbContext();
        var uow = new UnitOfWork(context);

        var repo = uow.Repository<Customer>();

        var customer = new Customer
        {
            Name = "Pelanggan UoW",
            Phone = "08333333333"
        };

        await repo.AddAsync(customer);
        await uow.SaveChangesAsync();

        var fetched = await repo.GetByIdAsync(customer.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Pelanggan UoW", fetched!.Name);

        repo.Delete(fetched);
        await uow.SaveChangesAsync();

        var afterDelete = await repo.GetByIdAsync(customer.Id);
        Assert.Null(afterDelete);
    }
}
