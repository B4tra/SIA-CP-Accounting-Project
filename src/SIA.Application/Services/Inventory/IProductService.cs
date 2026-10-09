using SIA.Application.Common.Models;
using SIA.Application.DTOs.Inventory;

namespace SIA.Application.Services.Inventory;

public interface IProductService
{
    Task<Result<IReadOnlyList<ProductDto>>> GetAllAsync(ProductFilterDto filter, CancellationToken ct = default);
    Task<Result<ProductDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<ProductDto>> CreateAsync(SaveProductDto dto, CancellationToken ct = default);
    Task<Result<ProductDto>> UpdateAsync(int id, SaveProductDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
}
