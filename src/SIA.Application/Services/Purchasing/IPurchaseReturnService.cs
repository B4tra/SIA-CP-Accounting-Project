using SIA.Application.Common.Models;
using SIA.Application.DTOs.Purchasing;

namespace SIA.Application.Services.Purchasing;

public interface IPurchaseReturnService
{
    Task<Result<IReadOnlyList<PurchaseReturnDto>>> GetListAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default);
    Task<Result<PurchaseReturnDetailDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<PurchaseReturnDetailDto>> CreateAsync(CreatePurchaseReturnDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
}
