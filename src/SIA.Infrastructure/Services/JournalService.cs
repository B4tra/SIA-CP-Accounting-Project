using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.Accounting;
using SIA.Application.Services.Accounting;
using SIA.Application.Services.System;
using SIA.Domain.Entities.Accounting;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Services;

/// <summary>
/// Layanan Jurnal Umum yang diinput manual.
/// Otorisasi:
/// - Lihat & Input Jurnal: Admin dan Viewer (Policies.InputJournal)
/// - Hapus Jurnal: Admin saja (Policies.RequireAdmin)
/// </summary>
public class JournalService : IJournalService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITransactionNumberService _transactionNumbers;

    public JournalService(
        AppDbContext context,
        ICurrentUserService currentUser,
        ITransactionNumberService transactionNumbers)
    {
        _context = context;
        _currentUser = currentUser;
        _transactionNumbers = transactionNumbers;
    }

    public async Task<Result<IReadOnlyList<JournalEntryDto>>> GetAllAsync(JournalFilterDto filter, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.InputJournal))
            return Result<IReadOnlyList<JournalEntryDto>>.Failure(ServiceGuard.ReadDenied);

        filter ??= new JournalFilterDto();
        var query = _context.JournalEntries
            .AsNoTracking()
            .Include(j => j.Lines)
                .ThenInclude(l => l.Account)
            .AsQueryable();

        if (filter.DateFrom is not null)
            query = query.Where(j => j.TransactionDate >= filter.DateFrom.Value.Date);

        if (filter.DateTo is not null)
        {
            var end = filter.DateTo.Value.Date.AddDays(1);
            query = query.Where(j => j.TransactionDate < end);
        }

        if (filter.AccountId is not null)
            query = query.Where(j => j.Lines.Any(l => l.AccountId == filter.AccountId.Value));

        var term = ServiceGuard.Clean(filter.Search);
        if (term is not null)
        {
            var pattern = $"%{term}%";
            query = query.Where(j =>
                EF.Functions.Like(j.JournalNumber, pattern) ||
                EF.Functions.Like(j.Description, pattern));
        }

        var journals = await query
            .OrderByDescending(j => j.TransactionDate)
            .ThenByDescending(j => j.Id)
            .ToListAsync(cancellationToken);

        var userNames = await GetUserNamesAsync(journals.Select(j => j.UserId), cancellationToken);
        return Result<IReadOnlyList<JournalEntryDto>>.Success(journals.Select(j => ToDto(j, userNames)).ToList());
    }

    public async Task<Result<JournalEntryDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.InputJournal))
            return Result<JournalEntryDto>.Failure(ServiceGuard.ReadDenied);

        var journal = await _context.JournalEntries
            .AsNoTracking()
            .Include(j => j.Lines)
                .ThenInclude(l => l.Account)
            .SingleOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (journal is null)
            return Result<JournalEntryDto>.Failure("Jurnal tidak ditemukan.");

        var userNames = await GetUserNamesAsync(new[] { journal.UserId }, cancellationToken);
        return Result<JournalEntryDto>.Success(ToDto(journal, userNames));
    }

    public async Task<Result<JournalEntryDto>> CreateAsync(CreateJournalEntryDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.InputJournal))
            return Result<JournalEntryDto>.Failure(ServiceGuard.WriteDenied);

        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            return Result<JournalEntryDto>.Failure("User yang sedang login tidak dikenali.");

        if (dto is null)
            return Result<JournalEntryDto>.Failure("Data jurnal tidak boleh kosong.");

        var errors = ServiceGuard.Validate(dto).ToList();
        if (dto.Lines == null || dto.Lines.Count < 2)
        {
            errors.Add("Jurnal minimal memiliki 2 baris (debit dan kredit).");
        }
        else
        {
            foreach (var line in dto.Lines)
            {
                errors.AddRange(ServiceGuard.Validate(line));
            }
        }

        if (errors.Count > 0)
            return Result<JournalEntryDto>.Failure(errors.Distinct());

        foreach (var line in dto.Lines!)
        {
            if (line.Debit < 0 || line.Credit < 0)
                return Result<JournalEntryDto>.Failure("Nilai debit atau kredit tidak boleh negatif.");

            var hasDebit = line.Debit > 0;
            var hasCredit = line.Credit > 0;
            if ((hasDebit && hasCredit) || (!hasDebit && !hasCredit))
            {
                return Result<JournalEntryDto>.Failure("Setiap baris jurnal harus memiliki tepat salah satu Debit atau Kredit lebih besar dari 0.");
            }
        }

        var totalDebit = dto.Lines.Sum(l => l.Debit);
        var totalCredit = dto.Lines.Sum(l => l.Credit);

        if (totalDebit <= 0)
            return Result<JournalEntryDto>.Failure("Total transaksi jurnal harus lebih besar dari nol.");

        if (totalDebit != totalCredit)
            return Result<JournalEntryDto>.Failure("Total debit dan total kredit tidak seimbang.");

        var accountIds = dto.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await _context.Accounts
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        foreach (var line in dto.Lines)
        {
            if (!accounts.TryGetValue(line.AccountId, out var account))
                return Result<JournalEntryDto>.Failure("Akun tidak ditemukan.");

            if (!account.IsActive)
                return Result<JournalEntryDto>.Failure($"Akun '{account.Code} - {account.Name}' sedang nonaktif.");
        }

        foreach (var account in accounts.Values)
        {
            await _context.Entry(account).ReloadAsync(cancellationToken);
        }

        var ownsTransaction = _context.Database.CurrentTransaction is null;
        if (ownsTransaction)
            await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var journalNumber = await _transactionNumbers.GenerateAsync(
                TransactionTypes.JournalEntry, 
                dto.TransactionDate, 
                cancellationToken);

            var journal = new JournalEntry
            {
                JournalNumber = journalNumber,
                TransactionDate = dto.TransactionDate,
                Description = dto.Description.Trim(),
                UserId = userId,
            };

            foreach (var line in dto.Lines)
            {
                var account = accounts[line.AccountId];

                if (account.NormalBalance == NormalBalance.Debit)
                {
                    account.Balance += (line.Debit - line.Credit);
                }
                else if (account.NormalBalance == NormalBalance.Credit)
                {
                    account.Balance += (line.Credit - line.Debit);
                }

                journal.Lines.Add(new JournalEntryLine
                {
                    AccountId = line.AccountId,
                    Debit = line.Debit,
                    Credit = line.Credit,
                });
            }

            _context.JournalEntries.Add(journal);
            await _context.SaveChangesAsync(cancellationToken);

            if (ownsTransaction)
                await _context.Database.CommitTransactionAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return await GetByIdAsync(journal.Id, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            await RollbackAsync(ownsTransaction, cancellationToken);
            return Result<JournalEntryDto>.Failure(ex.Message);
        }
        catch
        {
            await RollbackAsync(ownsTransaction, cancellationToken);
            throw;
        }
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.RequireAdmin))
            return Result.Failure(ServiceGuard.WriteDenied);

        var journal = await _context.JournalEntries
            .Include(j => j.Lines)
            .SingleOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (journal is null)
            return Result.Failure("Jurnal tidak ditemukan.");

        var ownsTransaction = _context.Database.CurrentTransaction is null;
        if (ownsTransaction)
            await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var accountIds = journal.Lines.Select(l => l.AccountId).Distinct().ToList();
            var accounts = await _context.Accounts
                .Where(a => accountIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, cancellationToken);

            foreach (var account in accounts.Values)
            {
                await _context.Entry(account).ReloadAsync(cancellationToken);
            }

            foreach (var line in journal.Lines)
            {
                if (accounts.TryGetValue(line.AccountId, out var account))
                {
                    if (account.NormalBalance == NormalBalance.Debit)
                    {
                        account.Balance -= (line.Debit - line.Credit);
                    }
                    else if (account.NormalBalance == NormalBalance.Credit)
                    {
                        account.Balance -= (line.Credit - line.Debit);
                    }
                }
            }

            var now = DateTime.UtcNow;
            journal.IsDeleted = true;
            journal.DeletedAt = now;
            journal.UpdatedAt = now;

            foreach (var line in journal.Lines)
            {
                line.IsDeleted = true;
                line.DeletedAt = now;
                line.UpdatedAt = now;
            }

            await _context.SaveChangesAsync(cancellationToken);
            if (ownsTransaction)
                await _context.Database.CommitTransactionAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            await RollbackAsync(ownsTransaction, cancellationToken);
            return Result.Failure(ex.Message);
        }
        catch
        {
            await RollbackAsync(ownsTransaction, cancellationToken);
            throw;
        }
    }

    private async Task RollbackAsync(bool ownsTransaction, CancellationToken cancellationToken)
    {
        if (ownsTransaction && _context.Database.CurrentTransaction is not null)
        {
            await _context.Database.RollbackTransactionAsync(cancellationToken);
        }
        _context.ChangeTracker.Clear();
    }

    private async Task<Dictionary<string, string>> GetUserNamesAsync(IEnumerable<string> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        var users = await _context.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(
            u => u.Id,
            u => u.UserName ?? u.FullName ?? u.Id);
    }

    private static JournalEntryDto ToDto(JournalEntry j, IReadOnlyDictionary<string, string> userNames)
    {
        var lines = j.Lines
            .OrderBy(l => l.Id)
            .Select(l => new JournalEntryLineDto
            {
                Id = l.Id,
                AccountId = l.AccountId,
                AccountCode = l.Account?.Code ?? string.Empty,
                AccountName = l.Account?.Name ?? string.Empty,
                Debit = l.Debit,
                Credit = l.Credit,
            })
            .ToList();

        return new JournalEntryDto
        {
            Id = j.Id,
            JournalNumber = j.JournalNumber,
            TransactionDate = j.TransactionDate,
            Description = j.Description,
            UserId = j.UserId,
            UserName = userNames.TryGetValue(j.UserId, out var name) ? name : null,
            TotalDebit = lines.Sum(l => l.Debit),
            TotalCredit = lines.Sum(l => l.Credit),
            Lines = lines,
        };
    }
}
