using SIA.Application.Common.Models;
using SIA.Application.DTOs.Purchasing;

namespace SIA.Application.Services.Purchasing;

/// <summary>Retur pembelian. Baca: semua role. Buat: Admin, Staff.</summary>
public interface IPurchaseReturnService
{
    Task<Result<IReadOnlyList<PurchaseReturnDto>>> GetAllAsync(DateTime? dateFrom = null, DateTime? dateTo = null, CancellationToken cancellationToken = default);

    Task<Result<PurchaseReturnDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<PurchaseReturnDto>>> GetByPurchaseOrderAsync(int purchaseOrderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Membuat retur untuk PO berstatus Received: validasi jumlah dan stok mencukupi,
    /// mengurangi stok dan menulis StockCard (ReturnOut).
    /// </summary>
    Task<Result<PurchaseReturnDto>> CreateAsync(CreatePurchaseReturnDto dto, CancellationToken cancellationToken = default);
}
