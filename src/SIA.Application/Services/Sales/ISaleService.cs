using SIA.Application.Common.Models;
using SIA.Application.DTOs.Payments;
using SIA.Application.DTOs.Sales;

namespace SIA.Application.Services.Sales;

/// <summary>Penjualan. Baca: semua role. Buat: Admin, Staff.</summary>
public interface ISaleService
{
    Task<Result<IReadOnlyList<SaleDto>>> GetAllAsync(SaleFilterDto filter, CancellationToken cancellationToken = default);

    Task<Result<SaleDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Membuat penjualan: nomor invoice dari ITransactionNumberService, validasi stok,
    /// mengurangi stok dan menulis StockCard (Out), serta menghitung PaidAmount/RemainingAmount/PaymentStatus.
    /// Jika PaidAmount &lt; total, DueDate wajib. Jurnal TIDAK diposting otomatis.
    /// </summary>
    Task<Result<SaleDto>> CreateAsync(CreateSaleDto dto, CancellationToken cancellationToken = default);

    /// <summary>Daftar piutang (penjualan yang belum lunas).</summary>
    Task<Result<IReadOnlyList<OutstandingDto>>> GetOutstandingAsync(CancellationToken cancellationToken = default);
}
