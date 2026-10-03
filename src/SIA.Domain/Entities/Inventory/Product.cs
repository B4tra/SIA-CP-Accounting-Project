using SIA.Domain.Common;
using SIA.Domain.Entities.Purchasing;
using SIA.Domain.Entities.Sales;
using SIA.Domain.Enums;

namespace SIA.Domain.Entities.Inventory;

public class Product : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Contoh: "Aki Motor", "Aki Mobil".</summary>
    public string? Category { get; set; }

    // Atribut khusus aki
    public string? Brand { get; set; }
    public BatteryType? BatteryType { get; set; }
    public string? Voltage { get; set; }
    public string? Capacity { get; set; }

    public decimal SellingPrice { get; set; }
    public decimal PurchasePrice { get; set; }
    public int Stock { get; set; }

    /// <summary>Batas minimum stok sebelum alert.</summary>
    public int ReorderPoint { get; set; }

    public string? SKU { get; set; }

    public ICollection<StockCard> StockCards { get; set; } = new List<StockCard>();
    public ICollection<SaleDetail> SaleDetails { get; set; } = new List<SaleDetail>();
    public ICollection<PurchaseOrderDetail> PurchaseOrderDetails { get; set; } = new List<PurchaseOrderDetail>();
}
