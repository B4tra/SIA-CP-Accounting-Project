using SIA.Application.Common.Models;
using SIA.Application.DTOs.Sales;

namespace SIA.Application.Services.Sales;

public interface ISaleService
{
    Task<Result<IReadOnlyList<SaleDto>>> GetListAsync(SaleFilterDto filter, CancellationToken ct = default);
    Task<Result<SaleDetailDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<SaleDetailDto>> CreateAsync(CreateSaleDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
}
