using SIA.Domain.Common;
using SIA.Domain.Entities.Inventory;

namespace SIA.Domain.Entities.Purchasing;

public class PurchaseReturnDetail : BaseEntity
{
    public int PurchaseReturnId { get; set; }
    public PurchaseReturn PurchaseReturn { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
}
