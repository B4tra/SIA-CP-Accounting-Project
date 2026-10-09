using SIA.Domain.Enums;

namespace SIA.Application.DTOs.Inventory;

public record StockCardDto
{
    public int Id { get; init; }
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? SKU { get; init; }
    public DateTime MovementDate { get; init; }
    public StockMovementType MovementType { get; init; }
    public int Quantity { get; init; }
    public int StockBefore { get; init; }
    public int StockAfter { get; init; }
    public string? Reference { get; init; }
}

public record ReorderAlertDto
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? SKU { get; init; }
    public string? Brand { get; init; }
    public int CurrentStock { get; init; }
    public int ReorderPoint { get; init; }
    public int Deficit => ReorderPoint - CurrentStock;
}
