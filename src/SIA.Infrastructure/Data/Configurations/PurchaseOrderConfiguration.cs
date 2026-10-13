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

        builder.Property(p => p.PONumber).HasMaxLength(30).IsRequired();
        builder.Property(p => p.UserId).HasMaxLength(255).IsRequired();
        builder.Property(p => p.ApprovedByUserId).HasMaxLength(255);
        builder.Property(p => p.TotalAmount).HasPrecision(18, 2);
        builder.Property(p => p.PaidAmount).HasPrecision(18, 2);
        builder.Property(p => p.RemainingAmount).HasPrecision(18, 2);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.PaymentStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Notes).HasMaxLength(500);

        builder.HasIndex(p => p.PONumber).IsUnique();
        builder.HasIndex(p => p.OrderDate);
        builder.HasIndex(p => p.Status);

        builder.HasOne(p => p.Supplier)
            .WithMany(s => s.PurchaseOrders)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        // Pembuat PO
        builder.HasOne<ApplicationUser>()
            .WithMany(u => u.CreatedPurchaseOrders)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Yang meng-approve (nullable)
        builder.HasOne<ApplicationUser>()
            .WithMany(u => u.ApprovedPurchaseOrders)
            .HasForeignKey(p => p.ApprovedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
