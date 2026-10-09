using System.ComponentModel.DataAnnotations;

namespace SIA.Application.DTOs.Accounting;

public record JournalEntryDto
{
    public int Id { get; init; }
    public string JournalNumber { get; init; } = string.Empty;
    public DateTime TransactionDate { get; init; }
    public string Description { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string? UserFullName { get; init; }
    public decimal TotalDebit { get; init; }
    public decimal TotalCredit { get; init; }
    public int LineCount { get; init; }
}

public record JournalEntryLineDto
{
    public int Id { get; init; }
    public int AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public decimal Debit { get; init; }
    public decimal Credit { get; init; }
}

public record JournalEntryDetailDto
{
    public int Id { get; init; }
    public string JournalNumber { get; init; } = string.Empty;
    public DateTime TransactionDate { get; init; }
    public string Description { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string? UserFullName { get; init; }
    public decimal TotalDebit { get; init; }
    public decimal TotalCredit { get; init; }
    public IReadOnlyList<JournalEntryLineDto> Lines { get; init; } = Array.Empty<JournalEntryLineDto>();
}

public class CreateJournalEntryLineDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Akun wajib dipilih.")]
    public int AccountId { get; set; }

    [Range(0, (double)decimal.MaxValue, ErrorMessage = "Nilai debit tidak boleh negatif.")]
    public decimal Debit { get; set; }

    [Range(0, (double)decimal.MaxValue, ErrorMessage = "Nilai kredit tidak boleh negatif.")]
    public decimal Credit { get; set; }
}

public class CreateJournalEntryDto
{
    [Required(ErrorMessage = "Tanggal transaksi wajib diisi.")]
    public DateTime TransactionDate { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "Keterangan jurnal wajib diisi.")]
    [StringLength(500, ErrorMessage = "Keterangan maksimal 500 karakter.")]
    public string Description { get; set; } = string.Empty;

    [MinLength(2, ErrorMessage = "Jurnal minimal harus memiliki 2 baris detail (double-entry).")]
    public List<CreateJournalEntryLineDto> Lines { get; set; } = new();
}

public class JournalFilterDto
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? SearchTerm { get; set; }
    public int? AccountId { get; set; }
}
