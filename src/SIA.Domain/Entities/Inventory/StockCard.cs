using SIA.Domain.Common;
using SIA.Domain.Enums;

namespace SIA.Domain.Entities.Inventory;

public class StockCard : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public DateTime MovementDate { get; set; }
    public StockMovementType MovementType { get; set; }
    public int Quantity { get; set; }
    public int StockBefore { get; set; }
    public int StockAfter { get; set; }

    /// <summary>Nomor transaksi sumber, mis. "INV-2026-0001".</summary>
    public string? Reference { get; set; }
}
