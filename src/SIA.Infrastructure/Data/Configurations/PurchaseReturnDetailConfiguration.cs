using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.Purchasing;

namespace SIA.Infrastructure.Data.Configurations;

public class PurchaseReturnDetailConfiguration : IEntityTypeConfiguration<PurchaseReturnDetail>
{
    public void Configure(EntityTypeBuilder<PurchaseReturnDetail> builder)
    {
        builder.ToTable("PurchaseReturnDetails");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.UnitPrice)
            .HasPrecision(18, 2);

        builder.Property(d => d.Subtotal)
            .HasPrecision(18, 2);

        builder.HasOne(d => d.PurchaseReturn)
            .WithMany(r => r.Details)
            .HasForeignKey(d => d.PurchaseReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.Product)
            .WithMany()
            .HasForeignKey(d => d.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
