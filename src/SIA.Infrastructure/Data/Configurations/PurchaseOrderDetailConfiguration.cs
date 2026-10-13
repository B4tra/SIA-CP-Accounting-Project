using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.Purchasing;

namespace SIA.Infrastructure.Data.Configurations;

public class PurchaseOrderDetailConfiguration : IEntityTypeConfiguration<PurchaseOrderDetail>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderDetail> builder)
    {
        builder.ToTable("PurchaseOrderDetails");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.UnitPrice).HasPrecision(18, 2);
        builder.Property(d => d.Subtotal).HasPrecision(18, 2);

        builder.HasOne(d => d.PurchaseOrder)
            .WithMany(p => p.Details)
            .HasForeignKey(d => d.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Product)
            .WithMany(p => p.PurchaseOrderDetails)
            .HasForeignKey(d => d.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
