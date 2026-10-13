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

        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.Category).HasMaxLength(50);
        builder.Property(p => p.Brand).HasMaxLength(50);
        builder.Property(p => p.BatteryType).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Voltage).HasMaxLength(20);
        builder.Property(p => p.Capacity).HasMaxLength(20);
        builder.Property(p => p.SellingPrice).HasPrecision(18, 2);
        builder.Property(p => p.PurchasePrice).HasPrecision(18, 2);
        builder.Property(p => p.SKU).HasMaxLength(50);

        // SKU opsional; MySQL mengizinkan banyak NULL pada unique index
        builder.HasIndex(p => p.SKU).IsUnique();
        builder.HasIndex(p => p.Name);
    }
}
