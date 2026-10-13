using System.ComponentModel.DataAnnotations;

namespace SIA.Application.DTOs.Accounting;

public class JournalEntryDto
{
    public int Id { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public List<JournalEntryLineDto> Lines { get; set; } = new();
}

public class JournalEntryLineDto
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

/// <summary>
/// Jurnal umum diinput manual. Nomor jurnal dibuat oleh ITransactionNumberService;
/// validasi total debit = total kredit dilakukan di IJournalService.
/// </summary>
public class CreateJournalEntryDto
{
    [Required(ErrorMessage = "Tanggal transaksi wajib diisi.")]
    public DateTime TransactionDate { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "Keterangan jurnal wajib diisi.")]
    [StringLength(255, ErrorMessage = "Keterangan maksimal 255 karakter.")]
    public string Description { get; set; } = string.Empty;

    [MinLength(2, ErrorMessage = "Jurnal minimal memiliki 2 baris (debit dan kredit).")]
    public List<CreateJournalEntryLineDto> Lines { get; set; } = new();
}

public class CreateJournalEntryLineDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Akun wajib dipilih.")]
    public int AccountId { get; set; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Debit tidak boleh negatif.")]
    public decimal Debit { get; set; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Kredit tidak boleh negatif.")]
    public decimal Credit { get; set; }
}

public class JournalFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? AccountId { get; set; }

    /// <summary>Pencarian pada nomor jurnal atau keterangan.</summary>
    public string? Search { get; set; }
}
