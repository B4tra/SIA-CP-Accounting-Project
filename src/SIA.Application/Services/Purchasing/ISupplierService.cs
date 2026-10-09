using SIA.Application.Common.Models;
using SIA.Application.DTOs.Purchasing;

namespace SIA.Application.Services.Purchasing;

public interface ISupplierService
{
    Task<Result<IReadOnlyList<SupplierDto>>> GetAllAsync(string? searchTerm = null, CancellationToken ct = default);
    Task<Result<SupplierDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<SupplierDto>> CreateAsync(SaveSupplierDto dto, CancellationToken ct = default);
    Task<Result<SupplierDto>> UpdateAsync(int id, SaveSupplierDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
}
