using System.ComponentModel.DataAnnotations;
using SIA.Domain.Enums;

namespace SIA.Application.DTOs.Payments;

public record PaymentDto
{
    public int Id { get; init; }
    public string PaymentNumber { get; init; } = string.Empty;
    public DateTime PaymentDate { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
    public int ReferenceId { get; init; }
    public ReferenceType ReferenceType { get; init; }
    public string? ReferenceNumber { get; init; }
    public decimal Amount { get; init; }
    public string? Notes { get; init; }
}

public class CreatePaymentDto
{
    [Required(ErrorMessage = "Tanggal pembayaran wajib diisi.")]
    public DateTime PaymentDate { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "Metode pembayaran wajib dipilih.")]
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    [Range(1, int.MaxValue, ErrorMessage = "Referensi transaksi wajib dipilih.")]
    public int ReferenceId { get; set; }

    [Required(ErrorMessage = "Jenis referensi (Sale / PurchaseOrder) wajib dipilih.")]
    public ReferenceType ReferenceType { get; set; }

    [Range(1, (double)decimal.MaxValue, ErrorMessage = "Jumlah pembayaran harus lebih dari 0.")]
    public decimal Amount { get; set; }

    [StringLength(250, ErrorMessage = "Catatan maksimal 250 karakter.")]
    public string? Notes { get; set; }
}

public class PaymentFilterDto
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public ReferenceType? ReferenceType { get; set; }
    public string? SearchTerm { get; set; }
}

public record OutstandingDto
{
    public int ReferenceId { get; init; }
    public ReferenceType ReferenceType { get; init; }
    public string ReferenceNumber { get; init; } = string.Empty;
    public DateTime TransactionDate { get; init; }
    public DateTime? DueDate { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal RemainingAmount { get; init; }
    public string CustomerOrSupplierName { get; init; } = string.Empty;
}
