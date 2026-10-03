using SIA.Domain.Common;
using SIA.Domain.Entities.Inventory;

namespace SIA.Domain.Entities.Sales;

public class SaleDetail : BaseEntity
{
    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
}
