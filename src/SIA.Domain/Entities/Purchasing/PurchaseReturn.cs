using SIA.Domain.Common;

namespace SIA.Domain.Entities.Purchasing;

public class PurchaseReturn : BaseEntity
{
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    /// <summary>FK ke ApplicationUser (Infrastructure).</summary>
    public string UserId { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }
    public string? Reason { get; set; }

    public ICollection<PurchaseReturnDetail> Details { get; set; } = new List<PurchaseReturnDetail>();
}
