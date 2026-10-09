using SIA.Application.Common.Models;
using SIA.Application.DTOs.Payments;
using SIA.Domain.Enums;

namespace SIA.Application.Services.Payments;

public interface IPaymentService
{
    Task<Result<IReadOnlyList<PaymentDto>>> GetListAsync(PaymentFilterDto filter, CancellationToken ct = default);
    Task<Result<PaymentDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<PaymentDto>> CreateAsync(CreatePaymentDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
    Task<Result<IReadOnlyList<OutstandingDto>>> GetOutstandingAsync(ReferenceType referenceType, CancellationToken ct = default);
}
