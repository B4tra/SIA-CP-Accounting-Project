using System.ComponentModel.DataAnnotations;
using SIA.Domain.Enums;

namespace SIA.Application.DTOs.Sales;

public class SaleDto
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime SaleDate { get; set; }
    public DateTime? DueDate { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public string? Notes { get; set; }
    public List<SaleDetailDto> Details { get; set; } = new();
}

public class SaleDetailDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? SKU { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
}

/// <summary>
/// Input penjualan. Nomor invoice dibuat oleh ITransactionNumberService.
/// Jika <see cref="PaidAmount"/> &lt; total, transaksi dianggap kredit dan <see cref="DueDate"/> wajib diisi
/// (divalidasi di ISaleService).
/// </summary>
public class CreateSaleDto
{
    [Required(ErrorMessage = "Tanggal penjualan wajib diisi.")]
    public DateTime SaleDate { get; set; } = DateTime.Today;

    /// <summary>Tanggal jatuh tempo; wajib jika transaksi kredit.</summary>
    public DateTime? DueDate { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Pelanggan wajib dipilih.")]
    public int CustomerId { get; set; }

    [Required(ErrorMessage = "Metode pembayaran wajib dipilih.")]
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>Uang yang dibayar saat transaksi.</summary>
    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Jumlah dibayar tidak boleh negatif.")]
    public decimal PaidAmount { get; set; }

    [StringLength(500, ErrorMessage = "Catatan maksimal 500 karakter.")]
    public string? Notes { get; set; }

    [MinLength(1, ErrorMessage = "Penjualan minimal memiliki 1 item.")]
    public List<CreateSaleDetailDto> Details { get; set; } = new();
}

public class CreateSaleDetailDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Produk wajib dipilih.")]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Jumlah minimal 1.")]
    public int Quantity { get; set; } = 1;

    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Harga satuan tidak boleh negatif.")]
    public decimal UnitPrice { get; set; }
}

public class SaleFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? CustomerId { get; set; }
    public PaymentStatus? PaymentStatus { get; set; }

    /// <summary>Pencarian pada nomor invoice atau nama pelanggan.</summary>
    public string? Search { get; set; }
}
