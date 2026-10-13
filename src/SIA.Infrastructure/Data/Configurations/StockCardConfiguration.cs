using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.Inventory;

namespace SIA.Infrastructure.Data.Configurations;

public class StockCardConfiguration : IEntityTypeConfiguration<StockCard>
{
    public void Configure(EntityTypeBuilder<StockCard> builder)
    {
        builder.ToTable("StockCards");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.MovementType).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Reference).HasMaxLength(30);

        builder.HasIndex(s => new { s.ProductId, s.MovementDate });

        builder.HasOne(s => s.Product)
            .WithMany(p => p.StockCards)
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
