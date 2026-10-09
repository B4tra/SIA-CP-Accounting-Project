using System.ComponentModel.DataAnnotations;
using SIA.Domain.Enums;

namespace SIA.Application.DTOs.Inventory;

public record ProductDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Category { get; init; }
    public string? Brand { get; init; }
    public BatteryType? BatteryType { get; init; }
    public string? Voltage { get; init; }
    public string? Capacity { get; init; }
    public decimal SellingPrice { get; init; }
    public decimal PurchasePrice { get; init; }
    public int Stock { get; init; }
    public int ReorderPoint { get; init; }
    public string? SKU { get; init; }
}

public class SaveProductDto
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Nama produk wajib diisi.")]
    [StringLength(100, ErrorMessage = "Nama produk maksimal 100 karakter.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(250, ErrorMessage = "Deskripsi maksimal 250 karakter.")]
    public string? Description { get; set; }

    [StringLength(50, ErrorMessage = "Kategori maksimal 50 karakter.")]
    public string? Category { get; set; }

    [StringLength(50, ErrorMessage = "Merek maksimal 50 karakter.")]
    public string? Brand { get; set; }

    public BatteryType? BatteryType { get; set; }

    [StringLength(20, ErrorMessage = "Tegangan maksimal 20 karakter.")]
    public string? Voltage { get; set; }

    [StringLength(20, ErrorMessage = "Kapasitas maksimal 20 karakter.")]
    public string? Capacity { get; set; }

    [Range(0, (double)decimal.MaxValue, ErrorMessage = "Harga jual tidak boleh negatif.")]
    public decimal SellingPrice { get; set; }

    [Range(0, (double)decimal.MaxValue, ErrorMessage = "Harga beli tidak boleh negatif.")]
    public decimal PurchasePrice { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Titik pemesanan ulang (reorder point) tidak boleh negatif.")]
    public int ReorderPoint { get; set; }

    [StringLength(50, ErrorMessage = "SKU maksimal 50 karakter.")]
    public string? SKU { get; set; }
}

public class ProductFilterDto
{
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public BatteryType? BatteryType { get; set; }
    public string? SearchTerm { get; set; }
    public bool? IsLowStock { get; set; }
}
