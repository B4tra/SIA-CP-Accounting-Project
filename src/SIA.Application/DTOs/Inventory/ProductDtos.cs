using System.ComponentModel.DataAnnotations;
using SIA.Domain.Enums;

namespace SIA.Application.DTOs.Inventory;

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public BatteryType? BatteryType { get; set; }
    public string? Voltage { get; set; }
    public string? Capacity { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal PurchasePrice { get; set; }
    public int Stock { get; set; }
    public int ReorderPoint { get; set; }
    public string? SKU { get; set; }
}

/// <summary>
/// Input produk. Stok TIDAK diinput di sini: stok hanya berubah lewat transaksi
/// (penjualan, penerimaan PO, retur) agar setiap perubahan tercatat di StockCard.
/// </summary>
public class CreateProductDto
{
    [Required(ErrorMessage = "Nama produk wajib diisi.")]
    [StringLength(150, ErrorMessage = "Nama produk maksimal 150 karakter.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Deskripsi maksimal 500 karakter.")]
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

    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Harga jual tidak boleh negatif.")]
    public decimal SellingPrice { get; set; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Harga beli tidak boleh negatif.")]
    public decimal PurchasePrice { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Batas minimum stok tidak boleh negatif.")]
    public int ReorderPoint { get; set; }

    [StringLength(50, ErrorMessage = "SKU maksimal 50 karakter.")]
    public string? SKU { get; set; }
}

public class UpdateProductDto : CreateProductDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Produk tidak valid.")]
    public int Id { get; set; }
}
