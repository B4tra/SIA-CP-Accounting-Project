using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.DTOs.System;
using SIA.Infrastructure.Data;
using SIA.Infrastructure.Identity;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;
using Xunit;

namespace SIA.Tests.Services;

public class UserServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly Mock<IAuditService> _auditMock = new();
    private readonly ServiceProvider _sp;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public UserServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Gunakan DbContext yang berbagi koneksi SQLite in-memory yang sama
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

        // Seed ketiga role standar
        IdentitySeeder.SeedRolesAsync(_roleManager, NullLogger.Instance).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _db.Dispose();
    }

    private UserService CreateService(ICurrentUserService currentUser) =>
        new(_userManager, currentUser, _auditMock.Object);

    private UserService CreateService(string role, string userId = "admin-logged-in", string userName = "admin@sia.local")
    {
        var mock = new Mock<ICurrentUserService>();
        mock.SetupGet(u => u.IsAuthenticated).Returns(!string.IsNullOrEmpty(role));
        mock.SetupGet(u => u.UserId).Returns(userId);
        mock.SetupGet(u => u.UserName).Returns(userName);
        mock.Setup(u => u.IsInRole(It.IsAny<string>())).Returns<string>(r => r == role);
        return CreateService(mock.Object);
    }

    private async Task<ApplicationUser> CreateTestUserAsync(
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

        var created = await _userManager.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        var addedRole = await _userManager.AddToRoleAsync(user, role);
        Assert.True(addedRole.Succeeded, string.Join("; ", addedRole.Errors.Select(e => e.Description)));

        return user;
    }

    private const string ReadDenied = "Anda tidak memiliki hak akses untuk melihat data ini.";
    private const string WriteDenied = "Anda tidak memiliki hak akses untuk mengubah data ini.";

    #region 1. Authorization Tests

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Viewer)]
    public async Task GetAllAsync_NonAdmin_ReturnsFailureReadDenied(string role)
    {
        var service = CreateService(role);

        var result = await service.GetAllAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ReadDenied, result.Error);
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Viewer)]
    public async Task GetByIdAsync_NonAdmin_ReturnsFailureReadDenied(string role)
    {
        var service = CreateService(role);

        var result = await service.GetByIdAsync("any-id");

        Assert.True(result.IsFailure);
        Assert.Equal(ReadDenied, result.Error);
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Viewer)]
    public async Task CreateAsync_NonAdmin_ReturnsFailureWriteDenied(string role)
    {
        var service = CreateService(role);

        var result = await service.CreateAsync(new CreateUserDto
        {
            FullName = "User Baru",
            Email = "baru@sia.local",
            Role = Roles.Staff,
            Password = "Password123",
            ConfirmPassword = "Password123",
        });

        Assert.True(result.IsFailure);
        Assert.Equal(WriteDenied, result.Error);
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Viewer)]
    public async Task UpdateAsync_NonAdmin_ReturnsFailureWriteDenied(string role)
    {
        var service = CreateService(role);

        var result = await service.UpdateAsync(new UpdateUserDto
        {
            Id = "any-id",
            FullName = "Update User",
            Email = "update@sia.local",
            Role = Roles.Staff,
        });

        Assert.True(result.IsFailure);
        Assert.Equal(WriteDenied, result.Error);
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Viewer)]
    public async Task SetActiveAsync_NonAdmin_ReturnsFailureWriteDenied(string role)
    {
        var service = CreateService(role);

        var result = await service.SetActiveAsync("any-id", false);

        Assert.True(result.IsFailure);
        Assert.Equal(WriteDenied, result.Error);
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Viewer)]
    public async Task ResetPasswordAsync_NonAdmin_ReturnsFailureWriteDenied(string role)
    {
        var service = CreateService(role);

        var result = await service.ResetPasswordAsync(new ResetUserPasswordDto
        {
            Id = "any-id",
            NewPassword = "PasswordBaru123",
            ConfirmPassword = "PasswordBaru123",
        });

        Assert.True(result.IsFailure);
        Assert.Equal(WriteDenied, result.Error);
    }

    #endregion

    #region 2. Create User Tests

    [Fact]
    public async Task CreateAsync_Admin_ValidDto_CreatesUserAndAssignsSingleRoleAndLogsAuditWithoutPassword()
    {
        var service = CreateService(Roles.Admin);
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

        var dto = new CreateUserDto
        {
            FullName = "Budi Staff",
            Email = "budistaff@sia.local",
            PhoneNumber = "08123456789",
            Role = Roles.Staff,
            Password = "PasswordStaff123",
            ConfirmPassword = "PasswordStaff123",
            IsActive = true,
        };

        var result = await service.CreateAsync(dto);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Budi Staff", result.Value.FullName);
        Assert.Equal("budistaff@sia.local", result.Value.Email);
        Assert.Equal(Roles.Staff, result.Value.Role);
        Assert.True(result.Value.IsActive);

        // Verifikasi database
        var createdUser = await _userManager.FindByEmailAsync("budistaff@sia.local");
        Assert.NotNull(createdUser);
        Assert.True(await _userManager.CheckPasswordAsync(createdUser, "PasswordStaff123"));

        var roles = await _userManager.GetRolesAsync(createdUser);
        var singleRole = Assert.Single(roles);
        Assert.Equal(Roles.Staff, singleRole);

        // Verifikasi audit log tidak memuat password atau password hash
        Assert.NotNull(capturedNewValues);
        Assert.DoesNotContain("PasswordStaff123", capturedNewValues);
        Assert.DoesNotContain("PasswordHash", capturedNewValues, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Budi Staff", capturedNewValues);
        Assert.Contains(Roles.Staff, capturedNewValues);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmail_ReturnsFailure()
    {
        await CreateTestUserAsync("kembar@sia.local", "User Kembar", Roles.Staff);
        var service = CreateService(Roles.Admin);

        var result = await service.CreateAsync(new CreateUserDto
        {
            FullName = "User Baru Kembar",
            Email = "kembar@sia.local",
            Role = Roles.Viewer,
            Password = "Password123",
            ConfirmPassword = "Password123",
        });

        Assert.True(result.IsFailure);
        Assert.Contains("sudah digunakan", result.Error);
    }

    [Fact]
    public async Task CreateAsync_InvalidRole_ReturnsFailure()
    {
        var service = CreateService(Roles.Admin);

        var result = await service.CreateAsync(new CreateUserDto
        {
            FullName = "User Role Palsu",
            Email = "palsu@sia.local",
            Role = "SuperAdmin",
            Password = "Password123",
            ConfirmPassword = "Password123",
        });

        Assert.True(result.IsFailure);
        Assert.Contains("Role tidak valid", result.Error);
    }

    [Fact]
    public async Task CreateAsync_ValidationError_ReturnsValidationFailure()
    {
        var service = CreateService(Roles.Admin);

        var result = await service.CreateAsync(new CreateUserDto
        {
            FullName = "", // Required
            Email = "bukan-email", // Format email salah
            Role = Roles.Staff,
            Password = "pendek", // Kurang dari 8 karakter
            ConfirmPassword = "beda",
        });

        Assert.True(result.IsFailure);
        Assert.NotEmpty(result.Errors);
    }

    #endregion

    #region 3. Update Profile & Single-Role Tests

    [Fact]
    public async Task UpdateAsync_Admin_UpdatesProfileAndChangesSingleRole_EmitsAuditLog()
    {
        var existing = await CreateTestUserAsync("lama@sia.local", "Nama Lama", Roles.Staff);
        var service = CreateService(Roles.Admin);
        string? capturedOldValues = null;
        string? capturedNewValues = null;

        _auditMock.Setup(a => a.LogAsync(
                "Update",
                "User",
                existing.Id,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string?, string?, CancellationToken>(
                (_, _, _, oldVals, newVals, _) =>
                {
                    capturedOldValues = oldVals;
                    capturedNewValues = newVals;
                })
            .Returns(Task.CompletedTask);

        var updateDto = new UpdateUserDto
        {
            Id = existing.Id,
            FullName = "Nama Baru",
            Email = "baru@sia.local",
            PhoneNumber = "0899999999",
            Role = Roles.Viewer, // Ganti dari Staff -> Viewer
            IsActive = true,
        };

        var result = await service.UpdateAsync(updateDto);

        Assert.True(result.IsSuccess);
        Assert.Equal("Nama Baru", result.Value!.FullName);
        Assert.Equal("baru@sia.local", result.Value.Email);
        Assert.Equal(Roles.Viewer, result.Value.Role);

        // Verifikasi database: role tunggal (hanya Viewer, Staff telah dicopot)
        var updatedUser = await _userManager.FindByIdAsync(existing.Id);
        Assert.NotNull(updatedUser);
        Assert.Equal("Nama Baru", updatedUser.FullName);
        Assert.Equal("baru@sia.local", updatedUser.Email);

        var roles = await _userManager.GetRolesAsync(updatedUser);
        var role = Assert.Single(roles);
        Assert.Equal(Roles.Viewer, role);

        // Verifikasi audit log memuat perubahan
        Assert.NotNull(capturedOldValues);
        Assert.NotNull(capturedNewValues);
        Assert.Contains(Roles.Staff, capturedOldValues);
        Assert.Contains(Roles.Viewer, capturedNewValues);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentUser_ReturnsFailure()
    {
        var service = CreateService(Roles.Admin);

        var result = await service.UpdateAsync(new UpdateUserDto
        {
            Id = "unknown-id",
            FullName = "Nama",
            Email = "email@sia.local",
            Role = Roles.Staff,
        });

        Assert.True(result.IsFailure);
        Assert.Equal("Pengguna tidak ditemukan.", result.Error);
    }

    #endregion

    #region 4. Self-Demote & Self-Deactivate Protection Tests

    [Fact]
    public async Task SetActiveAsync_SelfDeactivate_ReturnsFailureAndPreservesActiveState()
    {
        var admin = await CreateTestUserAsync("admin_me@sia.local", "Admin Me", Roles.Admin);
        var service = CreateService(Roles.Admin, userId: admin.Id, userName: admin.UserName!);

        var result = await service.SetActiveAsync(admin.Id, false);

        Assert.True(result.IsFailure);
        Assert.Contains("tidak dapat menonaktifkan akun Anda sendiri", result.Error);

        var refreshed = await _userManager.FindByIdAsync(admin.Id);
        Assert.True(refreshed!.IsActive);

        // Tidak boleh mencatat audit nonaktifkan diri sendiri
        _auditMock.Verify(a => a.LogAsync("Deactivate", "User", admin.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_SelfDemote_ReturnsFailureAndPreservesAdminRole()
    {
        var admin = await CreateTestUserAsync("admin_demote@sia.local", "Admin Demote", Roles.Admin);
        var service = CreateService(Roles.Admin, userId: admin.Id, userName: admin.UserName!);

        var result = await service.UpdateAsync(new UpdateUserDto
        {
            Id = admin.Id,
            FullName = "Admin Demote",
            Email = admin.Email!,
            Role = Roles.Staff, // Percobaan menurunkan role diri sendiri menjadi Staff
            IsActive = true,
        });

        Assert.True(result.IsFailure);
        Assert.Contains("tidak dapat mengubah role akun Anda sendiri", result.Error);

        var refreshed = await _userManager.FindByIdAsync(admin.Id);
        var roles = await _userManager.GetRolesAsync(refreshed!);
        Assert.Single(roles);
        Assert.Equal(Roles.Admin, roles[0]);
    }

    [Fact]
    public async Task UpdateAsync_SelfProfileChangeWithoutRoleChange_Succeeds()
    {
        var admin = await CreateTestUserAsync("admin_self@sia.local", "Admin Asli", Roles.Admin);
        var service = CreateService(Roles.Admin, userId: admin.Id, userName: admin.UserName!);

        var result = await service.UpdateAsync(new UpdateUserDto
        {
            Id = admin.Id,
            FullName = "Admin Ganti Nama",
            Email = "admin_baru@sia.local",
            Role = Roles.Admin, // Tetap Admin
            IsActive = true,
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("Admin Ganti Nama", result.Value!.FullName);
        Assert.Equal("admin_baru@sia.local", result.Value.Email);
    }

    #endregion

    #region 5. Activate / Deactivate & SecurityStamp Tests

    [Fact]
    public async Task SetActiveAsync_DeactivateOtherUser_UpdatesIsActiveAndRefreshesSecurityStamp()
    {
        var user = await CreateTestUserAsync("target@sia.local", "Target Staff", Roles.Staff, isActive: true);
        var initialStamp = user.SecurityStamp;
        var service = CreateService(Roles.Admin, userId: "admin-different");

        var result = await service.SetActiveAsync(user.Id, false);

        Assert.True(result.IsSuccess);

        var refreshed = await _userManager.FindByIdAsync(user.Id);
        Assert.NotNull(refreshed);
        Assert.False(refreshed.IsActive);
        Assert.NotEqual(initialStamp, refreshed.SecurityStamp);

        _auditMock.Verify(a => a.LogAsync("Deactivate", "User", user.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetActiveAsync_ActivateOtherUser_UpdatesIsActiveToTrue()
    {
        var user = await CreateTestUserAsync("inactive@sia.local", "Inactive Staff", Roles.Staff, isActive: false);
        var service = CreateService(Roles.Admin, userId: "admin-different");

        var result = await service.SetActiveAsync(user.Id, true);

        Assert.True(result.IsSuccess);

        var refreshed = await _userManager.FindByIdAsync(user.Id);
        Assert.NotNull(refreshed);
        Assert.True(refreshed.IsActive);

        _auditMock.Verify(a => a.LogAsync("Activate", "User", user.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetActiveAsync_SameStatus_ReturnsSuccessWithoutChanges()
    {
        var user = await CreateTestUserAsync("aktif@sia.local", "Aktif Staff", Roles.Staff, isActive: true);
        var service = CreateService(Roles.Admin, userId: "admin-different");

        var result = await service.SetActiveAsync(user.Id, true);

        Assert.True(result.IsSuccess);
        _auditMock.Verify(a => a.LogAsync(It.IsAny<string>(), "User", user.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region 6. Reset Password & SecurityStamp Tests

    [Fact]
    public async Task ResetPasswordAsync_Admin_UpdatesPasswordAndRefreshesSecurityStampAndAuditsWithoutPassword()
    {
        var user = await CreateTestUserAsync("reset@sia.local", "Reset Staff", Roles.Staff, password: "PasswordLama123");
        var initialStamp = user.SecurityStamp;
        var service = CreateService(Roles.Admin);
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

        var dto = new ResetUserPasswordDto
        {
            Id = user.Id,
            NewPassword = "PasswordBaru123",
            ConfirmPassword = "PasswordBaru123",
        };

        var result = await service.ResetPasswordAsync(dto);

        Assert.True(result.IsSuccess);

        var refreshed = await _userManager.FindByIdAsync(user.Id);
        Assert.NotNull(refreshed);
        Assert.True(await _userManager.CheckPasswordAsync(refreshed, "PasswordBaru123"));
        Assert.False(await _userManager.CheckPasswordAsync(refreshed, "PasswordLama123"));
        Assert.NotEqual(initialStamp, refreshed.SecurityStamp);

        // Verifikasi audit log tidak memuat password baru maupun hash
        Assert.NotNull(capturedNewValues);
        Assert.DoesNotContain("PasswordBaru123", capturedNewValues);
        Assert.DoesNotContain("PasswordLama123", capturedNewValues);
        Assert.DoesNotContain("PasswordHash", capturedNewValues, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PasswordReset", capturedNewValues);
    }

    [Fact]
    public async Task ResetPasswordAsync_ShortPassword_ReturnsFailure()
    {
        var user = await CreateTestUserAsync("pendek@sia.local", "User Pendek", Roles.Staff);
        var service = CreateService(Roles.Admin);

        var result = await service.ResetPasswordAsync(new ResetUserPasswordDto
        {
            Id = user.Id,
            NewPassword = "123",
            ConfirmPassword = "123",
        });

        Assert.True(result.IsFailure);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task ResetPasswordAsync_NonExistentUser_ReturnsFailure()
    {
        var service = CreateService(Roles.Admin);

        var result = await service.ResetPasswordAsync(new ResetUserPasswordDto
        {
            Id = "unknown-id",
            NewPassword = "PasswordBaru123",
            ConfirmPassword = "PasswordBaru123",
        });

        Assert.True(result.IsFailure);
        Assert.Equal("Pengguna tidak ditemukan.", result.Error);
    }

    #endregion

    #region 7. GetAll & GetById Tests

    [Fact]
    public async Task GetAllAsync_Admin_ReturnsAllUsersWithSingleRole()
    {
        await CreateTestUserAsync("u1@sia.local", "User Satu", Roles.Admin);
        await CreateTestUserAsync("u2@sia.local", "User Dua", Roles.Staff);
        await CreateTestUserAsync("u3@sia.local", "User Tiga", Roles.Viewer);

        var service = CreateService(Roles.Admin);
        var result = await service.GetAllAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);

        var roleMap = result.Value.ToDictionary(u => u.Email, u => u.Role);
        Assert.Equal(Roles.Admin, roleMap["u1@sia.local"]);
        Assert.Equal(Roles.Staff, roleMap["u2@sia.local"]);
        Assert.Equal(Roles.Viewer, roleMap["u3@sia.local"]);
    }

    [Fact]
    public async Task GetAllAsync_WithSearchAndStatusFilters_ReturnsFilteredList()
    {
        await CreateTestUserAsync("aktif@sia.local", "Budi Aktif", Roles.Staff, isActive: true);
        await CreateTestUserAsync("nonaktif@sia.local", "Budi Nonaktif", Roles.Staff, isActive: false);
        await CreateTestUserAsync("joko@sia.local", "Joko Aktif", Roles.Viewer, isActive: true);

        var service = CreateService(Roles.Admin);

        var searchResult = await service.GetAllAsync(search: "Budi");
        Assert.True(searchResult.IsSuccess);
        Assert.Equal(2, searchResult.Value!.Count);

        var activeResult = await service.GetAllAsync(isActive: false);
        Assert.True(activeResult.IsSuccess);
        var single = Assert.Single(activeResult.Value!);
        Assert.Equal("nonaktif@sia.local", single.Email);
    }

    [Fact]
    public async Task GetByIdAsync_Admin_ReturnsUserDto()
    {
        var user = await CreateTestUserAsync("detail@sia.local", "Detail Staff", Roles.Staff);
        var service = CreateService(Roles.Admin);

        var result = await service.GetByIdAsync(user.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("Detail Staff", result.Value!.FullName);
        Assert.Equal(Roles.Staff, result.Value.Role);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentUser_ReturnsFailure()
    {
        var service = CreateService(Roles.Admin);

        var result = await service.GetByIdAsync("unknown-id");

        Assert.True(result.IsFailure);
        Assert.Equal("Pengguna tidak ditemukan.", result.Error);
    }

    #endregion
}
