using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.Inventory;

namespace SIA.Infrastructure.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Description)
            .HasMaxLength(250);

        builder.Property(p => p.Category)
            .HasMaxLength(50);

        builder.Property(p => p.Brand)
            .HasMaxLength(50);

        builder.Property(p => p.Voltage)
            .HasMaxLength(20);

        builder.Property(p => p.Capacity)
            .HasMaxLength(20);

        builder.Property(p => p.SKU)
            .HasMaxLength(50);

        builder.HasIndex(p => p.SKU);

        builder.Property(p => p.SellingPrice)
            .HasPrecision(18, 2);

        builder.Property(p => p.PurchasePrice)
            .HasPrecision(18, 2);

        builder.HasMany(p => p.StockCards)
            .WithOne(s => s.Product)
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
