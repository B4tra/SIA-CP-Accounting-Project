using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.Inventory;
using SIA.Application.Services.Inventory;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Services;

public class StockCardService : IStockCardService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public StockCardService(AppDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<StockCardDto>>> GetAllAsync(
        StockCardFilterDto? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<StockCardDto>>.Failure(ServiceGuard.ReadDenied);

        var items = await QueryStockCardsAsync(filter ?? new StockCardFilterDto(), cancellationToken);
        return Result<IReadOnlyList<StockCardDto>>.Success(items);
    }

    public async Task<Result<IReadOnlyList<StockCardDto>>> GetByProductAsync(
        int productId,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<StockCardDto>>.Failure(ServiceGuard.ReadDenied);

        var productExists = await _context.Products
            .AsNoTracking()
            .AnyAsync(p => p.Id == productId, cancellationToken);

        if (!productExists)
            return Result<IReadOnlyList<StockCardDto>>.Failure("Produk tidak ditemukan.");

        var items = await QueryStockCardsAsync(
            new StockCardFilterDto
            {
                ProductId = productId,
                DateFrom = dateFrom,
                DateTo = dateTo,
            },
            cancellationToken);

        return Result<IReadOnlyList<StockCardDto>>.Success(items);
    }

    public async Task<Result<IReadOnlyList<ReorderAlertDto>>> GetReorderAlertsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<ReorderAlertDto>>.Failure(ServiceGuard.ReadDenied);

        var alerts = await _context.Products
            .AsNoTracking()
            .Where(p => p.Stock <= p.ReorderPoint)
            .OrderBy(p => p.Stock)
            .ThenBy(p => p.Name)
            .Select(p => new ReorderAlertDto
            {
                ProductId = p.Id,
                ProductName = p.Name,
                SKU = p.SKU,
                Brand = p.Brand,
                Stock = p.Stock,
                ReorderPoint = p.ReorderPoint,
            })
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<ReorderAlertDto>>.Success(alerts);
    }

    private async Task<IReadOnlyList<StockCardDto>> QueryStockCardsAsync(
        StockCardFilterDto filter,
        CancellationToken cancellationToken)
    {
        var query = _context.StockCards.AsNoTracking().AsQueryable();

        if (filter.ProductId is not null)
            query = query.Where(s => s.ProductId == filter.ProductId.Value);

        if (filter.MovementType is not null)
            query = query.Where(s => s.MovementType == filter.MovementType.Value);

        if (filter.DateFrom is not null)
        {
            var start = filter.DateFrom.Value.Date;
            query = query.Where(s => s.MovementDate >= start);
        }

        if (filter.DateTo is not null && filter.DateTo.Value.Date < DateTime.MaxValue.Date)
        {
            var end = filter.DateTo.Value.Date.AddDays(1);
            query = query.Where(s => s.MovementDate < end);
        }

        var term = ServiceGuard.Clean(filter.Search);
        if (term is not null)
        {
            var pattern = $"%{term}%";
            query = query.Where(s =>
                (s.Reference != null && EF.Functions.Like(s.Reference, pattern)) ||
                EF.Functions.Like(s.Product.Name, pattern) ||
                (s.Product.SKU != null && EF.Functions.Like(s.Product.SKU, pattern)) ||
                (s.Product.Brand != null && EF.Functions.Like(s.Product.Brand, pattern)));
        }

        return await query
            .OrderByDescending(s => s.MovementDate)
            .ThenByDescending(s => s.Id)
            .Select(s => new StockCardDto
            {
                Id = s.Id,
                ProductId = s.ProductId,
                ProductName = s.Product.Name,
                SKU = s.Product.SKU,
                Brand = s.Product.Brand,
                MovementDate = s.MovementDate,
                MovementType = s.MovementType,
                Quantity = s.Quantity,
                StockBefore = s.StockBefore,
                StockAfter = s.StockAfter,
                Reference = s.Reference,
            })
            .ToListAsync(cancellationToken);
    }
}
