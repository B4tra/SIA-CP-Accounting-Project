using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.Purchasing;
using SIA.Application.Services.Purchasing;
using SIA.Application.Services.System;
using SIA.Domain.Entities.Inventory;
using SIA.Domain.Entities.Purchasing;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Services;

/// <summary>
/// Layanan Retur Pembelian.
/// Otorisasi:
/// - Baca (GetAll, GetById, GetByPurchaseOrder): Policies.ViewAllData (Admin, Staff, Viewer)
/// - Buat (Create): Policies.ManageTransactions (Admin, Staff)
/// Retur pembelian hanya dapat dibuat untuk Purchase Order yang berstatus Received.
/// Retur pembelian mengurangi stok produk di gudang dan mencatat StockCard (ReturnOut).
/// Data Purchase Order asal (TotalAmount, PaidAmount, RemainingAmount, Status, PaymentStatus) TIDAK diubah.
/// Jurnal umum TIDAK diposting otomatis.
/// </summary>
public class PurchaseReturnService : IPurchaseReturnService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITransactionNumberService _transactionNumbers;

    public PurchaseReturnService(AppDbContext context, ICurrentUserService currentUser, ITransactionNumberService transactionNumbers)
    {
        _context = context;
        _currentUser = currentUser;
        _transactionNumbers = transactionNumbers;
    }

    public async Task<Result<IReadOnlyList<PurchaseReturnDto>>> GetAllAsync(DateTime? dateFrom = null, DateTime? dateTo = null, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<PurchaseReturnDto>>.Failure(ServiceGuard.ReadDenied);

        var query = _context.PurchaseReturns.AsNoTracking()
            .Include(r => r.PurchaseOrder).ThenInclude(p => p.Supplier)
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
        return Result<IReadOnlyList<PurchaseReturnDto>>.Success(list.Select(r => ToDto(r, userNames)).ToList());
    }

    public async Task<Result<PurchaseReturnDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<PurchaseReturnDto>.Failure(ServiceGuard.ReadDenied);

        var purchaseReturn = await _context.PurchaseReturns.AsNoTracking()
            .Include(r => r.PurchaseOrder).ThenInclude(p => p.Supplier)
            .Include(r => r.Details).ThenInclude(d => d.Product)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (purchaseReturn is null)
            return Result<PurchaseReturnDto>.Failure("Retur pembelian tidak ditemukan.");

        var userNames = await GetUserNamesAsync(new[] { purchaseReturn.UserId }, cancellationToken);
        return Result<PurchaseReturnDto>.Success(ToDto(purchaseReturn, userNames));
    }

    public async Task<Result<IReadOnlyList<PurchaseReturnDto>>> GetByPurchaseOrderAsync(int purchaseOrderId, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<PurchaseReturnDto>>.Failure(ServiceGuard.ReadDenied);

        var list = await _context.PurchaseReturns.AsNoTracking()
            .Include(r => r.PurchaseOrder).ThenInclude(p => p.Supplier)
            .Include(r => r.Details).ThenInclude(d => d.Product)
            .Where(r => r.PurchaseOrderId == purchaseOrderId)
            .OrderByDescending(r => r.ReturnDate)
            .ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);

        var userNames = await GetUserNamesAsync(list.Select(r => r.UserId), cancellationToken);
        return Result<IReadOnlyList<PurchaseReturnDto>>.Success(list.Select(r => ToDto(r, userNames)).ToList());
    }

    public async Task<Result<PurchaseReturnDto>> CreateAsync(CreatePurchaseReturnDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result<PurchaseReturnDto>.Failure(ServiceGuard.WriteDenied);

        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            return Result<PurchaseReturnDto>.Failure("User yang sedang login tidak dikenali.");

        var errors = ServiceGuard.Validate(dto).ToList();
        if (dto.Details is not null)
        {
            foreach (var detail in dto.Details)
                errors.AddRange(ServiceGuard.Validate(detail));
        }
        if (errors.Count > 0)
            return Result<PurchaseReturnDto>.Failure(errors.Distinct());

        if (dto.Details is null || dto.Details.Count == 0)
            return Result<PurchaseReturnDto>.Failure("Retur minimal memiliki 1 item.");

        if (dto.Details.GroupBy(d => d.ProductId).Any(g => g.Count() > 1))
            return Result<PurchaseReturnDto>.Failure("Produk yang sama tidak boleh muncul lebih dari satu kali dalam satu retur.");

        var po = await _context.PurchaseOrders
            .Include(p => p.Details)
            .Include(p => p.Returns).ThenInclude(r => r.Details)
            .SingleOrDefaultAsync(p => p.Id == dto.PurchaseOrderId, cancellationToken);

        if (po is null)
            return Result<PurchaseReturnDto>.Failure("Purchase Order tidak ditemukan.");

        if (po.Status != PurchaseOrderStatus.Received)
            return Result<PurchaseReturnDto>.Failure("Retur pembelian hanya dapat dilakukan untuk Purchase Order yang sudah diterima.");

        if (dto.ReturnDate.Date < po.OrderDate.Date)
            return Result<PurchaseReturnDto>.Failure("Tanggal retur tidak boleh sebelum tanggal order.");

        var previouslyReturnedMap = po.Returns
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
            var poDetail = po.Details.FirstOrDefault(d => d.ProductId == detail.ProductId);
            if (poDetail is null)
                return Result<PurchaseReturnDto>.Failure($"Produk ID {detail.ProductId} tidak terdapat dalam Purchase Order asal.");

            var previouslyReturned = previouslyReturnedMap.GetValueOrDefault(detail.ProductId, 0);
            var remainingReturnable = poDetail.Quantity - previouslyReturned;
            if (detail.Quantity > remainingReturnable)
                return Result<PurchaseReturnDto>.Failure($"Jumlah retur untuk produk ID {detail.ProductId} melebihi sisa yang dapat diretur (tersedia {remainingReturnable}, diminta {detail.Quantity}).");

            if (!products.TryGetValue(detail.ProductId, out var product))
                return Result<PurchaseReturnDto>.Failure($"Produk ID {detail.ProductId} tidak ditemukan.");

            if (detail.Quantity > product.Stock)
                return Result<PurchaseReturnDto>.Failure($"Stok {product.Name} tidak cukup di gudang untuk diretur (tersedia {product.Stock}, diminta {detail.Quantity}).");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var returnNumber = await _transactionNumbers.GenerateAsync(TransactionTypes.PurchaseReturn, dto.ReturnDate, cancellationToken);

            var purchaseReturn = new PurchaseReturn
            {
                ReturnNumber = returnNumber,
                ReturnDate = dto.ReturnDate,
                PurchaseOrderId = dto.PurchaseOrderId,
                UserId = userId,
                Reason = ServiceGuard.Clean(dto.Reason),
            };

            decimal totalAmount = 0;
            foreach (var detail in dto.Details)
            {
                var poDetail = po.Details.First(d => d.ProductId == detail.ProductId);
                var subtotal = detail.Quantity * poDetail.UnitPrice;
                totalAmount += subtotal;

                purchaseReturn.Details.Add(new PurchaseReturnDetail
                {
                    ProductId = detail.ProductId,
                    Quantity = detail.Quantity,
                    UnitPrice = poDetail.UnitPrice,
                    Subtotal = subtotal,
                });

                var product = products[detail.ProductId];
                var stockBefore = product.Stock;
                product.Stock -= detail.Quantity;

                _context.StockCards.Add(new StockCard
                {
                    ProductId = product.Id,
                    MovementDate = dto.ReturnDate,
                    MovementType = StockMovementType.ReturnOut,
                    Quantity = detail.Quantity,
                    StockBefore = stockBefore,
                    StockAfter = product.Stock,
                    Reference = returnNumber,
                });
            }

            purchaseReturn.TotalAmount = totalAmount;

            _context.PurchaseReturns.Add(purchaseReturn);
            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return await GetByIdAsync(purchaseReturn.Id, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _context.ChangeTracker.Clear();
            return Result<PurchaseReturnDto>.Failure(ex.Message);
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

    private static PurchaseReturnDto ToDto(PurchaseReturn r, IReadOnlyDictionary<string, string> userNames) => new()
    {
        Id = r.Id,
        ReturnNumber = r.ReturnNumber,
        ReturnDate = r.ReturnDate,
        PurchaseOrderId = r.PurchaseOrderId,
        PONumber = r.PurchaseOrder?.PONumber ?? string.Empty,
        SupplierName = r.PurchaseOrder?.Supplier?.Name,
        UserId = r.UserId,
        UserName = userNames.TryGetValue(r.UserId, out var name) ? name : null,
        TotalAmount = r.TotalAmount,
        Reason = r.Reason,
        Details = r.Details
            .OrderBy(d => d.Id)
            .Select(d => new PurchaseReturnDetailDto
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
