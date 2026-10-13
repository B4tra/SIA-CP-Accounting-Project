using System.ComponentModel.DataAnnotations;
using SIA.Domain.Enums;

namespace SIA.Application.DTOs.Accounting;

public class AccountDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountCategory Category { get; set; }
    public NormalBalance NormalBalance { get; set; }
    public decimal Balance { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int? ParentAccountId { get; set; }
    public string? ParentAccountName { get; set; }
}

/// <summary>Saldo tidak diinput; saldo hanya berubah lewat posting jurnal atau rekalkulasi.</summary>
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

    [Required(ErrorMessage = "Saldo normal wajib dipilih.")]
    public NormalBalance NormalBalance { get; set; }

    [StringLength(255, ErrorMessage = "Deskripsi maksimal 255 karakter.")]
    public string? Description { get; set; }

    public int? ParentAccountId { get; set; }
}

public class UpdateAccountDto : CreateAccountDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Akun tidak valid.")]
    public int Id { get; set; }

    public bool IsActive { get; set; } = true;
}
