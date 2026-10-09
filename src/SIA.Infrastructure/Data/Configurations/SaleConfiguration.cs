using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.Sales;
using SIA.Infrastructure.Identity;

namespace SIA.Infrastructure.Data.Configurations;

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("Sales");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.InvoiceNumber)
            .IsRequired()
            .HasMaxLength(30);

        builder.HasIndex(s => s.InvoiceNumber)
            .IsUnique();

        builder.Property(s => s.TotalAmount)
            .HasPrecision(18, 2);

        builder.Property(s => s.PaidAmount)
            .HasPrecision(18, 2);

        builder.Property(s => s.RemainingAmount)
            .HasPrecision(18, 2);

        builder.Property(s => s.Notes)
            .HasMaxLength(500);

        builder.Property(s => s.UserId)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasOne(s => s.Customer)
            .WithMany(c => c.Sales)
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany(u => u.Sales)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Details)
            .WithOne(d => d.Sale)
            .HasForeignKey(d => d.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Returns)
            .WithOne(r => r.Sale)
            .HasForeignKey(r => r.SaleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
