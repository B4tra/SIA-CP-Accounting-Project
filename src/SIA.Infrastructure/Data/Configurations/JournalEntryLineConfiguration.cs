using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.Accounting;

namespace SIA.Infrastructure.Data.Configurations;

public class JournalEntryLineConfiguration : IEntityTypeConfiguration<JournalEntryLine>
{
    public void Configure(EntityTypeBuilder<JournalEntryLine> builder)
    {
        builder.ToTable("JournalEntryLines");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Debit)
            .HasPrecision(18, 2);

        builder.Property(l => l.Credit)
            .HasPrecision(18, 2);

        builder.HasOne(l => l.JournalEntry)
            .WithMany(j => j.Lines)
            .HasForeignKey(l => l.JournalEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Account)
            .WithMany(a => a.JournalEntryLines)
            .HasForeignKey(l => l.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
