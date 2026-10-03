using SIA.Domain.Common;
using SIA.Domain.Enums;

namespace SIA.Domain.Entities.Accounting;

public class Account : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountCategory Category { get; set; }
    public NormalBalance NormalBalance { get; set; }

    /// <summary>
    /// Saldo tersimpan; diperbarui saat jurnal diposting dan dapat direkalkulasi dari SUM(JournalEntryLine).
    /// </summary>
    public decimal Balance { get; set; }

    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    // Hierarki Chart of Accounts
    public int? ParentAccountId { get; set; }
    public Account? ParentAccount { get; set; }
    public ICollection<Account> ChildAccounts { get; set; } = new List<Account>();

    public ICollection<JournalEntryLine> JournalEntryLines { get; set; } = new List<JournalEntryLine>();
}
