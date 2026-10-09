using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.Purchasing;
using SIA.Infrastructure.Identity;

namespace SIA.Infrastructure.Data.Configurations;

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("PurchaseOrders");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.PONumber)
            .IsRequired()
            .HasMaxLength(30);

        builder.HasIndex(p => p.PONumber)
            .IsUnique();

        builder.Property(p => p.TotalAmount)
            .HasPrecision(18, 2);

        builder.Property(p => p.PaidAmount)
            .HasPrecision(18, 2);

        builder.Property(p => p.RemainingAmount)
            .HasPrecision(18, 2);

        builder.Property(p => p.Notes)
            .HasMaxLength(500);

        builder.Property(p => p.UserId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(p => p.ApprovedByUserId)
            .HasMaxLength(255);

        builder.HasOne(p => p.Supplier)
            .WithMany(s => s.PurchaseOrders)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany(u => u.CreatedPurchaseOrders)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany(u => u.ApprovedPurchaseOrders)
            .HasForeignKey(p => p.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Details)
            .WithOne(d => d.PurchaseOrder)
            .HasForeignKey(d => d.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Returns)
            .WithOne(r => r.PurchaseOrder)
            .HasForeignKey(r => r.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
