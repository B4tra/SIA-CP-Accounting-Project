using SIA.Domain.Common;

namespace SIA.Domain.Entities.Sales;

public class SaleReturn : BaseEntity
{
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate { get; set; }
    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;

    /// <summary>FK ke ApplicationUser (Infrastructure).</summary>
    public string UserId { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }
    public string? Reason { get; set; }

    public ICollection<SaleReturnDetail> Details { get; set; } = new List<SaleReturnDetail>();
}
