using SIA.Domain.Common;

namespace SIA.Domain.Entities.Accounting;

/// <summary>
/// Jurnal umum — diinput manual oleh pengguna.
/// </summary>
public class JournalEntry : BaseEntity
{
    public string JournalNumber { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>FK ke ApplicationUser (Infrastructure).</summary>
    public string UserId { get; set; } = string.Empty;

    public ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();
}
