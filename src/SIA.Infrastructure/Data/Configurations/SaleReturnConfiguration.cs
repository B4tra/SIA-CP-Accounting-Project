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

        builder.Property(r => r.ReturnNumber)
            .IsRequired()
            .HasMaxLength(30);

        builder.HasIndex(r => r.ReturnNumber)
            .IsUnique();

        builder.Property(r => r.TotalAmount)
            .HasPrecision(18, 2);

        builder.Property(r => r.Reason)
            .HasMaxLength(250);

        builder.Property(r => r.UserId)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasOne(r => r.Sale)
            .WithMany(s => s.Returns)
            .HasForeignKey(r => r.SaleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Details)
            .WithOne(d => d.SaleReturn)
            .HasForeignKey(d => d.SaleReturnId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
