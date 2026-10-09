using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.Sales;

namespace SIA.Infrastructure.Data.Configurations;

public class SaleReturnDetailConfiguration : IEntityTypeConfiguration<SaleReturnDetail>
{
    public void Configure(EntityTypeBuilder<SaleReturnDetail> builder)
    {
        builder.ToTable("SaleReturnDetails");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.UnitPrice)
            .HasPrecision(18, 2);

        builder.Property(d => d.Subtotal)
            .HasPrecision(18, 2);

        builder.HasOne(d => d.SaleReturn)
            .WithMany(r => r.Details)
            .HasForeignKey(d => d.SaleReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.Product)
            .WithMany()
            .HasForeignKey(d => d.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
