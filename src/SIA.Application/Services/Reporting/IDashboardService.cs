using SIA.Application.Common.Models;
using SIA.Application.DTOs.Reporting;

namespace SIA.Application.Services.Reporting;

/// <summary>
/// Layanan agregasi data ringkasan dashboard (KPI operasional, KPI keuangan, grafik tren, dan antrean operasional).
/// <para>
/// Otorisasi:
/// <list type="bullet">
/// <item><description>Semua role terautentikasi (<c>Admin</c>, <c>Staff</c>, <c>Viewer</c>) dapat mengakses data operasional.</description></item>
/// <item><description>Metrik keuangan (<see cref="DashboardSummaryDto.CanViewFinancials"/>, kas/bank, dan laba bersih) hanya diisi untuk role <c>Admin</c> dan <c>Viewer</c>.</description></item>
/// <item><description>Pengguna tanpa autentikasi ditolak dengan kegagalan otorisasi.</description></item>
/// </list>
/// </para>
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Mengambil seluruh data ringkasan dashboard sesuai periode tren yang dipilih.
    /// </summary>
    /// <param name="period">Periode waktu untuk visualisasi tren dan peringkat produk terlaris (default: <see cref="DashboardTrendPeriod.Last7Days"/>).</param>
    /// <param name="cancellationToken">Token pembatalan operasi asinkron.</param>
    /// <returns><see cref="Result{T}"/> berisi <see cref="DashboardSummaryDto"/>.</returns>
    Task<Result<DashboardSummaryDto>> GetSummaryAsync(
        DashboardTrendPeriod period = DashboardTrendPeriod.Last7Days,
        CancellationToken cancellationToken = default);
}
