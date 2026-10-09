using SIA.Application.Common.Models;
using SIA.Application.DTOs.Purchasing;

namespace SIA.Application.Services.Purchasing;

public interface IPurchaseOrderService
{
    Task<Result<IReadOnlyList<PurchaseOrderDto>>> GetListAsync(PurchaseOrderFilterDto filter, CancellationToken ct = default);
    Task<Result<PurchaseOrderDetailDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<PurchaseOrderDetailDto>> CreateAsync(SavePurchaseOrderDto dto, CancellationToken ct = default);
    Task<Result<PurchaseOrderDetailDto>> UpdateAsync(int id, SavePurchaseOrderDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
    Task<Result> ApproveAsync(int id, CancellationToken ct = default);
    Task<Result> ReceiveAsync(int id, DateTime? dueDate = null, CancellationToken ct = default);
    Task<Result> CancelAsync(int id, CancellationToken ct = default);
}
