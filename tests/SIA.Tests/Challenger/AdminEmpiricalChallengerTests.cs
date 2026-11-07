using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.DTOs.System;
using SIA.Domain.Entities.System;
using SIA.Infrastructure.Data;
using SIA.Infrastructure.Identity;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;
using SIA.Tests.Services;
using Xunit;

namespace SIA.Tests.Challenger;

/// <summary>
/// Empirical Challenger Verification Suite for Admin Backend Services & Security Rules.
/// Adversarially tests authorization bypasses, self-protection edge cases, password/secret leaks,
/// security stamp updates, single-role integrity, and query filter boundaries.
/// </summary>
public sealed class AdminEmpiricalChallengerTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly ServiceProvider _sp;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly Mock<IAuditService> _auditMock = new();

    public AdminEmpiricalChallengerTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<AppDbContext>(_ => _db.CreateContext());

        services.AddIdentityCore<ApplicationUser>(opts =>
        {
            opts.User.RequireUniqueEmail = true;
            opts.SignIn.RequireConfirmedAccount = false;
            opts.Password.RequiredLength = 8;
            opts.Password.RequireDigit = true;
            opts.Password.RequireLowercase = true;
            opts.Password.RequireUppercase = true;
            opts.Password.RequireNonAlphanumeric = false;
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>();

        _sp = services.BuildServiceProvider();
        _userManager = _sp.GetRequiredService<UserManager<ApplicationUser>>();
        _roleManager = _sp.GetRequiredService<RoleManager<IdentityRole>>();

        IdentitySeeder.SeedRolesAsync(_roleManager, NullLogger.Instance).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _db.Dispose();
    }

    private UserService CreateUserService(ICurrentUserService currentUser) =>
        new(_userManager, currentUser, _auditMock.Object);

    private UserService CreateUserService(string? role, string userId = "admin-1", string userName = "admin@sia.local")
    {
        var mock = new Mock<ICurrentUserService>();
        mock.SetupGet(u => u.IsAuthenticated).Returns(!string.IsNullOrEmpty(role));
        mock.SetupGet(u => u.UserId).Returns(userId);
        mock.SetupGet(u => u.UserName).Returns(userName);
        mock.Setup(u => u.IsInRole(It.IsAny<string>())).Returns<string>(r => r == role);
        return CreateUserService(mock.Object);
    }

    private AuditService CreateAuditService(ICurrentUserService currentUser) =>
        new(_db.CreateContext(), currentUser);

    private AuditService CreateAuditService(string? role) =>
        CreateAuditService(TestUser.InRole(role is null ? Array.Empty<string>() : new[] { role }));

    private async Task<ApplicationUser> CreateUserInDbAsync(
        string email,
        string fullName,
        string role,
        bool isActive = true,
        string password = "Password123")
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            IsActive = isActive,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
        };

        var res = await _userManager.CreateAsync(user, password);
        Assert.True(res.Succeeded, string.Join("; ", res.Errors.Select(e => e.Description)));

        var roleRes = await _userManager.AddToRoleAsync(user, role);
        Assert.True(roleRes.Succeeded, string.Join("; ", roleRes.Errors.Select(e => e.Description)));

        return user;
    }

    #region Vector 1: Authorization Bypass Tests

    [Theory]
    [InlineData(null)]          // Unauthenticated
    [InlineData(Roles.Staff)]   // Staff
    [InlineData(Roles.Viewer)]  // Viewer
    public async Task UserService_AllMethods_DenyNonAdminCallers(string? role)
    {
        var service = CreateUserService(role);

        // 1. GetAllAsync
        var getAll = await service.GetAllAsync();
        Assert.True(getAll.IsFailure);
        Assert.Equal("Anda tidak memiliki hak akses untuk melihat data ini.", getAll.Error);

        // 2. GetByIdAsync
        var getById = await service.GetByIdAsync("some-id");
        Assert.True(getById.IsFailure);
        Assert.Equal("Anda tidak memiliki hak akses untuk melihat data ini.", getById.Error);

        // 3. CreateAsync
        var create = await service.CreateAsync(new CreateUserDto
        {
            FullName = "New User",
            Email = "new@sia.local",
            Role = Roles.Staff,
            Password = "Password123",
            ConfirmPassword = "Password123",
        });
        Assert.True(create.IsFailure);
        Assert.Equal("Anda tidak memiliki hak akses untuk mengubah data ini.", create.Error);

        // 4. UpdateAsync
        var update = await service.UpdateAsync(new UpdateUserDto
        {
            Id = "some-id",
            FullName = "Updated",
            Email = "updated@sia.local",
            Role = Roles.Staff,
        });
        Assert.True(update.IsFailure);
        Assert.Equal("Anda tidak memiliki hak akses untuk mengubah data ini.", update.Error);

        // 5. SetActiveAsync
        var setActive = await service.SetActiveAsync("some-id", false);
        Assert.True(setActive.IsFailure);
        Assert.Equal("Anda tidak memiliki hak akses untuk mengubah data ini.", setActive.Error);

        // 6. ResetPasswordAsync
        var reset = await service.ResetPasswordAsync(new ResetUserPasswordDto
        {
            Id = "some-id",
            NewPassword = "Password123",
            ConfirmPassword = "Password123",
        });
        Assert.True(reset.IsFailure);
        Assert.Equal("Anda tidak memiliki hak akses untuk mengubah data ini.", reset.Error);
    }

    [Theory]
    [InlineData(null)]          // Unauthenticated
    [InlineData(Roles.Staff)]   // Staff
    [InlineData(Roles.Viewer)]  // Viewer
    public async Task AuditService_Queries_DenyNonAdminCallers(string? role)
    {
        var service = CreateAuditService(role);

        // 1. GetLogsAsync
        var logs = await service.GetLogsAsync(new AuditLogFilterDto());
        Assert.True(logs.IsFailure);
        Assert.Equal("Anda tidak memiliki hak akses untuk melihat data ini.", logs.Error);

        // 2. GetByEntityAsync
        var entityLogs = await service.GetByEntityAsync("User", "u1");
        Assert.True(entityLogs.IsFailure);
        Assert.Equal("Anda tidak memiliki hak akses untuk melihat data ini.", entityLogs.Error);
    }

    #endregion

    #region Vector 2: Self-Protection Bypass & Edge Cases

    [Fact]
    public async Task SelfProtection_AdminCannotSelfDeactivate_ViaSetActive()
    {
        var admin = await CreateUserInDbAsync("admin1@sia.local", "Admin One", Roles.Admin);
        var service = CreateUserService(Roles.Admin, userId: admin.Id, userName: admin.UserName!);

        var result = await service.SetActiveAsync(admin.Id, false);

        Assert.True(result.IsFailure);
        Assert.Contains("tidak dapat menonaktifkan akun Anda sendiri", result.Error);

        var refreshed = await _userManager.FindByIdAsync(admin.Id);
        Assert.True(refreshed!.IsActive);
    }

    [Fact]
    public async Task SelfProtection_AdminCannotSelfDeactivate_ViaUpdateAsync()
    {
        var admin = await CreateUserInDbAsync("admin2@sia.local", "Admin Two", Roles.Admin);
        var service = CreateUserService(Roles.Admin, userId: admin.Id, userName: admin.UserName!);

        var result = await service.UpdateAsync(new UpdateUserDto
        {
            Id = admin.Id,
            FullName = admin.FullName,
            Email = admin.Email!,
            Role = Roles.Admin,
            IsActive = false, // Attempt to deactivate self
        });

        Assert.True(result.IsFailure);
        Assert.Contains("tidak dapat menonaktifkan akun Anda sendiri", result.Error);

        var refreshed = await _userManager.FindByIdAsync(admin.Id);
        Assert.True(refreshed!.IsActive);
    }

    [Fact]
    public async Task SelfProtection_AdminCannotSelfDemote_ViaUpdateAsync()
    {
        var admin = await CreateUserInDbAsync("admin3@sia.local", "Admin Three", Roles.Admin);
        var service = CreateUserService(Roles.Admin, userId: admin.Id, userName: admin.UserName!);

        // Demote to Staff
        var resultStaff = await service.UpdateAsync(new UpdateUserDto
        {
            Id = admin.Id,
            FullName = admin.FullName,
            Email = admin.Email!,
            Role = Roles.Staff,
            IsActive = true,
        });
        Assert.True(resultStaff.IsFailure);
        Assert.Contains("tidak dapat mengubah role akun Anda sendiri", resultStaff.Error);

        // Demote to Viewer
        var resultViewer = await service.UpdateAsync(new UpdateUserDto
        {
            Id = admin.Id,
            FullName = admin.FullName,
            Email = admin.Email!,
            Role = Roles.Viewer,
            IsActive = true,
        });
        Assert.True(resultViewer.IsFailure);
        Assert.Contains("tidak dapat mengubah role akun Anda sendiri", resultViewer.Error);

        var roles = await _userManager.GetRolesAsync(admin);
        Assert.Single(roles);
        Assert.Equal(Roles.Admin, roles[0]);
    }

    [Fact]
    public async Task SelfProtection_WhitespacePaddedId_DoesNotAllowSelfDeactivation()
    {
        var admin = await CreateUserInDbAsync("admin_space@sia.local", "Admin Space", Roles.Admin);
        var service = CreateUserService(Roles.Admin, userId: admin.Id, userName: admin.UserName!);

        // Adversarial attempt: pass padded ID " admin-id " or "admin-id "
        var paddedId = admin.Id + " ";
        var result = await service.SetActiveAsync(paddedId, false);

        // Either it must be rejected by self-check OR rejected because padded id is not found
        // In NO case should admin.IsActive become false!
        var refreshed = await _userManager.FindByIdAsync(admin.Id);
        Assert.True(refreshed!.IsActive, "Admin must remain active despite whitespace-padded ID attack!");
    }

    [Fact]
    public async Task SelfProtection_AdminCanUpdateOwnProfile_WithoutRoleOrActiveChange()
    {
        var admin = await CreateUserInDbAsync("admin_prof@sia.local", "Original Admin Name", Roles.Admin);
        var service = CreateUserService(Roles.Admin, userId: admin.Id, userName: admin.UserName!);

        var result = await service.UpdateAsync(new UpdateUserDto
        {
            Id = admin.Id,
            FullName = "Updated Admin Name",
            Email = "admin_prof@sia.local",
            PhoneNumber = "08111222333",
            Role = Roles.Admin,
            IsActive = true,
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated Admin Name", result.Value!.FullName);
        Assert.Equal("08111222333", result.Value.PhoneNumber);

        var refreshed = await _userManager.FindByIdAsync(admin.Id);
        Assert.Equal("Updated Admin Name", refreshed!.FullName);
    }

    #endregion

    #region Vector 3: Password & Secret Leakage Prevention

    [Fact]
    public async Task PasswordLeakage_CreateUser_DoesNotWritePasswordOrHashToAuditLog()
    {
        string? capturedNewValues = null;
        _auditMock.Setup(a => a.LogAsync(
                "Create",
                "User",
                It.IsAny<string>(),
                null,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string?, string?, CancellationToken>(
                (_, _, _, _, newVals, _) => capturedNewValues = newVals)
            .Returns(Task.CompletedTask);

        var service = CreateUserService(Roles.Admin);
        var secretPassword = "SuperSecretPassword123!";

        var result = await service.CreateAsync(new CreateUserDto
        {
            FullName = "Audit Test User",
            Email = "audittest@sia.local",
            Role = Roles.Staff,
            Password = secretPassword,
            ConfirmPassword = secretPassword,
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedNewValues);

        // Check strictly that plain text password, hash, or security stamp are NOT present
        Assert.DoesNotContain(secretPassword, capturedNewValues);
        Assert.DoesNotContain("PasswordHash", capturedNewValues, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SecurityStamp", capturedNewValues, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConcurrencyStamp", capturedNewValues, StringComparison.OrdinalIgnoreCase);

        // Verify DTO does not leak any secret
        var dto = result.Value!;
        Assert.Equal("audittest@sia.local", dto.Email);
        Assert.Equal(Roles.Staff, dto.Role);
    }

    [Fact]
    public async Task PasswordLeakage_ResetPassword_DoesNotWritePasswordOrHashToAuditLog()
    {
        var user = await CreateUserInDbAsync("reset_leak@sia.local", "Reset Leak", Roles.Staff);
        string? capturedNewValues = null;

        _auditMock.Setup(a => a.LogAsync(
                "ResetPassword",
                "User",
                user.Id,
                null,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string?, string?, CancellationToken>(
                (_, _, _, _, newVals, _) => capturedNewValues = newVals)
            .Returns(Task.CompletedTask);

        var service = CreateUserService(Roles.Admin);
        var newSecret = "BrandNewSecret999!";

        var result = await service.ResetPasswordAsync(new ResetUserPasswordDto
        {
            Id = user.Id,
            NewPassword = newSecret,
            ConfirmPassword = newSecret,
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedNewValues);

        Assert.DoesNotContain(newSecret, capturedNewValues);
        Assert.DoesNotContain("PasswordHash", capturedNewValues, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SecurityStamp", capturedNewValues, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PasswordReset", capturedNewValues);
    }

    [Fact]
    public void SecretLeakage_UserDto_HasNoSensitiveProperties()
    {
        var properties = typeof(UserDto).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propNames = properties.Select(p => p.Name).ToList();

        // Must NOT have any password or security token properties
        Assert.DoesNotContain(propNames, p => p.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propNames, p => p.Contains("Hash", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propNames, p => p.Contains("Stamp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propNames, p => p.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propNames, p => p.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SecretLeakage_AuditSaveChangesInterceptor_DoesNotAuditIdentityUser()
    {
        // Direct DB modification to ApplicationUser
        await using var context = _db.CreateContext();
        var user = new ApplicationUser
        {
            Id = "raw-identity-user",
            UserName = "raw@sia.local",
            Email = "raw@sia.local",
            FullName = "Raw User",
            PasswordHash = "AQAAAAIAAYagAAAAEJRAW_HASH_HERE",
            SecurityStamp = "RAW_SECURITY_STAMP",
            ConcurrencyStamp = "RAW_CONCURRENCY_STAMP",
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Verify that AuditSaveChangesInterceptor did NOT create an AuditLog entry for ApplicationUser
        var auditLogs = await context.AuditLogs.Where(l => l.EntityId == "raw-identity-user").ToListAsync();
        Assert.Empty(auditLogs);
    }

    #endregion

    #region Vector 4: Token & SecurityStamp Invalidation

    [Fact]
    public async Task SecurityStamp_DeactivateUser_ConsistentlyRefreshesSecurityStamp()
    {
        var user = await CreateUserInDbAsync("stamp_deact@sia.local", "Stamp Deact", Roles.Staff, isActive: true);
        var initialStamp = user.SecurityStamp;
        Assert.NotNull(initialStamp);

        var service = CreateUserService(Roles.Admin, userId: "admin-different");

        // Deactivate
        var result = await service.SetActiveAsync(user.Id, false);
        Assert.True(result.IsSuccess);

        var refreshed = await _userManager.FindByIdAsync(user.Id);
        Assert.NotNull(refreshed);
        Assert.False(refreshed.IsActive);
        Assert.NotEqual(initialStamp, refreshed.SecurityStamp);
    }

    [Fact]
    public async Task SecurityStamp_UpdateAsyncDeactivate_RefreshesSecurityStamp()
    {
        var user = await CreateUserInDbAsync("stamp_upd@sia.local", "Stamp Upd", Roles.Staff, isActive: true);
        var initialStamp = user.SecurityStamp;
        var service = CreateUserService(Roles.Admin, userId: "admin-different");

        var result = await service.UpdateAsync(new UpdateUserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email!,
            Role = Roles.Staff,
            IsActive = false, // Deactivate via update
        });

        Assert.True(result.IsSuccess);

        var refreshed = await _userManager.FindByIdAsync(user.Id);
        Assert.NotNull(refreshed);
        Assert.False(refreshed.IsActive);
        Assert.NotEqual(initialStamp, refreshed.SecurityStamp);
    }

    [Fact]
    public async Task SecurityStamp_ResetPassword_RefreshesSecurityStamp()
    {
        var user = await CreateUserInDbAsync("stamp_reset@sia.local", "Stamp Reset", Roles.Staff);
        var initialStamp = user.SecurityStamp;
        var service = CreateUserService(Roles.Admin);

        var result = await service.ResetPasswordAsync(new ResetUserPasswordDto
        {
            Id = user.Id,
            NewPassword = "NewPassword123!",
            ConfirmPassword = "NewPassword123!",
        });

        Assert.True(result.IsSuccess);

        var refreshed = await _userManager.FindByIdAsync(user.Id);
        Assert.NotNull(refreshed);
        Assert.NotEqual(initialStamp, refreshed.SecurityStamp);
    }

    #endregion

    #region Vector 5: Role Consistency Tests

    [Fact]
    public async Task RoleConsistency_UpdateRole_CompletelyReplacesPriorRoleWithoutDuplicates()
    {
        var user = await CreateUserInDbAsync("role_switch@sia.local", "Role Switch", Roles.Staff);
        var service = CreateUserService(Roles.Admin);

        // 1. Switch from Staff -> Viewer
        var result1 = await service.UpdateAsync(new UpdateUserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email!,
            Role = Roles.Viewer,
            IsActive = true,
        });
        Assert.True(result1.IsSuccess);

        var userV = await _userManager.FindByIdAsync(user.Id);
        var rolesV = await _userManager.GetRolesAsync(userV!);
        var singleV = Assert.Single(rolesV);
        Assert.Equal(Roles.Viewer, singleV);

        // 2. Switch from Viewer -> Admin
        var result2 = await service.UpdateAsync(new UpdateUserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email!,
            Role = Roles.Admin,
            IsActive = true,
        });
        Assert.True(result2.IsSuccess);

        var userA = await _userManager.FindByIdAsync(user.Id);
        var rolesA = await _userManager.GetRolesAsync(userA!);
        var singleA = Assert.Single(rolesA);
        Assert.Equal(Roles.Admin, singleA);
    }

    [Fact]
    public async Task RoleConsistency_UserWithMultipleRoles_IsConsolidatedToOneRoleOnUpdate()
    {
        // Artificially give a user 2 roles in DB (e.g. legacy dirty data)
        var user = await CreateUserInDbAsync("multi_role@sia.local", "Multi Role", Roles.Staff);
        await _userManager.AddToRoleAsync(user, Roles.Viewer);

        var initialRoles = await _userManager.GetRolesAsync(user);
        Assert.Equal(2, initialRoles.Count);

        var service = CreateUserService(Roles.Admin);

        // Update to Admin
        var result = await service.UpdateAsync(new UpdateUserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email!,
            Role = Roles.Admin,
            IsActive = true,
        });

        Assert.True(result.IsSuccess);

        var refreshed = await _userManager.FindByIdAsync(user.Id);
        var finalRoles = await _userManager.GetRolesAsync(refreshed!);
        var singleRole = Assert.Single(finalRoles);
        Assert.Equal(Roles.Admin, singleRole);
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("root")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Administrator")]
    public async Task RoleConsistency_InvalidRole_RejectedOnCreateAndOnUpdate(string invalidRole)
    {
        var service = CreateUserService(Roles.Admin);

        // 1. Create with invalid role
        var createResult = await service.CreateAsync(new CreateUserDto
        {
            FullName = "Invalid Role User",
            Email = "invalidrole@sia.local",
            Role = invalidRole,
            Password = "Password123",
            ConfirmPassword = "Password123",
        });
        Assert.True(createResult.IsFailure);

        // 2. Update with invalid role
        var existing = await CreateUserInDbAsync("valid_before@sia.local", "Valid Before", Roles.Staff);
        var updateResult = await service.UpdateAsync(new UpdateUserDto
        {
            Id = existing.Id,
            FullName = existing.FullName,
            Email = existing.Email!,
            Role = invalidRole,
            IsActive = true,
        });
        Assert.True(updateResult.IsFailure);
    }

    #endregion

    #region Vector 6: Query Filter and Boundary Tests

    [Fact]
    public async Task AuditService_GetLogs_FilterBoundaries_InclusiveAndDescending()
    {
        await using var context = _db.CreateContext();
        context.AuditLogs.AddRange(
            new AuditLog { Action = "Action1", EntityName = "User", EntityId = "1", Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc) },
            new AuditLog { Action = "Action2", EntityName = "User", EntityId = "2", Timestamp = new DateTime(2026, 3, 1, 23, 59, 59, DateTimeKind.Utc) },
            new AuditLog { Action = "Action3", EntityName = "User", EntityId = "3", Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc) }
        );
        await context.SaveChangesAsync();

        var service = CreateAuditService(Roles.Admin);

        // Filter for exactly March 1, 2026
        var result = await service.GetLogsAsync(new AuditLogFilterDto
        {
            DateFrom = new DateTime(2026, 3, 1),
            DateTo = new DateTime(2026, 3, 1),
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal("Action2", result.Value[0].Action);
        Assert.Equal("Action1", result.Value[1].Action);
    }

    [Fact]
    public async Task UserService_GetAllAsync_SearchAcrossFullNameEmailAndUserName()
    {
        await CreateUserInDbAsync("andi@sia.local", "Andi Pratama", Roles.Staff);
        await CreateUserInDbAsync("budi@sia.local", "Budi Santoso", Roles.Viewer);
        await CreateUserInDbAsync("citra@sia.local", "Citra Dewi", Roles.Admin);

        var service = CreateUserService(Roles.Admin);

        // Search by FullName substring
        var r1 = await service.GetAllAsync(search: "Pratama");
        Assert.True(r1.IsSuccess);
        Assert.Single(r1.Value!);
        Assert.Equal("andi@sia.local", r1.Value[0].Email);

        // Search by Email substring
        var r2 = await service.GetAllAsync(search: "citra@");
        Assert.True(r2.IsSuccess);
        Assert.Single(r2.Value!);
        Assert.Equal("Citra Dewi", r2.Value[0].FullName);
    }

    [Fact]
    public async Task UserService_UpdateAsync_DuplicateEmailCaseInsensitive_ReturnsFailure()
    {
        var u1 = await CreateUserInDbAsync("user1@sia.local", "User One", Roles.Staff);
        var u2 = await CreateUserInDbAsync("user2@sia.local", "User Two", Roles.Staff);

        var service = CreateUserService(Roles.Admin);

        // Attempt to update u2's email to u1's email with uppercase
        var result = await service.UpdateAsync(new UpdateUserDto
        {
            Id = u2.Id,
            FullName = u2.FullName,
            Email = "USER1@SIA.LOCAL",
            Role = Roles.Staff,
            IsActive = true,
        });

        Assert.True(result.IsFailure);
        Assert.Contains("sudah digunakan", result.Error);
    }

    [Fact]
    public async Task UserService_ResetPasswordAsync_MismatchedConfirmPassword_ReturnsFailure()
    {
        var user = await CreateUserInDbAsync("reset_mismatch@sia.local", "Reset Mismatch", Roles.Staff);
        var service = CreateUserService(Roles.Admin);

        var result = await service.ResetPasswordAsync(new ResetUserPasswordDto
        {
            Id = user.Id,
            NewPassword = "Password123!",
            ConfirmPassword = "DifferentPassword456!",
        });

        Assert.True(result.IsFailure);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task UserService_CreateAsync_WeakPasswordWithoutDigit_RejectedByIdentityPolicy()
    {
        var service = CreateUserService(Roles.Admin);

        var result = await service.CreateAsync(new CreateUserDto
        {
            FullName = "No Digit User",
            Email = "nodigit@sia.local",
            Role = Roles.Staff,
            Password = "PasswordOnlyLetters", // No digits
            ConfirmPassword = "PasswordOnlyLetters",
        });

        Assert.True(result.IsFailure);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task AuditService_GetLogsAsync_InvertedDateRange_ReturnsEmptyList()
    {
        await using var context = _db.CreateContext();
        context.AuditLogs.Add(new AuditLog
        {
            Action = "Create",
            EntityName = "User",
            EntityId = "1",
            Timestamp = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc)
        });
        await context.SaveChangesAsync();

        var service = CreateAuditService(Roles.Admin);

        // DateFrom is later than DateTo
        var result = await service.GetLogsAsync(new AuditLogFilterDto
        {
            DateFrom = new DateTime(2026, 5, 20),
            DateTo = new DateTime(2026, 5, 1),
        });

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    #endregion
}
