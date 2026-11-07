using System.ComponentModel.DataAnnotations;
using SIA.Application.Common.Constants;

namespace SIA.Application.DTOs.System;

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateUserDto
{
    [Required(ErrorMessage = "Nama lengkap wajib diisi.")]
    [StringLength(100, ErrorMessage = "Nama lengkap maksimal 100 karakter.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email wajib diisi.")]
    [EmailAddress(ErrorMessage = "Format email tidak valid.")]
    [StringLength(256, ErrorMessage = "Email maksimal 256 karakter.")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Format nomor telepon tidak valid.")]
    [StringLength(20, ErrorMessage = "Nomor telepon maksimal 20 karakter.")]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "Role wajib dipilih.")]
    public string Role { get; set; } = Roles.Staff;

    [Required(ErrorMessage = "Kata sandi wajib diisi.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Kata sandi minimal 8 karakter.")]
    public string Password { get; set; } = string.Empty;

    [Compare(nameof(Password), ErrorMessage = "Konfirmasi kata sandi tidak cocok.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public class UpdateUserDto
{
    [Required(ErrorMessage = "ID pengguna tidak valid.")]
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nama lengkap wajib diisi.")]
    [StringLength(100, ErrorMessage = "Nama lengkap maksimal 100 karakter.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email wajib diisi.")]
    [EmailAddress(ErrorMessage = "Format email tidak valid.")]
    [StringLength(256, ErrorMessage = "Email maksimal 256 karakter.")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Format nomor telepon tidak valid.")]
    [StringLength(20, ErrorMessage = "Nomor telepon maksimal 20 karakter.")]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "Role wajib dipilih.")]
    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public class ResetUserPasswordDto
{
    [Required(ErrorMessage = "ID pengguna tidak valid.")]
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kata sandi baru wajib diisi.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Kata sandi baru minimal 8 karakter.")]
    public string NewPassword { get; set; } = string.Empty;

    [Compare(nameof(NewPassword), ErrorMessage = "Konfirmasi kata sandi tidak cocok.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
