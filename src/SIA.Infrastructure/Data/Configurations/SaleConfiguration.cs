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

        builder.Property(s => s.InvoiceNumber).HasMaxLength(30).IsRequired();
        builder.Property(s => s.UserId).HasMaxLength(255).IsRequired();
        builder.Property(s => s.TotalAmount).HasPrecision(18, 2);
        builder.Property(s => s.PaidAmount).HasPrecision(18, 2);
        builder.Property(s => s.RemainingAmount).HasPrecision(18, 2);
        builder.Property(s => s.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.PaymentStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Notes).HasMaxLength(500);

        builder.HasIndex(s => s.InvoiceNumber).IsUnique();
        builder.HasIndex(s => s.SaleDate);
        builder.HasIndex(s => s.PaymentStatus);

        builder.HasOne(s => s.Customer)
            .WithMany(c => c.Sales)
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany(u => u.Sales)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
