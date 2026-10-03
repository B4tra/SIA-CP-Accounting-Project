using SIA.Domain.Common;
using SIA.Domain.Enums;

namespace SIA.Domain.Entities.Purchasing;

/// <summary>
/// Purchase Order. Alur status: Draft -> Approved -> Received (atau Cancelled).
/// </summary>
public class PurchaseOrder : BaseEntity
{
    public string PONumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }

    /// <summary>Tanggal jatuh tempo; wajib diisi jika transaksi kredit.</summary>
    public DateTime? DueDate { get; set; }

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    /// <summary>FK ke ApplicationUser (Infrastructure) — pembuat PO.</summary>
    public string UserId { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public PurchaseOrderStatus Status { get; set; }

    /// <summary>FK ke ApplicationUser (Infrastructure) — yang meng-approve (nullable).</summary>
    public string? ApprovedByUserId { get; set; }

    public PaymentStatus PaymentStatus { get; set; }
    public string? Notes { get; set; }

    public ICollection<PurchaseOrderDetail> Details { get; set; } = new List<PurchaseOrderDetail>();
    public ICollection<PurchaseReturn> Returns { get; set; } = new List<PurchaseReturn>();
}
