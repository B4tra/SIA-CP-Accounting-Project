using SIA.Application.Common.Models;
using SIA.Application.DTOs.Accounting;

namespace SIA.Application.Services.Accounting;

public interface IAccountService
{
    Task<Result<IReadOnlyList<AccountDto>>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<Result<AccountDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<AccountDto>> CreateAsync(CreateAccountDto dto, CancellationToken ct = default);
    Task<Result<AccountDto>> UpdateAsync(UpdateAccountDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
    Task<Result> RecalculateBalancesAsync(CancellationToken ct = default);
}
