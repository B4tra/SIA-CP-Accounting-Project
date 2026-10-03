namespace SIA.Domain.Entities.System;

/// <summary>
/// Konfigurasi penomoran transaksi otomatis. Bukan turunan BaseEntity.
/// </summary>
public class TransactionNumberConfig
{
    public int Id { get; set; }

    /// <summary>Contoh: "Sale", "PurchaseOrder", "JournalEntry".</summary>
    public string TransactionType { get; set; } = string.Empty;

    /// <summary>Contoh: "INV", "PO", "JRN".</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Contoh: "{Prefix}-{Year}-{Seq:0000}".</summary>
    public string Format { get; set; } = string.Empty;

    public int CurrentSequence { get; set; }

    /// <summary>Tahun berjalan; sequence di-reset setiap tahun baru.</summary>
    public int Year { get; set; }
}
