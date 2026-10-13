using System.ComponentModel.DataAnnotations;

namespace SIA.Application.DTOs.Purchasing;

public class SupplierDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
}

public class CreateSupplierDto
{
    [Required(ErrorMessage = "Nama pemasok wajib diisi.")]
    [StringLength(100, ErrorMessage = "Nama pemasok maksimal 100 karakter.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "Nomor telepon maksimal 20 karakter.")]
    public string? Phone { get; set; }

    [StringLength(255, ErrorMessage = "Alamat maksimal 255 karakter.")]
    public string? Address { get; set; }
}

public class UpdateSupplierDto : CreateSupplierDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Pemasok tidak valid.")]
    public int Id { get; set; }
}
