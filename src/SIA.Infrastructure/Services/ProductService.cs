using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.Inventory;
using SIA.Application.Services.Inventory;
using SIA.Domain.Entities.Inventory;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Services;

/// <summary>
/// Produk aki/baterai. Baca: semua role. Tulis: Admin, Staff.
/// Stok TIDAK diubah di sini (0 saat dibuat, tidak berubah saat diedit); stok hanya berubah
/// lewat transaksi yang menulis StockCard.
/// </summary>
public class ProductService : IProductService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public ProductService(AppDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<ProductDto>>> GetAllAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<ProductDto>>.Failure(ServiceGuard.ReadDenied);

        var query = _context.Products.AsNoTracking();
        var term = ServiceGuard.Clean(search);
        if (term is not null)
        {
            var pattern = $"%{term}%";
            query = query.Where(p =>
                EF.Functions.Like(p.Name, pattern) ||
                (p.SKU != null && EF.Functions.Like(p.SKU, pattern)) ||
                (p.Brand != null && EF.Functions.Like(p.Brand, pattern)));
        }

        var products = await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<ProductDto>>.Success(products.Select(ToDto).ToList());
    }

    public async Task<Result<ProductDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<ProductDto>.Failure(ServiceGuard.ReadDenied);

        var product = await _context.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        return product is null
            ? Result<ProductDto>.Failure("Produk tidak ditemukan.")
            : Result<ProductDto>.Success(ToDto(product));
    }

    public async Task<Result<ProductDto>> CreateAsync(CreateProductDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result<ProductDto>.Failure(ServiceGuard.WriteDenied);

        var errors = await ValidateAsync(dto, null, cancellationToken);
        if (errors.Count > 0)
            return Result<ProductDto>.Failure(errors);

        var product = new Product { Stock = 0 };
        Apply(product, dto);

        _context.Products.Add(product);
        await _context.SaveChangesAsync(cancellationToken);
        return Result<ProductDto>.Success(ToDto(product));
    }

    public async Task<Result<ProductDto>> UpdateAsync(UpdateProductDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result<ProductDto>.Failure(ServiceGuard.WriteDenied);

        var product = await _context.Products.SingleOrDefaultAsync(p => p.Id == dto.Id, cancellationToken);
        if (product is null)
            return Result<ProductDto>.Failure("Produk tidak ditemukan.");

        var errors = await ValidateAsync(dto, dto.Id, cancellationToken);
        if (errors.Count > 0)
            return Result<ProductDto>.Failure(errors);

        Apply(product, dto); // Stock tidak disentuh
        await _context.SaveChangesAsync(cancellationToken);
        return Result<ProductDto>.Success(ToDto(product));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result.Failure(ServiceGuard.WriteDenied);

        var product = await _context.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
            return Result.Failure("Produk tidak ditemukan.");

        _context.Products.Remove(product); // soft delete
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<List<string>> ValidateAsync(CreateProductDto dto, int? currentId, CancellationToken cancellationToken)
    {
        var errors = ServiceGuard.Validate(dto).ToList();
        if (errors.Count > 0) return errors;

        if (dto.BatteryType is { } type && !Enum.IsDefined(type))
            errors.Add("Jenis aki tidak valid.");

        var sku = ServiceGuard.Clean(dto.SKU);
        if (sku is not null)
        {
            // Termasuk produk terhapus karena SKU dijaga unique index database
            var skuTaken = await _context.Products.IgnoreQueryFilters()
                .AnyAsync(p => p.SKU == sku && (currentId == null || p.Id != currentId), cancellationToken);
            if (skuTaken)
                errors.Add($"SKU '{sku}' sudah digunakan (termasuk produk yang telah dihapus).");
        }

        return errors;
    }

    private static void Apply(Product product, CreateProductDto dto)
    {
        product.Name = dto.Name.Trim();
        product.Description = ServiceGuard.Clean(dto.Description);
        product.Category = ServiceGuard.Clean(dto.Category);
        product.Brand = ServiceGuard.Clean(dto.Brand);
        product.BatteryType = dto.BatteryType;
        product.Voltage = ServiceGuard.Clean(dto.Voltage);
        product.Capacity = ServiceGuard.Clean(dto.Capacity);
        product.SellingPrice = dto.SellingPrice;
        product.PurchasePrice = dto.PurchasePrice;
        product.ReorderPoint = dto.ReorderPoint;
        product.SKU = ServiceGuard.Clean(dto.SKU);
    }

    private static ProductDto ToDto(Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        Category = p.Category,
        Brand = p.Brand,
        BatteryType = p.BatteryType,
        Voltage = p.Voltage,
        Capacity = p.Capacity,
        SellingPrice = p.SellingPrice,
        PurchasePrice = p.PurchasePrice,
        Stock = p.Stock,
        ReorderPoint = p.ReorderPoint,
        SKU = p.SKU,
    };
}
