using SIA.Application.Common.Models;
using SIA.Application.DTOs.Accounting;

namespace SIA.Application.Services.Accounting;

/// <summary>
/// Chart of Accounts. Baca: semua role. Tulis dan rekalkulasi saldo: Admin.
/// </summary>
public interface IAccountService
{
    Task<Result<IReadOnlyList<AccountDto>>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<Result<AccountDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<AccountDto>> CreateAsync(CreateAccountDto dto, CancellationToken cancellationToken = default);

    Task<Result<AccountDto>> UpdateAsync(UpdateAccountDto dto, CancellationToken cancellationToken = default);

    /// <summary>Soft delete; ditolak jika akun sudah dipakai di jurnal atau memiliki sub-akun.</summary>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Menghitung ulang <c>Account.Balance</c> seluruh akun dari SUM(JournalEntryLine).</summary>
    Task<Result> RecalculateBalancesAsync(CancellationToken cancellationToken = default);
}
