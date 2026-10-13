using SIA.Application.Common.Models;
using SIA.Application.DTOs.Inventory;

namespace SIA.Application.Services.Inventory;

/// <summary>
/// Produk aki/baterai. Baca: semua role. Tulis: Admin, Staff.
/// Stok tidak diubah lewat service ini (hanya lewat transaksi yang menulis StockCard).
/// </summary>
public interface IProductService
{
    /// <summary>Pencarian pada nama, SKU, atau merek.</summary>
    Task<Result<IReadOnlyList<ProductDto>>> GetAllAsync(string? search = null, CancellationToken cancellationToken = default);

    Task<Result<ProductDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<ProductDto>> CreateAsync(CreateProductDto dto, CancellationToken cancellationToken = default);

    Task<Result<ProductDto>> UpdateAsync(UpdateProductDto dto, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
