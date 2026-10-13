using SIA.Application.Common.Models;
using SIA.Application.DTOs.Sales;

namespace SIA.Application.Services.Sales;

/// <summary>Pelanggan. Baca: semua role. Tulis: Admin, Staff.</summary>
public interface ICustomerService
{
    Task<Result<IReadOnlyList<CustomerDto>>> GetAllAsync(string? search = null, CancellationToken cancellationToken = default);

    Task<Result<CustomerDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<CustomerDto>> CreateAsync(CreateCustomerDto dto, CancellationToken cancellationToken = default);

    Task<Result<CustomerDto>> UpdateAsync(UpdateCustomerDto dto, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
