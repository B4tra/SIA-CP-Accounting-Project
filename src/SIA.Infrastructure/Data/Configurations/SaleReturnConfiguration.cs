using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.Sales;
using SIA.Infrastructure.Identity;

namespace SIA.Infrastructure.Data.Configurations;

public class SaleReturnConfiguration : IEntityTypeConfiguration<SaleReturn>
{
    public void Configure(EntityTypeBuilder<SaleReturn> builder)
    {
        builder.ToTable("SaleReturns");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReturnNumber).HasMaxLength(30).IsRequired();
        builder.Property(r => r.UserId).HasMaxLength(255).IsRequired();
        builder.Property(r => r.TotalAmount).HasPrecision(18, 2);
        builder.Property(r => r.Reason).HasMaxLength(500);

        builder.HasIndex(r => r.ReturnNumber).IsUnique();
        builder.HasIndex(r => r.ReturnDate);

        builder.HasOne(r => r.Sale)
            .WithMany(s => s.Returns)
            .HasForeignKey(r => r.SaleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
