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

        builder.Property(j => j.JournalNumber)
            .IsRequired()
            .HasMaxLength(30);

        builder.HasIndex(j => j.JournalNumber)
            .IsUnique();

        builder.Property(j => j.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(j => j.UserId)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasOne<ApplicationUser>()
            .WithMany(u => u.JournalEntries)
            .HasForeignKey(j => j.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(j => j.Lines)
            .WithOne(l => l.JournalEntry)
            .HasForeignKey(l => l.JournalEntryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
