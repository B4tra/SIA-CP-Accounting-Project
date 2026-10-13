using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SIA.Infrastructure.Identity;

namespace SIA.Infrastructure.Data.Configurations;

/// <summary>
/// Tambahan kolom pada tabel AspNetUsers. Kolom bawaan Identity dikonfigurasi oleh IdentityDbContext.
/// </summary>
public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.FullName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.IsActive).HasDefaultValue(true);
    }
}
