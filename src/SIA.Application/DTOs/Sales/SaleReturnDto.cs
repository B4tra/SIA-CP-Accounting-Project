using System.ComponentModel.DataAnnotations;

namespace SIA.Application.DTOs.Sales;

public record SaleReturnDto
{
    public int Id { get; init; }
    public string ReturnNumber { get; init; } = string.Empty;
    public DateTime ReturnDate { get; init; }
    public int SaleId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string? UserFullName { get; init; }
    public decimal TotalAmount { get; init; }
    public string? Reason { get; init; }
    public int ItemCount { get; init; }
}

public record SaleReturnItemDto
{
    public int Id { get; init; }
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? SKU { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Subtotal { get; init; }
}

public record SaleReturnDetailDto
{
    public int Id { get; init; }
    public string ReturnNumber { get; init; } = string.Empty;
    public DateTime ReturnDate { get; init; }
    public int SaleId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string? UserFullName { get; init; }
    public decimal TotalAmount { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<SaleReturnItemDto> Items { get; init; } = Array.Empty<SaleReturnItemDto>();
}

public class CreateSaleReturnItemDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Produk wajib dipilih.")]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Kuantitas retur minimal 1.")]
    public int Quantity { get; set; } = 1;

    [Range(0, (double)decimal.MaxValue, ErrorMessage = "Harga satuan tidak boleh negatif.")]
    public decimal UnitPrice { get; set; }
}

public class CreateSaleReturnDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Penjualan sumber wajib dipilih.")]
    public int SaleId { get; set; }

    [Required(ErrorMessage = "Tanggal retur wajib diisi.")]
    public DateTime ReturnDate { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "Alasan retur wajib diisi.")]
    [StringLength(250, ErrorMessage = "Alasan retur maksimal 250 karakter.")]
    public string Reason { get; set; } = string.Empty;

    [MinLength(1, ErrorMessage = "Retur penjualan minimal harus memiliki 1 item barang.")]
    public List<CreateSaleReturnItemDto> Items { get; set; } = new();
}
