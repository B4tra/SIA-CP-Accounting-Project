using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.Accounting;

namespace SIA.Infrastructure.Data.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Code).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(255);
        builder.Property(a => a.Category).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.NormalBalance).HasConversion<string>().HasMaxLength(10);
        builder.Property(a => a.Balance).HasPrecision(18, 2);

        builder.HasIndex(a => a.Code).IsUnique();

        builder.HasOne(a => a.ParentAccount)
            .WithMany(a => a.ChildAccounts)
            .HasForeignKey(a => a.ParentAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
