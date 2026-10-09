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

        builder.Property(t => t.TransactionType)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(t => t.TransactionType)
            .IsUnique();

        builder.Property(t => t.Prefix)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(t => t.Format)
            .IsRequired()
            .HasMaxLength(50);
    }
}
