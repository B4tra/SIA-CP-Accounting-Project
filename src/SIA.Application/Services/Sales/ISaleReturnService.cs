using SIA.Application.Common.Models;
using SIA.Application.DTOs.Sales;

namespace SIA.Application.Services.Sales;

public interface ISaleReturnService
{
    Task<Result<IReadOnlyList<SaleReturnDto>>> GetListAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default);
    Task<Result<SaleReturnDetailDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<SaleReturnDetailDto>> CreateAsync(CreateSaleReturnDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
}
