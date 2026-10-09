using System.ComponentModel.DataAnnotations;

namespace SIA.Application.DTOs.Purchasing;

public record SupplierDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Address { get; init; }
}

public class SaveSupplierDto
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Nama pemasok wajib diisi.")]
    [StringLength(100, ErrorMessage = "Nama pemasok maksimal 100 karakter.")]
    public string Name { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Format nomor telepon tidak valid.")]
    [StringLength(20, ErrorMessage = "Nomor telepon maksimal 20 karakter.")]
    public string? Phone { get; set; }

    [StringLength(250, ErrorMessage = "Alamat maksimal 250 karakter.")]
    public string? Address { get; set; }
}
