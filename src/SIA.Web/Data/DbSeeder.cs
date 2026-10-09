using Microsoft.AspNetCore.Identity;
using SIA.Application.Common.Constants;
using SIA.Infrastructure.Identity;

namespace SIA.Web.Data;

/// <summary>
/// Seeder untuk inisialisasi awal 3 Role dan 1 akun uji untuk masing-masing role.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        // 1. Seed 3 Role
        string[] roles = [Roles.Admin, Roles.Staff, Roles.Viewer];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole(role));
                if (roleResult.Succeeded)
                {
                    logger.LogInformation("Role '{Role}' berhasil dibuat.", role);
                }
                else
                {
                    logger.LogError("Gagal membuat role '{Role}': {Errors}", role, string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                }
            }
        }

        // 2. Seed 1 Akun Uji per Role
        var testUsers = new[]
        {
            (
                Email: "admin@sia.local",
                FullName: "Administrator Toko",
                Role: Roles.Admin,
                Password: "Admin123!"
            ),
            (
                Email: "staff@sia.local",
                FullName: "Staf Kasir & Toko",
                Role: Roles.Staff,
                Password: "Staff123!"
            ),
            (
                Email: "viewer@sia.local",
                FullName: "Akuntan Pemeriksa",
                Role: Roles.Viewer,
                Password: "Viewer123!"
            )
        };

        foreach (var (email, fullName, role, password) in testUsers)
        {
            var existingUser = await userManager.FindByEmailAsync(email);
            if (existingUser == null)
            {
                var newUser = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FullName = fullName,
                    EmailConfirmed = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                var createResult = await userManager.CreateAsync(newUser, password);
                if (createResult.Succeeded)
                {
                    await userManager.AddToRoleAsync(newUser, role);
                    logger.LogInformation("Akun uji '{Email}' ({Role}) berhasil dibuat.", email, role);
                }
                else
                {
                    logger.LogError("Gagal membuat user '{Email}': {Errors}", email, string.Join(", ", createResult.Errors.Select(e => e.Description)));
                }
            }
        }
    }
}
