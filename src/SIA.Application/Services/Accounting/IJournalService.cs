using SIA.Application.Common.Models;
using SIA.Application.DTOs.Accounting;

namespace SIA.Application.Services.Accounting;

public interface IJournalService
{
    Task<Result<IReadOnlyList<JournalEntryDto>>> GetListAsync(JournalFilterDto filter, CancellationToken ct = default);
    Task<Result<JournalEntryDetailDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<JournalEntryDetailDto>> CreateAsync(CreateJournalEntryDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
}
