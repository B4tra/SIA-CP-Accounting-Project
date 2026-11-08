using SIA.Application.Common.Models;
using SIA.Application.DTOs.Payments;
using SIA.Domain.Enums;

namespace SIA.Application.Services.Payments;

/// <summary>Pembayaran piutang/utang. Baca: semua role. Buat: Admin, Staff. Hapus: Admin.</summary>
public interface IPaymentService
{
    Task<Result<IReadOnlyList<PaymentDto>>> GetAllAsync(DateTime? dateFrom = null, DateTime? dateTo = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mengambil riwayat pembayaran dengan filter pencarian, rentang tanggal, jenis referensi, dan metode pembayaran.
    /// Memerlukan hak akses ViewAllData.
    /// </summary>
    Task<Result<IReadOnlyList<PaymentDto>>> GetAllAsync(PaymentFilterDto filter, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Mengambil daftar piutang penjualan dan utang PO yang belum lunas (RemainingAmount > 0).
    /// Untuk PO, hanya menyertakan status Approved atau Received.
    /// Mendukung filter pencarian, jenis referensi, dan hanya yang jatuh tempo (OnlyOverdue).
    /// Memerlukan hak akses ViewAllData.
    /// </summary>
    Task<Result<IReadOnlyList<OutstandingDto>>> GetOutstandingAsync(OutstandingFilterDto? filter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Menghapus pembayaran (soft delete) dan memulihkan PaidAmount, RemainingAmount, dan PaymentStatus
    /// pada transaksi referensi (Sale atau PurchaseOrder). Hanya diizinkan untuk Admin (RequireAdmin).
    /// </summary>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
