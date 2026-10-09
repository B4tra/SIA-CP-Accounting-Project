using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.Purchasing;
using SIA.Infrastructure.Identity;

namespace SIA.Infrastructure.Data.Configurations;

public class PurchaseReturnConfiguration : IEntityTypeConfiguration<PurchaseReturn>
{
    public void Configure(EntityTypeBuilder<PurchaseReturn> builder)
    {
        builder.ToTable("PurchaseReturns");

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

        builder.HasOne(r => r.PurchaseOrder)
            .WithMany(p => p.Returns)
            .HasForeignKey(r => r.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Details)
            .WithOne(d => d.PurchaseReturn)
            .HasForeignKey(d => d.PurchaseReturnId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
