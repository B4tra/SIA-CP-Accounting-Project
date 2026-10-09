using SIA.Application.Common.Models;
using SIA.Application.DTOs.Sales;

namespace SIA.Application.Services.Sales;

public interface ICustomerService
{
    Task<Result<IReadOnlyList<CustomerDto>>> GetAllAsync(string? searchTerm = null, CancellationToken ct = default);
    Task<Result<CustomerDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<CustomerDto>> CreateAsync(SaveCustomerDto dto, CancellationToken ct = default);
    Task<Result<CustomerDto>> UpdateAsync(int id, SaveCustomerDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
}
