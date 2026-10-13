using SIA.Application.Common.Models;
using SIA.Application.DTOs.Accounting;

namespace SIA.Application.Services.Accounting;

/// <summary>
/// Jurnal umum yang DIINPUT MANUAL (tidak ada posting otomatis dari transaksi lain).
/// Input jurnal: Admin dan Viewer.
/// </summary>
public interface IJournalService
{
    Task<Result<IReadOnlyList<JournalEntryDto>>> GetAllAsync(JournalFilterDto filter, CancellationToken cancellationToken = default);

    Task<Result<JournalEntryDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Memposting jurnal double-entry: validasi total debit = total kredit, membuat nomor jurnal
    /// lewat ITransactionNumberService, dan memperbarui <c>Account.Balance</c>.
    /// </summary>
    Task<Result<JournalEntryDto>> CreateAsync(CreateJournalEntryDto dto, CancellationToken cancellationToken = default);

    /// <summary>Soft delete jurnal dan membalik pengaruhnya pada <c>Account.Balance</c>.</summary>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
