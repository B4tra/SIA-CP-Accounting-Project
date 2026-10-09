using System.ComponentModel.DataAnnotations;
using SIA.Domain.Enums;

namespace SIA.Application.DTOs.Sales;

public record SaleDto
{
    public int Id { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateTime SaleDate { get; init; }
    public DateTime? DueDate { get; init; }
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string? UserFullName { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal RemainingAmount { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public string? Notes { get; init; }
    public int ItemCount { get; init; }
}

public record SaleItemDto
{
    public int Id { get; init; }
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? SKU { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Subtotal { get; init; }
}

public record SaleDetailDto
{
    public int Id { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateTime SaleDate { get; init; }
    public DateTime? DueDate { get; init; }
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string? UserFullName { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal RemainingAmount { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<SaleItemDto> Items { get; init; } = Array.Empty<SaleItemDto>();
}

public class CreateSaleItemDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Produk wajib dipilih.")]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Kuantitas minimal 1.")]
    public int Quantity { get; set; } = 1;

    [Range(0, (double)decimal.MaxValue, ErrorMessage = "Harga satuan tidak boleh negatif.")]
    public decimal UnitPrice { get; set; }
}

public class CreateSaleDto
{
    [Required(ErrorMessage = "Tanggal penjualan wajib diisi.")]
    public DateTime SaleDate { get; set; } = DateTime.Today;

    /// <summary>
    /// Wajib diisi jika transaksi kredit (PaidAmount &lt; TotalAmount).
    /// </summary>
    public DateTime? DueDate { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Pelanggan wajib dipilih.")]
    public int CustomerId { get; set; }

    [Required(ErrorMessage = "Metode pembayaran wajib dipilih.")]
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    [Range(0, (double)decimal.MaxValue, ErrorMessage = "Jumlah bayar awal tidak boleh negatif.")]
    public decimal PaidAmount { get; set; }

    [StringLength(500, ErrorMessage = "Catatan maksimal 500 karakter.")]
    public string? Notes { get; set; }

    [MinLength(1, ErrorMessage = "Penjualan minimal harus memiliki 1 item barang.")]
    public List<CreateSaleItemDto> Items { get; set; } = new();
}

public class SaleFilterDto
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? CustomerId { get; set; }
    public PaymentStatus? PaymentStatus { get; set; }
    public string? SearchTerm { get; set; }
}
