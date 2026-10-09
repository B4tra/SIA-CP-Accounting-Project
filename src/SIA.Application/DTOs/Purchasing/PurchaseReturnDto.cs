using System.ComponentModel.DataAnnotations;

namespace SIA.Application.DTOs.Purchasing;

public record PurchaseReturnDto
{
    public int Id { get; init; }
    public string ReturnNumber { get; init; } = string.Empty;
    public DateTime ReturnDate { get; init; }
    public int PurchaseOrderId { get; init; }
    public string PONumber { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string? UserFullName { get; init; }
    public decimal TotalAmount { get; init; }
    public string? Reason { get; init; }
    public int ItemCount { get; init; }
}

public record PurchaseReturnItemDto
{
    public int Id { get; init; }
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? SKU { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Subtotal { get; init; }
}

public record PurchaseReturnDetailDto
{
    public int Id { get; init; }
    public string ReturnNumber { get; init; } = string.Empty;
    public DateTime ReturnDate { get; init; }
    public int PurchaseOrderId { get; init; }
    public string PONumber { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string? UserFullName { get; init; }
    public decimal TotalAmount { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<PurchaseReturnItemDto> Items { get; init; } = Array.Empty<PurchaseReturnItemDto>();
}

public class CreatePurchaseReturnItemDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Produk wajib dipilih.")]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Kuantitas retur minimal 1.")]
    public int Quantity { get; set; } = 1;

    [Range(0, (double)decimal.MaxValue, ErrorMessage = "Harga satuan tidak boleh negatif.")]
    public decimal UnitPrice { get; set; }
}

public class CreatePurchaseReturnDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Pesanan pembelian sumber wajib dipilih.")]
    public int PurchaseOrderId { get; set; }

    [Required(ErrorMessage = "Tanggal retur wajib diisi.")]
    public DateTime ReturnDate { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "Alasan retur wajib diisi.")]
    [StringLength(250, ErrorMessage = "Alasan retur maksimal 250 karakter.")]
    public string Reason { get; set; } = string.Empty;

    [MinLength(1, ErrorMessage = "Retur pembelian minimal harus memiliki 1 item barang.")]
    public List<CreatePurchaseReturnItemDto> Items { get; set; } = new();
}
