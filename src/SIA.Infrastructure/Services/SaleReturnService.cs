using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.Sales;
using SIA.Application.Services.Sales;
using SIA.Application.Services.System;
using SIA.Domain.Entities.Inventory;
using SIA.Domain.Entities.Sales;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Services;

/// <summary>
/// Layanan Retur Penjualan.
/// Otorisasi:
/// - Baca (GetAll, GetById, GetBySale): Policies.ViewAllData (Admin, Staff, Viewer)
/// - Buat (Create): Policies.ManageTransactions (Admin, Staff)
/// Retur penjualan menambah stok produk dan mencatat StockCard (ReturnIn).
/// Data penjualan asal (TotalAmount, PaidAmount, RemainingAmount, PaymentStatus) TIDAK diubah.
/// Jurnal umum TIDAK diposting otomatis.
/// </summary>
public class SaleReturnService : ISaleReturnService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITransactionNumberService _transactionNumbers;

    public SaleReturnService(AppDbContext context, ICurrentUserService currentUser, ITransactionNumberService transactionNumbers)
    {
        _context = context;
        _currentUser = currentUser;
        _transactionNumbers = transactionNumbers;
    }

    public async Task<Result<IReadOnlyList<SaleReturnDto>>> GetAllAsync(DateTime? dateFrom = null, DateTime? dateTo = null, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<SaleReturnDto>>.Failure(ServiceGuard.ReadDenied);

        var query = _context.SaleReturns.AsNoTracking()
            .Include(r => r.Sale).ThenInclude(s => s.Customer)
            .Include(r => r.Details).ThenInclude(d => d.Product)
            .AsQueryable();

        if (dateFrom is not null)
            query = query.Where(r => r.ReturnDate >= dateFrom.Value.Date);
        if (dateTo is not null)
        {
            var end = dateTo.Value.Date.AddDays(1);
            query = query.Where(r => r.ReturnDate < end);
        }

        var list = await query
            .OrderByDescending(r => r.ReturnDate)
            .ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);

        var userNames = await GetUserNamesAsync(list.Select(r => r.UserId), cancellationToken);
        return Result<IReadOnlyList<SaleReturnDto>>.Success(list.Select(r => ToDto(r, userNames)).ToList());
    }

    public async Task<Result<SaleReturnDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<SaleReturnDto>.Failure(ServiceGuard.ReadDenied);

        var saleReturn = await _context.SaleReturns.AsNoTracking()
            .Include(r => r.Sale).ThenInclude(s => s.Customer)
            .Include(r => r.Details).ThenInclude(d => d.Product)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (saleReturn is null)
            return Result<SaleReturnDto>.Failure("Retur penjualan tidak ditemukan.");

        var userNames = await GetUserNamesAsync(new[] { saleReturn.UserId }, cancellationToken);
        return Result<SaleReturnDto>.Success(ToDto(saleReturn, userNames));
    }

    public async Task<Result<IReadOnlyList<SaleReturnDto>>> GetBySaleAsync(int saleId, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<SaleReturnDto>>.Failure(ServiceGuard.ReadDenied);

        var list = await _context.SaleReturns.AsNoTracking()
            .Include(r => r.Sale).ThenInclude(s => s.Customer)
            .Include(r => r.Details).ThenInclude(d => d.Product)
            .Where(r => r.SaleId == saleId)
            .OrderByDescending(r => r.ReturnDate)
            .ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);

        var userNames = await GetUserNamesAsync(list.Select(r => r.UserId), cancellationToken);
        return Result<IReadOnlyList<SaleReturnDto>>.Success(list.Select(r => ToDto(r, userNames)).ToList());
    }

    public async Task<Result<SaleReturnDto>> CreateAsync(CreateSaleReturnDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result<SaleReturnDto>.Failure(ServiceGuard.WriteDenied);

        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            return Result<SaleReturnDto>.Failure("User yang sedang login tidak dikenali.");

        var errors = ServiceGuard.Validate(dto).ToList();
        if (dto.Details is not null)
        {
            foreach (var detail in dto.Details)
                errors.AddRange(ServiceGuard.Validate(detail));
        }
        if (errors.Count > 0)
            return Result<SaleReturnDto>.Failure(errors.Distinct());

        if (dto.Details is null || dto.Details.Count == 0)
            return Result<SaleReturnDto>.Failure("Retur minimal memiliki 1 item.");

        if (dto.Details.GroupBy(d => d.ProductId).Any(g => g.Count() > 1))
            return Result<SaleReturnDto>.Failure("Produk yang sama tidak boleh muncul lebih dari satu kali dalam satu retur.");

        var sale = await _context.Sales
            .Include(s => s.Details)
            .Include(s => s.Returns).ThenInclude(r => r.Details)
            .SingleOrDefaultAsync(s => s.Id == dto.SaleId, cancellationToken);

        if (sale is null)
            return Result<SaleReturnDto>.Failure("Penjualan tidak ditemukan.");

        if (dto.ReturnDate.Date < sale.SaleDate.Date)
            return Result<SaleReturnDto>.Failure("Tanggal retur tidak boleh sebelum tanggal penjualan.");

        var previouslyReturnedMap = sale.Returns
            .SelectMany(r => r.Details)
            .GroupBy(d => d.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Quantity));

        var productIds = dto.Details.Select(d => d.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        foreach (var product in products.Values)
        {
            await _context.Entry(product).ReloadAsync(cancellationToken);
        }

        foreach (var detail in dto.Details)
        {
            var saleDetail = sale.Details.FirstOrDefault(d => d.ProductId == detail.ProductId);
            if (saleDetail is null)
                return Result<SaleReturnDto>.Failure($"Produk ID {detail.ProductId} tidak terdapat dalam penjualan asal.");

            var previouslyReturned = previouslyReturnedMap.GetValueOrDefault(detail.ProductId, 0);
            var remainingReturnable = saleDetail.Quantity - previouslyReturned;
            if (detail.Quantity > remainingReturnable)
                return Result<SaleReturnDto>.Failure($"Jumlah retur untuk produk ID {detail.ProductId} melebihi sisa yang dapat diretur (tersedia {remainingReturnable}, diminta {detail.Quantity}).");

            if (!products.ContainsKey(detail.ProductId))
                return Result<SaleReturnDto>.Failure($"Produk ID {detail.ProductId} tidak ditemukan.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var returnNumber = await _transactionNumbers.GenerateAsync(TransactionTypes.SaleReturn, dto.ReturnDate, cancellationToken);

            var saleReturn = new SaleReturn
            {
                ReturnNumber = returnNumber,
                ReturnDate = dto.ReturnDate,
                SaleId = dto.SaleId,
                UserId = userId,
                Reason = ServiceGuard.Clean(dto.Reason),
            };

            decimal totalAmount = 0;
            foreach (var detail in dto.Details)
            {
                var saleDetail = sale.Details.First(d => d.ProductId == detail.ProductId);
                var subtotal = detail.Quantity * saleDetail.UnitPrice;
                totalAmount += subtotal;

                saleReturn.Details.Add(new SaleReturnDetail
                {
                    ProductId = detail.ProductId,
                    Quantity = detail.Quantity,
                    UnitPrice = saleDetail.UnitPrice,
                    Subtotal = subtotal,
                });

                var product = products[detail.ProductId];
                var stockBefore = product.Stock;
                product.Stock += detail.Quantity;

                _context.StockCards.Add(new StockCard
                {
                    ProductId = product.Id,
                    MovementDate = dto.ReturnDate,
                    MovementType = StockMovementType.ReturnIn,
                    Quantity = detail.Quantity,
                    StockBefore = stockBefore,
                    StockAfter = product.Stock,
                    Reference = returnNumber,
                });
            }

            saleReturn.TotalAmount = totalAmount;

            _context.SaleReturns.Add(saleReturn);
            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return await GetByIdAsync(saleReturn.Id, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _context.ChangeTracker.Clear();
            return Result<SaleReturnDto>.Failure(ex.Message);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<Dictionary<string, string>> GetUserNamesAsync(IEnumerable<string> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        var users = await _context.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(
            u => u.Id,
            u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName ?? u.Id : u.FullName);
    }

    private static SaleReturnDto ToDto(SaleReturn r, IReadOnlyDictionary<string, string> userNames) => new()
    {
        Id = r.Id,
        ReturnNumber = r.ReturnNumber,
        ReturnDate = r.ReturnDate,
        SaleId = r.SaleId,
        InvoiceNumber = r.Sale?.InvoiceNumber ?? string.Empty,
        CustomerName = r.Sale?.Customer?.Name,
        UserId = r.UserId,
        UserName = userNames.TryGetValue(r.UserId, out var name) ? name : null,
        TotalAmount = r.TotalAmount,
        Reason = r.Reason,
        Details = r.Details
            .OrderBy(d => d.Id)
            .Select(d => new SaleReturnDetailDto
            {
                Id = d.Id,
                ProductId = d.ProductId,
                ProductName = d.Product?.Name ?? string.Empty,
                Quantity = d.Quantity,
                UnitPrice = d.UnitPrice,
                Subtotal = d.Subtotal,
            })
            .ToList(),
    };
}
