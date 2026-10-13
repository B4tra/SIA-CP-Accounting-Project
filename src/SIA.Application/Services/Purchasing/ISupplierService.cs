using SIA.Application.Common.Models;
using SIA.Application.DTOs.Purchasing;

namespace SIA.Application.Services.Purchasing;

/// <summary>Pemasok. Baca: semua role. Tulis: Admin, Staff.</summary>
public interface ISupplierService
{
    Task<Result<IReadOnlyList<SupplierDto>>> GetAllAsync(string? search = null, CancellationToken cancellationToken = default);

    Task<Result<SupplierDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<SupplierDto>> CreateAsync(CreateSupplierDto dto, CancellationToken cancellationToken = default);

    Task<Result<SupplierDto>> UpdateAsync(UpdateSupplierDto dto, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
