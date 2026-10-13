using System.ComponentModel.DataAnnotations;

namespace SIA.Application.DTOs.Sales;

public class CustomerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
}

public class CreateCustomerDto
{
    [Required(ErrorMessage = "Nama pelanggan wajib diisi.")]
    [StringLength(100, ErrorMessage = "Nama pelanggan maksimal 100 karakter.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "Nomor telepon maksimal 20 karakter.")]
    public string? Phone { get; set; }

    [StringLength(255, ErrorMessage = "Alamat maksimal 255 karakter.")]
    public string? Address { get; set; }
}

public class UpdateCustomerDto : CreateCustomerDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Pelanggan tidak valid.")]
    public int Id { get; set; }
}
