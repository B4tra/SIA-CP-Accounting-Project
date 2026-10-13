using SIA.Application.Common.Models;
using SIA.Application.DTOs.Inventory;

namespace SIA.Application.Services.Inventory;

/// <summary>Kartu stok dan peringatan stok minimum. Baca: semua role.</summary>
public interface IStockCardService
{
    Task<Result<IReadOnlyList<StockCardDto>>> GetByProductAsync(
        int productId,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        CancellationToken cancellationToken = default);

    /// <summary>Produk dengan Stock &lt;= ReorderPoint.</summary>
    Task<Result<IReadOnlyList<ReorderAlertDto>>> GetReorderAlertsAsync(CancellationToken cancellationToken = default);
}
