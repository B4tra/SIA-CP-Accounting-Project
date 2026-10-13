using SIA.Application.Common.Models;
using SIA.Application.DTOs.System;

namespace SIA.Application.Services.System;

/// <summary>
/// Satu-satunya sumber nomor transaksi. Format default <c>{Prefix}-{Year}-{Seq:0000}</c>,
/// sequence di-reset setiap tahun.
/// </summary>
public interface ITransactionNumberService
{
    /// <summary>
    /// Menghasilkan nomor berikutnya untuk jenis transaksi (lihat <c>Common.Constants.TransactionTypes</c>)
    /// berdasarkan tahun dari <paramref name="transactionDate"/>. Dipanggil oleh service lain, bukan oleh UI.
    /// Melempar <see cref="InvalidOperationException"/> jika konfigurasi untuk jenis tersebut tidak ada.
    /// </summary>
    Task<string> GenerateAsync(string transactionType, DateTime transactionDate, CancellationToken cancellationToken = default);

    /// <summary>Daftar konfigurasi. Hanya Admin.</summary>
    Task<Result<IReadOnlyList<TransactionNumberConfigDto>>> GetConfigsAsync(CancellationToken cancellationToken = default);

    /// <summary>Mengubah prefix/format. Hanya Admin.</summary>
    Task<Result<TransactionNumberConfigDto>> UpdateConfigAsync(UpdateTransactionNumberConfigDto dto, CancellationToken cancellationToken = default);
}
