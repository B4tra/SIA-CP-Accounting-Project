using System.ComponentModel.DataAnnotations;
using SIA.Domain.Enums;

namespace SIA.Application.DTOs.Payments;

public class PaymentDto
{
    public int Id { get; set; }
    public string PaymentNumber { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public ReferenceType ReferenceType { get; set; }
    public int ReferenceId { get; set; }

    /// <summary>Nomor invoice/PO yang dibayar.</summary>
    public string? ReferenceNumber { get; set; }

    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Input pembayaran piutang (Sale) atau utang (PurchaseOrder). Keberadaan referensi dan
/// batas sisa tagihan divalidasi di IPaymentService.
/// </summary>
public class CreatePaymentDto
{
    [Required(ErrorMessage = "Tanggal pembayaran wajib diisi.")]
    public DateTime PaymentDate { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "Metode pembayaran wajib dipilih.")]
    public PaymentMethod PaymentMethod { get; set; }

    [Required(ErrorMessage = "Jenis referensi wajib dipilih.")]
    public ReferenceType ReferenceType { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Transaksi yang dibayar wajib dipilih.")]
    public int ReferenceId { get; set; }

    [Range(typeof(decimal), "0.01", "79228162514264337593543950335", ErrorMessage = "Jumlah pembayaran harus lebih dari 0.")]
    public decimal Amount { get; set; }

    [StringLength(500, ErrorMessage = "Catatan maksimal 500 karakter.")]
    public string? Notes { get; set; }
}

/// <summary>Ringkasan piutang (Sale) atau utang (PurchaseOrder) yang belum lunas.</summary>
public class OutstandingDto
{
    public ReferenceType ReferenceType { get; set; }
    public int ReferenceId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;

    /// <summary>Nama pelanggan atau pemasok.</summary>
    public string PartyName { get; set; } = string.Empty;

    public DateTime TransactionDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
}
