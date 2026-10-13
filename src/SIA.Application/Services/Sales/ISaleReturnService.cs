using SIA.Application.Common.Models;
using SIA.Application.DTOs.Sales;

namespace SIA.Application.Services.Sales;

/// <summary>Retur penjualan. Baca: semua role. Buat: Admin, Staff.</summary>
public interface ISaleReturnService
{
    Task<Result<IReadOnlyList<SaleReturnDto>>> GetAllAsync(DateTime? dateFrom = null, DateTime? dateTo = null, CancellationToken cancellationToken = default);

    Task<Result<SaleReturnDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SaleReturnDto>>> GetBySaleAsync(int saleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Membuat retur: validasi jumlah tidak melebihi yang terjual (dikurangi retur sebelumnya),
    /// menambah stok dan menulis StockCard (ReturnIn).
    /// </summary>
    Task<Result<SaleReturnDto>> CreateAsync(CreateSaleReturnDto dto, CancellationToken cancellationToken = default);
}
