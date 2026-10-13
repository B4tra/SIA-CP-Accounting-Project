using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.System;

namespace SIA.Infrastructure.Data.Configurations;

public class TransactionNumberConfigConfiguration : IEntityTypeConfiguration<TransactionNumberConfig>
{
    public void Configure(EntityTypeBuilder<TransactionNumberConfig> builder)
    {
        builder.ToTable("TransactionNumberConfigs");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TransactionType).HasMaxLength(30).IsRequired();
        builder.Property(t => t.Prefix).HasMaxLength(10).IsRequired();
        builder.Property(t => t.Format).HasMaxLength(50).IsRequired();

        // Concurrency token: mencegah dua transaksi mendapat nomor yang sama
        builder.Property(t => t.CurrentSequence).IsConcurrencyToken();

        builder.HasIndex(t => t.TransactionType).IsUnique();
    }
}
