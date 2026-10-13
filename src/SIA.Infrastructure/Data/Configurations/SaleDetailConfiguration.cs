using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.Sales;

namespace SIA.Infrastructure.Data.Configurations;

public class SaleDetailConfiguration : IEntityTypeConfiguration<SaleDetail>
{
    public void Configure(EntityTypeBuilder<SaleDetail> builder)
    {
        builder.ToTable("SaleDetails");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.UnitPrice).HasPrecision(18, 2);
        builder.Property(d => d.Subtotal).HasPrecision(18, 2);

        builder.HasOne(d => d.Sale)
            .WithMany(s => s.Details)
            .HasForeignKey(d => d.SaleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Product)
            .WithMany(p => p.SaleDetails)
            .HasForeignKey(d => d.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
