using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Domain.Entities.System;

namespace SIA.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        builder.Property(a => a.UserId).HasMaxLength(255);
        builder.Property(a => a.Action).HasMaxLength(20).IsRequired();
        builder.Property(a => a.EntityName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
        // OldValues/NewValues tanpa batas panjang (longtext di MySQL)
        builder.Property(a => a.IpAddress).HasMaxLength(45);

        builder.HasIndex(a => new { a.EntityName, a.EntityId });
        builder.HasIndex(a => a.Timestamp);
        builder.HasIndex(a => a.UserId);
    }
}
