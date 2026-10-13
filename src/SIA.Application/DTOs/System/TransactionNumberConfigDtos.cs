using System.ComponentModel.DataAnnotations;

namespace SIA.Application.DTOs.System;

public class TransactionNumberConfigDto
{
    public int Id { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public int CurrentSequence { get; set; }
    public int Year { get; set; }
}

/// <summary>Perubahan konfigurasi nomor transaksi (khusus Admin).</summary>
public class UpdateTransactionNumberConfigDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Konfigurasi tidak valid.")]
    public int Id { get; set; }

    [Required(ErrorMessage = "Prefix wajib diisi.")]
    [StringLength(10, ErrorMessage = "Prefix maksimal 10 karakter.")]
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Contoh: "{Prefix}-{Year}-{Seq:0000}".</summary>
    [Required(ErrorMessage = "Format wajib diisi.")]
    [StringLength(50, ErrorMessage = "Format maksimal 50 karakter.")]
    public string Format { get; set; } = string.Empty;
}
