using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SIA.Application.Common.Interfaces;
using SIA.Infrastructure.Data;
using SIA.Infrastructure.Data.Interceptors;
using SIA.Infrastructure.Repositories;

namespace SIA.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "DefaultConnection";
    public const string DefaultServerVersion = "8.0.36-mysql";

    /// <summary>
    /// Mendaftarkan DbContext (MySQL/Pomelo), interceptor audit, repository, dan unit of work.
    /// Connection string diambil dari <c>ConnectionStrings:DefaultConnection</c>
    /// (user-secrets / environment variable — jangan disimpan di appsettings yang di-commit).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' belum dikonfigurasi. " +
                "Atur lewat user-secrets atau environment variable 'ConnectionStrings__DefaultConnection'.");

        var serverVersion = ServerVersion.Parse(configuration["Database:ServerVersion"] ?? DefaultServerVersion);

        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseMySql(connectionString, serverVersion,
                mySql => mySql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
