using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.Accounting;
using SIA.Application.Services.Accounting;
using SIA.Domain.Entities.Accounting;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Services;

/// <summary>
/// Chart of Accounts. Baca: semua role login. Tulis dan rekalkulasi saldo: Admin.
/// Saldo tidak pernah diinput manual; hanya berubah lewat posting jurnal atau rekalkulasi.
/// </summary>
public class AccountService : IAccountService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public AccountService(AppDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<AccountDto>>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<AccountDto>>.Failure(ServiceGuard.ReadDenied);

        var query = _context.Accounts.AsNoTracking().Include(a => a.ParentAccount).AsQueryable();
        if (!includeInactive)
            query = query.Where(a => a.IsActive);

        var accounts = await query.OrderBy(a => a.Code).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<AccountDto>>.Success(accounts.Select(ToDto).ToList());
    }

    public async Task<Result<AccountDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<AccountDto>.Failure(ServiceGuard.ReadDenied);

        var account = await _context.Accounts.AsNoTracking()
            .Include(a => a.ParentAccount)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

        return account is null
            ? Result<AccountDto>.Failure("Akun tidak ditemukan.")
            : Result<AccountDto>.Success(ToDto(account));
    }

    public async Task<Result<AccountDto>> CreateAsync(CreateAccountDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.RequireAdmin))
            return Result<AccountDto>.Failure(ServiceGuard.WriteDenied);

        var errors = await ValidateAsync(dto, null, cancellationToken);
        if (errors.Count > 0)
            return Result<AccountDto>.Failure(errors);

        var account = new Account
        {
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            Category = dto.Category,
            NormalBalance = dto.NormalBalance,
            Description = ServiceGuard.Clean(dto.Description),
            ParentAccountId = dto.ParentAccountId,
            Balance = 0m,
            IsActive = true,
        };

        _context.Accounts.Add(account);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(account.Id, cancellationToken);
    }

    public async Task<Result<AccountDto>> UpdateAsync(UpdateAccountDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.RequireAdmin))
            return Result<AccountDto>.Failure(ServiceGuard.WriteDenied);

        var account = await _context.Accounts.SingleOrDefaultAsync(a => a.Id == dto.Id, cancellationToken);
        if (account is null)
            return Result<AccountDto>.Failure("Akun tidak ditemukan.");

        var errors = await ValidateAsync(dto, dto.Id, cancellationToken);
        if (errors.Count > 0)
            return Result<AccountDto>.Failure(errors);

        account.Code = dto.Code.Trim();
        account.Name = dto.Name.Trim();
        account.Category = dto.Category;
        account.NormalBalance = dto.NormalBalance;
        account.Description = ServiceGuard.Clean(dto.Description);
        account.ParentAccountId = dto.ParentAccountId;
        account.IsActive = dto.IsActive;
        // Balance sengaja tidak diubah

        await _context.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(account.Id, cancellationToken);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.RequireAdmin))
            return Result.Failure(ServiceGuard.WriteDenied);

        var account = await _context.Accounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
            return Result.Failure("Akun tidak ditemukan.");

        if (await _context.JournalEntryLines.AnyAsync(l => l.AccountId == id, cancellationToken))
            return Result.Failure("Akun tidak dapat dihapus karena sudah dipakai di jurnal.");

        if (await _context.Accounts.AnyAsync(a => a.ParentAccountId == id, cancellationToken))
            return Result.Failure("Akun tidak dapat dihapus karena masih memiliki sub-akun.");

        _context.Accounts.Remove(account); // dikonversi menjadi soft delete oleh AppDbContext
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> RecalculateBalancesAsync(CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.RequireAdmin))
            return Result.Failure(ServiceGuard.WriteDenied);

        // Baris jurnal & jurnal yang terhapus diabaikan. Penjumlahan dilakukan di memori agar
        // presisi decimal terjaga di semua provider database.
        var lines = await _context.JournalEntryLines.AsNoTracking()
            .Where(l => !l.JournalEntry.IsDeleted)
            .Select(l => new { l.AccountId, l.Debit, l.Credit })
            .ToListAsync(cancellationToken);

        var totals = lines
            .GroupBy(l => l.AccountId)
            .ToDictionary(g => g.Key, g => (Debit: g.Sum(x => x.Debit), Credit: g.Sum(x => x.Credit)));

        var accounts = await _context.Accounts.ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            var (debit, credit) = totals.TryGetValue(account.Id, out var t) ? t : (0m, 0m);
            account.Balance = account.NormalBalance == NormalBalance.Debit ? debit - credit : credit - debit;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<List<string>> ValidateAsync(CreateAccountDto dto, int? currentId, CancellationToken cancellationToken)
    {
        var errors = ServiceGuard.Validate(dto).ToList();
        if (errors.Count > 0) return errors;

        if (!Enum.IsDefined(dto.Category))
            errors.Add("Kategori akun tidak valid.");
        if (!Enum.IsDefined(dto.NormalBalance))
            errors.Add("Saldo normal tidak valid.");

        var code = dto.Code.Trim();
        // Termasuk akun yang sudah dihapus (soft delete) karena kode dijaga unique index database
        var codeTaken = await _context.Accounts.IgnoreQueryFilters()
            .AnyAsync(a => a.Code == code && (currentId == null || a.Id != currentId), cancellationToken);
        if (codeTaken)
            errors.Add($"Kode akun '{code}' sudah digunakan (termasuk akun yang telah dihapus).");

        if (dto.ParentAccountId is int parentId)
        {
            if (parentId == currentId)
            {
                errors.Add("Akun induk tidak boleh akun itu sendiri.");
            }
            else if (!await _context.Accounts.AnyAsync(a => a.Id == parentId, cancellationToken))
            {
                errors.Add("Akun induk tidak ditemukan.");
            }
            else if (currentId is int id && await IsDescendantAsync(parentId, id, cancellationToken))
            {
                errors.Add("Akun induk tidak boleh merupakan sub-akun dari akun ini.");
            }
        }

        return errors;
    }

    /// <summary>True jika <paramref name="candidateId"/> berada di bawah <paramref name="ancestorId"/>.</summary>
    private async Task<bool> IsDescendantAsync(int candidateId, int ancestorId, CancellationToken cancellationToken)
    {
        var parents = await _context.Accounts.AsNoTracking()
            .Select(a => new { a.Id, a.ParentAccountId })
            .ToDictionaryAsync(a => a.Id, a => a.ParentAccountId, cancellationToken);

        var visited = new HashSet<int>();
        int? current = candidateId;
        while (current is int c && visited.Add(c))
        {
            if (c == ancestorId) return true;
            current = parents.TryGetValue(c, out var p) ? p : null;
        }
        return false;
    }

    private static AccountDto ToDto(Account a) => new()
    {
        Id = a.Id,
        Code = a.Code,
        Name = a.Name,
        Category = a.Category,
        NormalBalance = a.NormalBalance,
        Balance = a.Balance,
        Description = a.Description,
        IsActive = a.IsActive,
        ParentAccountId = a.ParentAccountId,
        ParentAccountName = a.ParentAccount?.Name,
    };
}
