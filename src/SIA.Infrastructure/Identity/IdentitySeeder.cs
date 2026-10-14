using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using SIA.Application.Common.Constants;

namespace SIA.Infrastructure.Identity;

/// <summary>
/// Seeding role Identity (Admin, Staff, Viewer) dan satu akun uji per role.
/// Password akun uji diambil dari konfigurasi <c>Seed:DefaultPassword</c> (user-secrets / env var),
/// tidak pernah di-hardcode.
/// </summary>
public static class IdentitySeeder
{
    public const string DefaultPasswordConfigKey = "Seed:DefaultPassword";

    public sealed record TestUser(string Email, string FullName, string Role);

    public static readonly IReadOnlyList<TestUser> TestUsers = new[]
    {
        new TestUser("admin@sia.local", "Administrator", Roles.Admin),
        new TestUser("staff@sia.local", "Staf Toko", Roles.Staff),
        new TestUser("viewer@sia.local", "Akuntan", Roles.Viewer),
    };

    /// <summary>Membuat role yang belum ada (idempoten).</summary>
    public static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager, ILogger logger)
    {
        foreach (var role in Roles.All)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole(role));
            EnsureSucceeded(result, $"membuat role '{role}'");
            logger.LogInformation("Role {Role} dibuat.", role);
        }
    }

    /// <summary>
    /// Membuat akun uji per role (idempoten). Jika password kosong, seeding akun dilewati.
    /// </summary>
    public static async Task SeedTestUsersAsync(UserManager<ApplicationUser> userManager, string? defaultPassword, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(defaultPassword))
        {
            logger.LogWarning(
                "Seeding akun uji dilewati: '{Key}' belum diatur (gunakan user-secrets atau environment variable).",
                DefaultPasswordConfigKey);
            return;
        }

        foreach (var testUser in TestUsers)
        {
            var user = await userManager.FindByEmailAsync(testUser.Email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = testUser.Email,
                    Email = testUser.Email,
                    EmailConfirmed = true,
                    FullName = testUser.FullName,
                    IsActive = true,
                };

                var created = await userManager.CreateAsync(user, defaultPassword);
                EnsureSucceeded(created, $"membuat akun '{testUser.Email}'");
                logger.LogInformation("Akun uji {Email} dibuat.", testUser.Email);
            }

            if (!await userManager.IsInRoleAsync(user, testUser.Role))
            {
                var added = await userManager.AddToRoleAsync(user, testUser.Role);
                EnsureSucceeded(added, $"menambahkan '{testUser.Email}' ke role '{testUser.Role}'");
            }
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Gagal {action}: {errors}");
        }
    }
}
