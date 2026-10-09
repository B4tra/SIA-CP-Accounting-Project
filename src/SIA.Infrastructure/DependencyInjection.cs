using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SIA.Application.Common.Interfaces;
using SIA.Infrastructure.Data;
using SIA.Infrastructure.Data.Interceptors;
using SIA.Infrastructure.Identity;
using SIA.Infrastructure.Repositories;

namespace SIA.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Server=localhost;Database=sia_db;User=root;Password=;";

        var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));

        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var auditInterceptor = sp.GetRequiredService<AuditSaveChangesInterceptor>();

            options.UseMySql(connectionString, serverVersion, mysqlOptions =>
            {
                mysqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            })
            .AddInterceptors(auditInterceptor);
        });

        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequiredLength = 6;
            options.User.RequireUniqueEmail = true;
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>();

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
