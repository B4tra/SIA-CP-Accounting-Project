using SIA.Application.Common.Models;
using SIA.Application.DTOs.System;

namespace SIA.Application.Services.System;

/// <summary>
/// Layanan manajemen pengguna untuk Admin.
/// Operasi: daftar user, detail, tambah, ubah profil & role, aktif/nonaktif, dan reset kata sandi.
/// Seluruh metode wajib menegakkan otorisasi server-side Policies.RequireAdmin.
/// </summary>
public interface IUserService
{
    Task<Result<IReadOnlyList<UserDto>>> GetAllAsync(
        string? search = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default);

    Task<Result<UserDto>> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<Result<UserDto>> CreateAsync(
        CreateUserDto dto,
        CancellationToken cancellationToken = default);

    Task<Result<UserDto>> UpdateAsync(
        UpdateUserDto dto,
        CancellationToken cancellationToken = default);

    Task<Result> SetActiveAsync(
        string id,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<Result> ResetPasswordAsync(
        ResetUserPasswordDto dto,
        CancellationToken cancellationToken = default);
}
