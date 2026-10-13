using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.Accounting;
using SIA.Infrastructure.Identity;

namespace SIA.Infrastructure.Data.Configurations;

public class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("JournalEntries");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.JournalNumber).HasMaxLength(30).IsRequired();
        builder.Property(j => j.Description).HasMaxLength(255).IsRequired();
        builder.Property(j => j.UserId).HasMaxLength(255).IsRequired();

        builder.HasIndex(j => j.JournalNumber).IsUnique();
        builder.HasIndex(j => j.TransactionDate);

        builder.HasOne<ApplicationUser>()
            .WithMany(u => u.JournalEntries)
            .HasForeignKey(j => j.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
