using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SIA.Domain.Common;
using SIA.Domain.Entities.Accounting;
using SIA.Domain.Entities.Inventory;
using SIA.Domain.Entities.Payments;
using SIA.Domain.Entities.Purchasing;
using SIA.Domain.Entities.Sales;
using SIA.Domain.Entities.System;
using SIA.Infrastructure.Identity;

namespace SIA.Infrastructure.Data;

/// <summary>
/// DbContext utama. Menangani timestamp otomatis, soft delete, dan global query filter
/// untuk semua entity turunan <see cref="BaseEntity"/>. AuditLog ditulis oleh
/// <c>AuditSaveChangesInterceptor</c>.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // Accounting
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();

    // Sales
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleDetail> SaleDetails => Set<SaleDetail>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<SaleReturnDetail> SaleReturnDetails => Set<SaleReturnDetail>();

    // Purchasing
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderDetail> PurchaseOrderDetails => Set<PurchaseOrderDetail>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnDetail> PurchaseReturnDetails => Set<PurchaseReturnDetail>();

    // Inventory
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockCard> StockCards => Set<StockCard>();

    // Payments
    public DbSet<Payment> Payments => Set<Payment>();

    // System
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TransactionNumberConfig> TransactionNumberConfigs => Set<TransactionNumberConfig>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Soft delete: global query filter untuk semua BaseEntity
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var property = Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
                var filter = Expression.Lambda(Expression.Not(property), parameter);
                builder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyBaseEntityRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyBaseEntityRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Mengisi CreatedAt/UpdatedAt dan mengubah penghapusan fisik menjadi soft delete.
    /// </summary>
    private void ApplyBaseEntityRules()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = null;
                    entry.Entity.IsDeleted = false;
                    entry.Entity.DeletedAt = null;
                    break;

                case EntityState.Modified:
                    entry.Property(e => e.CreatedAt).IsModified = false;
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Property(e => e.CreatedAt).IsModified = false;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }
}
