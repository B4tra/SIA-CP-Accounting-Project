using SIA.Domain.Common;
using SIA.Domain.Enums;

namespace SIA.Domain.Entities.Payments;

/// <summary>
/// Pembayaran polimorfik: merujuk ke Sale atau PurchaseOrder via ReferenceType + ReferenceId (tanpa FK).
/// Integritas divalidasi di IPaymentService.
/// </summary>
public class Payment : BaseEntity
{
    public string PaymentNumber { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public int ReferenceId { get; set; }
    public ReferenceType ReferenceType { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}
