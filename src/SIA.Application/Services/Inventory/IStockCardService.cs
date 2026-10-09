using SIA.Application.Common.Models;
using SIA.Application.DTOs.Inventory;

namespace SIA.Application.Services.Inventory;

public interface IStockCardService
{
    Task<Result<IReadOnlyList<StockCardDto>>> GetByProductAsync(
        int productId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<ReorderAlertDto>>> GetReorderAlertsAsync(CancellationToken ct = default);
}
