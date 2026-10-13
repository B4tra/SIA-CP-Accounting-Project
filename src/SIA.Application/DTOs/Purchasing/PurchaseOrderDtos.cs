using System.ComponentModel.DataAnnotations;
using SIA.Domain.Enums;

namespace SIA.Application.DTOs.Purchasing;

public class PurchaseOrderDto
{
    public int Id { get; set; }
    public string PONumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? DueDate { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public PurchaseOrderStatus Status { get; set; }
    public string? ApprovedByUserId { get; set; }
    public string? ApprovedByUserName { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public string? Notes { get; set; }
    public List<PurchaseOrderDetailDto> Details { get; set; } = new();
}

public class PurchaseOrderDetailDto
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
/// Input PO baru (status Draft). Nomor PO dibuat oleh ITransactionNumberService.
/// PO dianggap kredit bila <see cref="DueDate"/> diisi.
/// </summary>
public class CreatePurchaseOrderDto
{
    [Required(ErrorMessage = "Tanggal order wajib diisi.")]
    public DateTime OrderDate { get; set; } = DateTime.Today;

    /// <summary>Tanggal jatuh tempo; diisi jika pembelian kredit.</summary>
    public DateTime? DueDate { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Pemasok wajib dipilih.")]
    public int SupplierId { get; set; }

    [StringLength(500, ErrorMessage = "Catatan maksimal 500 karakter.")]
    public string? Notes { get; set; }

    [MinLength(1, ErrorMessage = "PO minimal memiliki 1 item.")]
    public List<CreatePurchaseOrderDetailDto> Details { get; set; } = new();
}

/// <summary>Perubahan PO hanya diizinkan selama status masih Draft.</summary>
public class UpdatePurchaseOrderDto : CreatePurchaseOrderDto
{
    [Range(1, int.MaxValue, ErrorMessage = "PO tidak valid.")]
    public int Id { get; set; }
}

public class CreatePurchaseOrderDetailDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Produk wajib dipilih.")]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Jumlah minimal 1.")]
    public int Quantity { get; set; } = 1;

    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Harga satuan tidak boleh negatif.")]
    public decimal UnitPrice { get; set; }
}

public class PurchaseOrderFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? SupplierId { get; set; }
    public PurchaseOrderStatus? Status { get; set; }
    public PaymentStatus? PaymentStatus { get; set; }

    /// <summary>Pencarian pada nomor PO atau nama pemasok.</summary>
    public string? Search { get; set; }
}
