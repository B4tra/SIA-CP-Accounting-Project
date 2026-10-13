using System.ComponentModel.DataAnnotations;

namespace SIA.Application.DTOs.Purchasing;

public class PurchaseReturnDto
{
    public int Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public int PurchaseOrderId { get; set; }
    public string PONumber { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Reason { get; set; }
    public List<PurchaseReturnDetailDto> Details { get; set; } = new();
}

public class PurchaseReturnDetailDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
}

/// <summary>
/// Input retur pembelian (hanya untuk PO berstatus Received). Harga satuan diambil dari detail PO asal;
/// stok berkurang dengan StockCard bertipe ReturnOut.
/// </summary>
public class CreatePurchaseReturnDto
{
    [Required(ErrorMessage = "Tanggal retur wajib diisi.")]
    public DateTime ReturnDate { get; set; } = DateTime.Today;

    [Range(1, int.MaxValue, ErrorMessage = "PO asal wajib dipilih.")]
    public int PurchaseOrderId { get; set; }

    [StringLength(500, ErrorMessage = "Alasan retur maksimal 500 karakter.")]
    public string? Reason { get; set; }

    [MinLength(1, ErrorMessage = "Retur minimal memiliki 1 item.")]
    public List<CreatePurchaseReturnDetailDto> Details { get; set; } = new();
}

public class CreatePurchaseReturnDetailDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Produk wajib dipilih.")]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Jumlah retur minimal 1.")]
    public int Quantity { get; set; } = 1;
}
