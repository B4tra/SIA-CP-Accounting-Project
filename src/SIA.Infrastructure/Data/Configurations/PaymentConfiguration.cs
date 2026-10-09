using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.Payments;

namespace SIA.Infrastructure.Data.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.PaymentNumber)
            .IsRequired()
            .HasMaxLength(30);

        builder.HasIndex(p => p.PaymentNumber)
            .IsUnique();

        builder.Property(p => p.Amount)
            .HasPrecision(18, 2);

        builder.Property(p => p.Notes)
            .HasMaxLength(250);

        // Index komposit referensi polimorfik (tanpa FK fisik)
        builder.HasIndex(p => new { p.ReferenceType, p.ReferenceId });
    }
}
