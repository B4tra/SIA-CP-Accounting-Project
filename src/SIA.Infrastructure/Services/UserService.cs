using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.System;
using SIA.Application.Services.System;
using SIA.Infrastructure.Identity;

namespace SIA.Infrastructure.Services;

/// <summary>
/// Implementasi <see cref="IUserService"/> berbasis ASP.NET Core Identity.
/// Otorisasi server-side: hanya Admin yang dapat mengakses semua metode.
/// Aturan bisnis:
/// - Single-role validation (Admin, Staff, Viewer).
/// - Self-protection: Admin tidak dapat menonaktifkan dirinya sendiri atau menurunkan role dirinya sendiri.
/// - SecurityStamp diperbarui saat nonaktifkan atau reset kata sandi.
/// - Audit logging via IAuditService.LogAsync tanpa mencatat password atau hash.
/// </summary>
public class UserService : IUserService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;

    public UserService(
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUser,
        IAuditService auditService)
    {
        _userManager = userManager;
        _currentUser = currentUser;
        _auditService = auditService;
    }

    public async Task<Result<IReadOnlyList<UserDto>>> GetAllAsync(
        string? search = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.RequireAdmin))
            return Result<IReadOnlyList<UserDto>>.Failure(ServiceGuard.ReadDenied);

        var query = _userManager.Users.AsNoTracking().AsQueryable();

        if (isActive.HasValue)
            query = query.Where(u => u.IsActive == isActive.Value);

        var term = ServiceGuard.Clean(search);
        if (term is not null)
        {
            query = query.Where(u =>
                u.FullName.Contains(term) ||
                (u.Email != null && u.Email.Contains(term)) ||
                (u.UserName != null && u.UserName.Contains(term)));
        }

        var users = await query.OrderBy(u => u.FullName).ToListAsync(cancellationToken);

        var dtos = new List<UserDto>(users.Count);
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            dtos.Add(new UserDto
            {
                Id = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                Role = roles.FirstOrDefault() ?? string.Empty,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
            });
        }

        return Result<IReadOnlyList<UserDto>>.Success(dtos);
    }

    public async Task<Result<UserDto>> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.RequireAdmin))
            return Result<UserDto>.Failure(ServiceGuard.ReadDenied);

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return Result<UserDto>.Failure("Pengguna tidak ditemukan.");

        var roles = await _userManager.GetRolesAsync(user);
        return Result<UserDto>.Success(new UserDto
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            Role = roles.FirstOrDefault() ?? string.Empty,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
        });
    }

    public async Task<Result<UserDto>> CreateAsync(
        CreateUserDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.RequireAdmin))
            return Result<UserDto>.Failure(ServiceGuard.WriteDenied);

        var errors = ServiceGuard.Validate(dto).ToList();
        if (errors.Count > 0)
            return Result<UserDto>.Failure(errors);

        var role = dto.Role?.Trim() ?? string.Empty;
        if (!Roles.All.Contains(role))
            return Result<UserDto>.Failure("Role tidak valid. Pilih salah satu: Admin, Staff, atau Viewer.");

        var email = dto.Email.Trim();
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
            return Result<UserDto>.Failure($"Email '{email}' sudah digunakan.");

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = dto.FullName.Trim(),
            PhoneNumber = ServiceGuard.Clean(dto.PhoneNumber),
            IsActive = dto.IsActive,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
        };

        var createResult = await _userManager.CreateAsync(user, dto.Password);
        if (!createResult.Succeeded)
            return Result<UserDto>.Failure(createResult.Errors.Select(e => e.Description));

        var roleResult = await _userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
            return Result<UserDto>.Failure(roleResult.Errors.Select(e => e.Description));

        // Catat audit manual tanpa kata sandi atau hash
        var newValues = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["FullName"] = user.FullName,
            ["Email"] = user.Email,
            ["PhoneNumber"] = user.PhoneNumber,
            ["Role"] = role,
            ["IsActive"] = user.IsActive,
        });

        await _auditService.LogAsync("Create", "User", user.Id, null, newValues, cancellationToken);

        return Result<UserDto>.Success(new UserDto
        {
            Id = user.Id,
            UserName = user.UserName!,
            Email = user.Email!,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            Role = role,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
        });
    }

    public async Task<Result<UserDto>> UpdateAsync(
        UpdateUserDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.RequireAdmin))
            return Result<UserDto>.Failure(ServiceGuard.WriteDenied);

        var errors = ServiceGuard.Validate(dto).ToList();
        if (errors.Count > 0)
            return Result<UserDto>.Failure(errors);

        var role = dto.Role?.Trim() ?? string.Empty;
        if (!Roles.All.Contains(role))
            return Result<UserDto>.Failure("Role tidak valid. Pilih salah satu: Admin, Staff, atau Viewer.");

        // Proteksi self-demote dan self-deactivate
        var isSelf = string.Equals(dto.Id, _currentUser.UserId, StringComparison.Ordinal);
        if (isSelf)
        {
            if (!dto.IsActive)
                return Result<UserDto>.Failure("Anda tidak dapat menonaktifkan akun Anda sendiri.");
            if (!string.Equals(role, Roles.Admin, StringComparison.Ordinal))
                return Result<UserDto>.Failure("Anda tidak dapat mengubah role akun Anda sendiri.");
        }

        var user = await _userManager.FindByIdAsync(dto.Id);
        if (user is null)
            return Result<UserDto>.Failure("Pengguna tidak ditemukan.");

        var email = dto.Email.Trim();
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await _userManager.FindByEmailAsync(email);
            if (existing is not null && existing.Id != user.Id)
                return Result<UserDto>.Failure($"Email '{email}' sudah digunakan.");
            user.Email = email;
            user.UserName = email;
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        var oldRole = currentRoles.FirstOrDefault() ?? string.Empty;

        var oldValues = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["FullName"] = user.FullName,
            ["Email"] = user.Email,
            ["PhoneNumber"] = user.PhoneNumber,
            ["Role"] = oldRole,
            ["IsActive"] = user.IsActive,
        });

        user.FullName = dto.FullName.Trim();
        user.PhoneNumber = ServiceGuard.Clean(dto.PhoneNumber);
        var wasActive = user.IsActive;
        user.IsActive = dto.IsActive;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return Result<UserDto>.Failure(updateResult.Errors.Select(e => e.Description));

        // Update SecurityStamp bila dinonaktifkan
        if (wasActive && !dto.IsActive)
            await _userManager.UpdateSecurityStampAsync(user);

        if (!string.Equals(oldRole, role, StringComparison.Ordinal))
        {
            if (currentRoles.Count > 0)
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, role);
        }

        var newValues = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["FullName"] = user.FullName,
            ["Email"] = user.Email,
            ["PhoneNumber"] = user.PhoneNumber,
            ["Role"] = role,
            ["IsActive"] = user.IsActive,
        });

        await _auditService.LogAsync("Update", "User", user.Id, oldValues, newValues, cancellationToken);

        return Result<UserDto>.Success(new UserDto
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            Role = role,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
        });
    }

    public async Task<Result> SetActiveAsync(
        string id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.RequireAdmin))
            return Result.Failure(ServiceGuard.WriteDenied);

        if (string.Equals(id, _currentUser.UserId, StringComparison.Ordinal) && !isActive)
            return Result.Failure("Anda tidak dapat menonaktifkan akun Anda sendiri.");

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return Result.Failure("Pengguna tidak ditemukan.");

        if (user.IsActive == isActive)
            return Result.Success();

        var oldActive = user.IsActive;
        user.IsActive = isActive;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return Result.Failure(updateResult.Errors.Select(e => e.Description));

        if (!isActive)
            await _userManager.UpdateSecurityStampAsync(user);

        var oldValues = JsonSerializer.Serialize(new Dictionary<string, object?> { ["IsActive"] = oldActive });
        var newValues = JsonSerializer.Serialize(new Dictionary<string, object?> { ["IsActive"] = isActive });

        await _auditService.LogAsync(
            isActive ? "Activate" : "Deactivate",
            "User",
            user.Id,
            oldValues,
            newValues,
            cancellationToken);

        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(
        ResetUserPasswordDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.RequireAdmin))
            return Result.Failure(ServiceGuard.WriteDenied);

        var errors = ServiceGuard.Validate(dto).ToList();
        if (errors.Count > 0)
            return Result.Failure(errors);

        var user = await _userManager.FindByIdAsync(dto.Id);
        if (user is null)
            return Result.Failure("Pengguna tidak ditemukan.");

        foreach (var validator in _userManager.PasswordValidators)
        {
            var valResult = await validator.ValidateAsync(_userManager, user, dto.NewPassword);
            if (!valResult.Succeeded)
                return Result.Failure(valResult.Errors.Select(e => e.Description));
        }

        if (await _userManager.HasPasswordAsync(user))
        {
            var removeResult = await _userManager.RemovePasswordAsync(user);
            if (!removeResult.Succeeded)
                return Result.Failure(removeResult.Errors.Select(e => e.Description));
        }

        var addResult = await _userManager.AddPasswordAsync(user, dto.NewPassword);
        if (!addResult.Succeeded)
            return Result.Failure(addResult.Errors.Select(e => e.Description));

        await _userManager.UpdateSecurityStampAsync(user);

        var newValues = JsonSerializer.Serialize(new Dictionary<string, object?> { ["PasswordReset"] = true });
        await _auditService.LogAsync("ResetPassword", "User", user.Id, null, newValues, cancellationToken);

        return Result.Success();
    }
}
