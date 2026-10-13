using SIA.Application.Common.Models;
using SIA.Application.DTOs.Payments;
using SIA.Application.DTOs.Purchasing;

namespace SIA.Application.Services.Purchasing;

/// <summary>
/// Purchase Order. Alur: Draft -> Approved -> Received (atau Cancelled).
/// Baca: semua role. Buat/ubah draft, terima, batal: Admin, Staff. Approve: HANYA Admin.
/// </summary>
public interface IPurchaseOrderService
{
    Task<Result<IReadOnlyList<PurchaseOrderDto>>> GetAllAsync(PurchaseOrderFilterDto filter, CancellationToken cancellationToken = default);

    Task<Result<PurchaseOrderDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Membuat PO berstatus Draft dengan nomor dari ITransactionNumberService.</summary>
    Task<Result<PurchaseOrderDto>> CreateDraftAsync(CreatePurchaseOrderDto dto, CancellationToken cancellationToken = default);

    /// <summary>Mengubah PO; hanya jika status masih Draft.</summary>
    Task<Result<PurchaseOrderDto>> UpdateDraftAsync(UpdatePurchaseOrderDto dto, CancellationToken cancellationToken = default);

    /// <summary>Draft -> Approved. Hanya Admin; mengisi ApprovedByUserId.</summary>
    Task<Result<PurchaseOrderDto>> ApproveAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Approved -> Received. Menambah stok dan menulis StockCard (In).</summary>
    Task<Result<PurchaseOrderDto>> ReceiveAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Draft/Approved -> Cancelled.</summary>
    Task<Result<PurchaseOrderDto>> CancelAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Daftar utang (PO berstatus Received yang belum lunas).</summary>
    Task<Result<IReadOnlyList<OutstandingDto>>> GetOutstandingAsync(CancellationToken cancellationToken = default);
}
