using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SIA.Infrastructure.Data;

/// <summary>
/// Dipakai oleh tool <c>dotnet ef</c> (migrations add / database update).
/// Connection string dibaca dari environment variable <c>ConnectionStrings__DefaultConnection</c>
/// agar tidak ada rahasia yang tersimpan di repository. Tanpa env var, dipakai connection string
/// lokal tanpa password yang cukup untuk membuat file migration (tidak membuka koneksi).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string FallbackConnectionString = "Server=localhost;Port=3306;Database=sia_db;User=root;";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                               ?? FallbackConnectionString;
        var serverVersion = ServerVersion.Parse(
            Environment.GetEnvironmentVariable("Database__ServerVersion") ?? DependencyInjection.DefaultServerVersion);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(connectionString, serverVersion,
                mySql => mySql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }
}
