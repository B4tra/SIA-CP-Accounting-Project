using System.ComponentModel.DataAnnotations;
using SIA.Domain.Enums;

namespace SIA.Application.DTOs.Purchasing;

public record PurchaseOrderDto
{
    public int Id { get; init; }
    public string PONumber { get; init; } = string.Empty;
    public DateTime OrderDate { get; init; }
    public DateTime? DueDate { get; init; }
    public int SupplierId { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string? UserFullName { get; init; }
    public string? ApprovedByUserId { get; init; }
    public string? ApprovedByUserFullName { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal RemainingAmount { get; init; }
    public PurchaseOrderStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public string? Notes { get; init; }
    public int ItemCount { get; init; }
}

public record PurchaseOrderItemDto
{
    public int Id { get; init; }
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? SKU { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Subtotal { get; init; }
}

public record PurchaseOrderDetailDto
{
    public int Id { get; init; }
    public string PONumber { get; init; } = string.Empty;
    public DateTime OrderDate { get; init; }
    public DateTime? DueDate { get; init; }
    public int SupplierId { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string? UserFullName { get; init; }
    public string? ApprovedByUserId { get; init; }
    public string? ApprovedByUserFullName { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal RemainingAmount { get; init; }
    public PurchaseOrderStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<PurchaseOrderItemDto> Items { get; init; } = Array.Empty<PurchaseOrderItemDto>();
}

public class SavePurchaseOrderItemDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Produk wajib dipilih.")]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Kuantitas minimal 1.")]
    public int Quantity { get; set; } = 1;

    [Range(0, (double)decimal.MaxValue, ErrorMessage = "Harga satuan tidak boleh negatif.")]
    public decimal UnitPrice { get; set; }
}

public class SavePurchaseOrderDto
{
    [Required(ErrorMessage = "Tanggal pesanan pembelian wajib diisi.")]
    public DateTime OrderDate { get; set; } = DateTime.Today;

    /// <summary>
    /// Tanggal jatuh tempo pembayaran (opsional saat draft, wajib saat penerimaan barang bila kredit).
    /// </summary>
    public DateTime? DueDate { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Pemasok wajib dipilih.")]
    public int SupplierId { get; set; }

    [StringLength(500, ErrorMessage = "Catatan maksimal 500 karakter.")]
    public string? Notes { get; set; }

    [MinLength(1, ErrorMessage = "Pesanan pembelian minimal harus memiliki 1 item barang.")]
    public List<SavePurchaseOrderItemDto> Items { get; set; } = new();
}

public class PurchaseOrderFilterDto
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? SupplierId { get; set; }
    public PurchaseOrderStatus? Status { get; set; }
    public PaymentStatus? PaymentStatus { get; set; }
    public string? SearchTerm { get; set; }
}
