using System.ComponentModel.DataAnnotations;

namespace SIA.Application.DTOs.System;

public record TransactionNumberConfigDto
{
    public int Id { get; init; }
    public string TransactionType { get; init; } = string.Empty;
    public string Prefix { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public int CurrentSequence { get; init; }
    public int Year { get; init; }
}

public class UpdateTransactionNumberConfigDto
{
    [Required(ErrorMessage = "Prefix wajib diisi.")]
    [StringLength(10, ErrorMessage = "Prefix maksimal 10 karakter.")]
    public string Prefix { get; set; } = string.Empty;

    [Required(ErrorMessage = "Format penomoran wajib diisi.")]
    [StringLength(50, ErrorMessage = "Format maksimal 50 karakter.")]
    public string Format { get; set; } = string.Empty;
}
