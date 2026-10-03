using SIA.Domain.Common;
using SIA.Domain.Enums;

namespace SIA.Domain.Entities.Sales;

public class Sale : BaseEntity
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime SaleDate { get; set; }

    /// <summary>Tanggal jatuh tempo; wajib diisi jika transaksi kredit.</summary>
    public DateTime? DueDate { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>FK ke ApplicationUser (Infrastructure).</summary>
    public string UserId { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public string? Notes { get; set; }

    public ICollection<SaleDetail> Details { get; set; } = new List<SaleDetail>();
    public ICollection<SaleReturn> Returns { get; set; } = new List<SaleReturn>();
}
