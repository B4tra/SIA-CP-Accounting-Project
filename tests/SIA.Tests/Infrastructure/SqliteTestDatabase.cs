using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Interfaces;
using SIA.Infrastructure.Data;
using SIA.Infrastructure.Data.Interceptors;

namespace SIA.Tests.Infrastructure;

/// <summary>
/// Database SQLite in-memory untuk test Infrastructure. Koneksi tetap terbuka selama
/// fixture hidup sehingga beberapa DbContext dapat berbagi data yang sama.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AuditSaveChangesInterceptor _interceptor;

    public SqliteTestDatabase(ICurrentUserService? currentUser = null)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _interceptor = new AuditSaveChangesInterceptor(currentUser);

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_interceptor)
            .Options;
        return new AppDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
