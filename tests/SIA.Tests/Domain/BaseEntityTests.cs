using SIA.Domain.Common;
using SIA.Domain.Entities.Accounting;
using SIA.Domain.Entities.Inventory;
using SIA.Domain.Entities.Payments;
using SIA.Domain.Entities.Purchasing;
using SIA.Domain.Entities.Sales;
using SIA.Domain.Entities.System;

namespace SIA.Tests.Domain;

public class BaseEntityTests
{
    [Fact]
    public void NewEntity_HasDefaultSoftDeleteState()
    {
        var entity = new Product();

        Assert.Equal(0, entity.Id);
        Assert.False(entity.IsDeleted);
        Assert.Null(entity.DeletedAt);
        Assert.Null(entity.UpdatedAt);
    }

    [Fact]
    public void BaseEntity_IdIsInt()
    {
        var idProperty = typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id));

        Assert.NotNull(idProperty);
        Assert.Equal(typeof(int), idProperty!.PropertyType);
    }

    [Theory]
    [InlineData(typeof(Account))]
    [InlineData(typeof(JournalEntry))]
    [InlineData(typeof(JournalEntryLine))]
    [InlineData(typeof(Customer))]
    [InlineData(typeof(Sale))]
    [InlineData(typeof(SaleDetail))]
    [InlineData(typeof(SaleReturn))]
    [InlineData(typeof(SaleReturnDetail))]
    [InlineData(typeof(Supplier))]
    [InlineData(typeof(PurchaseOrder))]
    [InlineData(typeof(PurchaseOrderDetail))]
    [InlineData(typeof(PurchaseReturn))]
    [InlineData(typeof(PurchaseReturnDetail))]
    [InlineData(typeof(Product))]
    [InlineData(typeof(StockCard))]
    [InlineData(typeof(Payment))]
    public void BusinessEntities_InheritBaseEntity(Type entityType)
    {
        Assert.True(typeof(BaseEntity).IsAssignableFrom(entityType));
    }

    [Theory]
    [InlineData(typeof(AuditLog))]
    [InlineData(typeof(TransactionNumberConfig))]
    public void SystemEntities_DoNotInheritBaseEntity(Type entityType)
    {
        Assert.False(typeof(BaseEntity).IsAssignableFrom(entityType));
    }

    [Fact]
    public void AuditLog_IdIsLong()
    {
        Assert.Equal(typeof(long), typeof(AuditLog).GetProperty(nameof(AuditLog.Id))!.PropertyType);
    }

    [Theory]
    [InlineData(typeof(Sale))]
    [InlineData(typeof(PurchaseOrder))]
    public void SaleAndPurchaseOrder_HaveNullableDueDate(Type entityType)
    {
        var dueDate = entityType.GetProperty("DueDate");

        Assert.NotNull(dueDate);
        Assert.Equal(typeof(DateTime?), dueDate!.PropertyType);
    }

    [Fact]
    public void NewAccount_IsActiveByDefault()
    {
        Assert.True(new Account().IsActive);
    }
}
