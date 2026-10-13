using SIA.Domain.Enums;

namespace SIA.Application.DTOs.Inventory;

public class StockCardDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public DateTime MovementDate { get; set; }
    public StockMovementType MovementType { get; set; }
    public int Quantity { get; set; }
    public int StockBefore { get; set; }
    public int StockAfter { get; set; }

    /// <summary>Nomor transaksi sumber, mis. "INV-2026-0001".</summary>
    public string? Reference { get; set; }
}

/// <summary>Produk yang stoknya sudah mencapai/di bawah batas minimum (ReorderPoint).</summary>
public class ReorderAlertDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? SKU { get; set; }
    public string? Brand { get; set; }
    public int Stock { get; set; }
    public int ReorderPoint { get; set; }
}
