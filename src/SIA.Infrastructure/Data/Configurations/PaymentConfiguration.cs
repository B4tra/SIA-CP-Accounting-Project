using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.Payments;

namespace SIA.Infrastructure.Data.Configurations;

/// <summary>
/// Payment polimorfik (ReferenceType + ReferenceId) — sengaja TANPA foreign key;
/// integritas divalidasi di IPaymentService.
/// </summary>
public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PaymentNumber).HasMaxLength(30).IsRequired();
        builder.Property(p => p.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.ReferenceType).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Notes).HasMaxLength(500);

        builder.HasIndex(p => p.PaymentNumber).IsUnique();
        builder.HasIndex(p => new { p.ReferenceType, p.ReferenceId });
        builder.HasIndex(p => p.PaymentDate);
    }
}
