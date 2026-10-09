using System.ComponentModel.DataAnnotations;
using SIA.Domain.Enums;

namespace SIA.Application.DTOs.Accounting;

public record AccountDto
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public AccountCategory Category { get; init; }
    public NormalBalance NormalBalance { get; init; }
    public decimal Balance { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public int? ParentAccountId { get; init; }
    public string? ParentAccountName { get; init; }
}

public class CreateAccountDto
{
    [Required(ErrorMessage = "Kode akun wajib diisi.")]
    [StringLength(20, ErrorMessage = "Kode akun maksimal 20 karakter.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nama akun wajib diisi.")]
    [StringLength(100, ErrorMessage = "Nama akun maksimal 100 karakter.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kategori akun wajib dipilih.")]
    public AccountCategory Category { get; set; }

    [Required(ErrorMessage = "Saldo normal akun wajib dipilih.")]
    public NormalBalance NormalBalance { get; set; }

    [StringLength(250, ErrorMessage = "Keterangan maksimal 250 karakter.")]
    public string? Description { get; set; }

    public int? ParentAccountId { get; set; }
}

public class UpdateAccountDto
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Kode akun wajib diisi.")]
    [StringLength(20, ErrorMessage = "Kode akun maksimal 20 karakter.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nama akun wajib diisi.")]
    [StringLength(100, ErrorMessage = "Nama akun maksimal 100 karakter.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kategori akun wajib dipilih.")]
    public AccountCategory Category { get; set; }

    [Required(ErrorMessage = "Saldo normal akun wajib dipilih.")]
    public NormalBalance NormalBalance { get; set; }

    [StringLength(250, ErrorMessage = "Keterangan maksimal 250 karakter.")]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public int? ParentAccountId { get; set; }
}
