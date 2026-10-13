using System.ComponentModel.DataAnnotations;

namespace SIA.Application.DTOs.Sales;

public class SaleReturnDto
{
    public int Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public int SaleId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Reason { get; set; }
    public List<SaleReturnDetailDto> Details { get; set; } = new();
}

public class SaleReturnDetailDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
}

/// <summary>
/// Input retur penjualan. Harga satuan diambil dari detail penjualan asal;
/// stok bertambah dengan StockCard bertipe ReturnIn.
/// </summary>
public class CreateSaleReturnDto
{
    [Required(ErrorMessage = "Tanggal retur wajib diisi.")]
    public DateTime ReturnDate { get; set; } = DateTime.Today;

    [Range(1, int.MaxValue, ErrorMessage = "Penjualan asal wajib dipilih.")]
    public int SaleId { get; set; }

    [StringLength(500, ErrorMessage = "Alasan retur maksimal 500 karakter.")]
    public string? Reason { get; set; }

    [MinLength(1, ErrorMessage = "Retur minimal memiliki 1 item.")]
    public List<CreateSaleReturnDetailDto> Details { get; set; } = new();
}

public class CreateSaleReturnDetailDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Produk wajib dipilih.")]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Jumlah retur minimal 1.")]
    public int Quantity { get; set; } = 1;
}
