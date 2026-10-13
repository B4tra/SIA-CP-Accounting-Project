using SIA.Application.Common.Models;
using SIA.Application.DTOs.Payments;
using SIA.Domain.Enums;

namespace SIA.Application.Services.Payments;

/// <summary>Pembayaran piutang/utang. Baca: semua role. Buat: Admin, Staff.</summary>
public interface IPaymentService
{
    Task<Result<IReadOnlyList<PaymentDto>>> GetAllAsync(DateTime? dateFrom = null, DateTime? dateTo = null, CancellationToken cancellationToken = default);

    Task<Result<PaymentDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<PaymentDto>>> GetByReferenceAsync(
        ReferenceType referenceType,
        int referenceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mencatat pembayaran: validasi Sale/PurchaseOrder yang dirujuk ada (referensi polimorfik tanpa FK)
    /// dan jumlah tidak melebihi sisa tagihan, lalu memperbarui PaidAmount, RemainingAmount,
    /// dan PaymentStatus (Unpaid/PartiallyPaid/Paid).
    /// </summary>
    Task<Result<PaymentDto>> CreateAsync(CreatePaymentDto dto, CancellationToken cancellationToken = default);
}
