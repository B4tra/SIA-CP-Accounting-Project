using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.Payments;
using SIA.Application.DTOs.Purchasing;
using SIA.Application.Services.Purchasing;
using SIA.Application.Services.System;
using SIA.Domain.Entities.Inventory;
using SIA.Domain.Entities.Purchasing;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Services;

/// <summary>
/// Layanan Purchase Order.
/// Alur status: Draft -> Approved -> Received (atau Cancelled).
/// Otorisasi:
/// - Baca (GetAll, GetById, GetOutstanding): Policies.ViewAllData (Admin, Staff, Viewer)
/// - Transaksi (CreateDraft, UpdateDraft, Receive, Cancel): Policies.ManageTransactions (Admin, Staff)
/// - Approve: Policies.ApprovePurchaseOrder (HANYA Admin)
/// Stok fisik hanya bertambah dan StockCard (In) dicatat saat ReceiveAsync.
/// Jurnal umum TIDAK diposting otomatis.
/// </summary>
public class PurchaseOrderService : IPurchaseOrderService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITransactionNumberService _transactionNumbers;

    public PurchaseOrderService(AppDbContext context, ICurrentUserService currentUser, ITransactionNumberService transactionNumbers)
    {
        _context = context;
        _currentUser = currentUser;
        _transactionNumbers = transactionNumbers;
    }

    public async Task<Result<IReadOnlyList<PurchaseOrderDto>>> GetAllAsync(PurchaseOrderFilterDto filter, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<PurchaseOrderDto>>.Failure(ServiceGuard.ReadDenied);

        filter ??= new PurchaseOrderFilterDto();
        var query = _context.PurchaseOrders.AsNoTracking().Include(p => p.Supplier).AsQueryable();

        if (filter.DateFrom is not null)
            query = query.Where(p => p.OrderDate >= filter.DateFrom.Value.Date);
        if (filter.DateTo is not null)
        {
            var end = filter.DateTo.Value.Date.AddDays(1);
            query = query.Where(p => p.OrderDate < end);
        }
        if (filter.SupplierId is not null)
            query = query.Where(p => p.SupplierId == filter.SupplierId.Value);
        if (filter.Status is not null)
            query = query.Where(p => p.Status == filter.Status.Value);
        if (filter.PaymentStatus is not null)
            query = query.Where(p => p.PaymentStatus == filter.PaymentStatus.Value);

        var term = ServiceGuard.Clean(filter.Search);
        if (term is not null)
        {
            var pattern = $"%{term}%";
            query = query.Where(p =>
                EF.Functions.Like(p.PONumber, pattern) ||
                EF.Functions.Like(p.Supplier.Name, pattern));
        }

        var pos = await query
            .OrderByDescending(p => p.OrderDate)
            .ThenByDescending(p => p.Id)
            .ToListAsync(cancellationToken);

        var userIds = pos.Select(p => p.UserId).Concat(pos.Select(p => p.ApprovedByUserId));
        var userNames = await GetUserNamesAsync(userIds, cancellationToken);

        return Result<IReadOnlyList<PurchaseOrderDto>>.Success(
            pos.Select(p => ToDto(p, userNames, includeDetails: false)).ToList());
    }

    public async Task<Result<PurchaseOrderDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<PurchaseOrderDto>.Failure(ServiceGuard.ReadDenied);

        var po = await _context.PurchaseOrders.AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Details).ThenInclude(d => d.Product)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (po is null)
            return Result<PurchaseOrderDto>.Failure("Purchase Order tidak ditemukan.");

        var userNames = await GetUserNamesAsync(new[] { po.UserId, po.ApprovedByUserId }, cancellationToken);
        return Result<PurchaseOrderDto>.Success(ToDto(po, userNames, includeDetails: true));
    }

    public async Task<Result<PurchaseOrderDto>> CreateDraftAsync(CreatePurchaseOrderDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result<PurchaseOrderDto>.Failure(ServiceGuard.WriteDenied);

        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            return Result<PurchaseOrderDto>.Failure("User yang sedang login tidak dikenali.");

        var errors = ServiceGuard.Validate(dto).ToList();
        if (dto.Details is not null)
        {
            foreach (var detail in dto.Details)
                errors.AddRange(ServiceGuard.Validate(detail));
        }
        if (errors.Count > 0)
            return Result<PurchaseOrderDto>.Failure(errors.Distinct());

        if (dto.Details is null || dto.Details.Count == 0)
            return Result<PurchaseOrderDto>.Failure("PO minimal memiliki 1 item.");

        if (dto.Details.GroupBy(d => d.ProductId).Any(g => g.Count() > 1))
            return Result<PurchaseOrderDto>.Failure("Produk yang sama tidak boleh muncul lebih dari satu kali dalam satu Purchase Order.");

        if (dto.DueDate is not null && dto.DueDate.Value.Date < dto.OrderDate.Date)
            return Result<PurchaseOrderDto>.Failure("Tanggal jatuh tempo tidak boleh sebelum tanggal order.");

        if (!await _context.Suppliers.AnyAsync(s => s.Id == dto.SupplierId, cancellationToken))
            return Result<PurchaseOrderDto>.Failure("Pemasok tidak ditemukan.");

        var productIds = dto.Details.Select(d => d.ProductId).Distinct().ToList();
        var existingCount = await _context.Products.CountAsync(p => productIds.Contains(p.Id), cancellationToken);
        if (existingCount != productIds.Count)
            return Result<PurchaseOrderDto>.Failure("Produk yang dipilih tidak ditemukan.");

        var poNumber = await _transactionNumbers.GenerateAsync(TransactionTypes.PurchaseOrder, dto.OrderDate, cancellationToken);
        var total = dto.Details.Sum(d => d.Quantity * d.UnitPrice);

        var po = new PurchaseOrder
        {
            PONumber = poNumber,
            OrderDate = dto.OrderDate,
            DueDate = dto.DueDate,
            SupplierId = dto.SupplierId,
            UserId = userId,
            TotalAmount = total,
            PaidAmount = 0m,
            RemainingAmount = total,
            Status = PurchaseOrderStatus.Draft,
            ApprovedByUserId = null,
            PaymentStatus = PaymentStatus.Unpaid,
            Notes = ServiceGuard.Clean(dto.Notes),
        };

        foreach (var detail in dto.Details)
        {
            po.Details.Add(new PurchaseOrderDetail
            {
                ProductId = detail.ProductId,
                Quantity = detail.Quantity,
                UnitPrice = detail.UnitPrice,
                Subtotal = detail.Quantity * detail.UnitPrice,
            });
        }

        _context.PurchaseOrders.Add(po);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(po.Id, cancellationToken);
    }

    public async Task<Result<PurchaseOrderDto>> UpdateDraftAsync(UpdatePurchaseOrderDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result<PurchaseOrderDto>.Failure(ServiceGuard.WriteDenied);

        var errors = ServiceGuard.Validate(dto).ToList();
        if (dto.Details is not null)
        {
            foreach (var detail in dto.Details)
                errors.AddRange(ServiceGuard.Validate(detail));
        }
        if (errors.Count > 0)
            return Result<PurchaseOrderDto>.Failure(errors.Distinct());

        var po = await _context.PurchaseOrders
            .Include(p => p.Details)
            .SingleOrDefaultAsync(p => p.Id == dto.Id, cancellationToken);

        if (po is null)
            return Result<PurchaseOrderDto>.Failure("Purchase Order tidak ditemukan.");

        await _context.Entry(po).ReloadAsync(cancellationToken);
        await _context.Entry(po).Collection(p => p.Details).LoadAsync(cancellationToken);

        if (po.Status != PurchaseOrderStatus.Draft)
            return Result<PurchaseOrderDto>.Failure("Hanya Purchase Order berstatus Draft yang dapat diubah.");

        if (dto.DueDate is not null && dto.DueDate.Value.Date < dto.OrderDate.Date)
            return Result<PurchaseOrderDto>.Failure("Tanggal jatuh tempo tidak boleh sebelum tanggal order.");

        if (dto.Details is null || dto.Details.Count == 0)
            return Result<PurchaseOrderDto>.Failure("PO minimal memiliki 1 item.");

        if (dto.Details.GroupBy(d => d.ProductId).Any(g => g.Count() > 1))
            return Result<PurchaseOrderDto>.Failure("Produk yang sama tidak boleh muncul lebih dari satu kali dalam satu Purchase Order.");

        if (!await _context.Suppliers.AnyAsync(s => s.Id == dto.SupplierId, cancellationToken))
            return Result<PurchaseOrderDto>.Failure("Pemasok tidak ditemukan.");

        var productIds = dto.Details.Select(d => d.ProductId).Distinct().ToList();
        var existingCount = await _context.Products.CountAsync(p => productIds.Contains(p.Id), cancellationToken);
        if (existingCount != productIds.Count)
            return Result<PurchaseOrderDto>.Failure("Produk yang dipilih tidak ditemukan.");

        po.OrderDate = dto.OrderDate;
        po.DueDate = dto.DueDate;
        po.SupplierId = dto.SupplierId;
        po.Notes = ServiceGuard.Clean(dto.Notes);

        _context.PurchaseOrderDetails.RemoveRange(po.Details);
        po.Details.Clear();

        var total = dto.Details.Sum(d => d.Quantity * d.UnitPrice);
        po.TotalAmount = total;
        po.RemainingAmount = Math.Max(0m, total - po.PaidAmount);
        po.PaymentStatus = PaymentStatusCalculator.Calculate(total, po.PaidAmount);

        foreach (var detail in dto.Details)
        {
            po.Details.Add(new PurchaseOrderDetail
            {
                ProductId = detail.ProductId,
                Quantity = detail.Quantity,
                UnitPrice = detail.UnitPrice,
                Subtotal = detail.Quantity * detail.UnitPrice,
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(po.Id, cancellationToken);
    }

    public async Task<Result<PurchaseOrderDto>> ApproveAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ApprovePurchaseOrder))
            return Result<PurchaseOrderDto>.Failure(ServiceGuard.WriteDenied);

        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            return Result<PurchaseOrderDto>.Failure("User yang sedang login tidak dikenali.");

        var po = await _context.PurchaseOrders.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (po is null)
            return Result<PurchaseOrderDto>.Failure("Purchase Order tidak ditemukan.");

        await _context.Entry(po).ReloadAsync(cancellationToken);

        if (po.Status != PurchaseOrderStatus.Draft)
            return Result<PurchaseOrderDto>.Failure("Hanya Purchase Order berstatus Draft yang dapat disetujui.");

        po.Status = PurchaseOrderStatus.Approved;
        po.ApprovedByUserId = userId;

        await _context.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(po.Id, cancellationToken);
    }

    public async Task<Result<PurchaseOrderDto>> ReceiveAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result<PurchaseOrderDto>.Failure(ServiceGuard.WriteDenied);

        var po = await _context.PurchaseOrders
            .Include(p => p.Details)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (po is null)
            return Result<PurchaseOrderDto>.Failure("Purchase Order tidak ditemukan.");

        await _context.Entry(po).ReloadAsync(cancellationToken);
        await _context.Entry(po).Collection(p => p.Details).LoadAsync(cancellationToken);

        if (po.Status == PurchaseOrderStatus.Draft)
            return Result<PurchaseOrderDto>.Failure("Purchase Order harus disetujui terlebih dahulu sebelum barang diterima.");

        if (po.Status == PurchaseOrderStatus.Received)
            return Result<PurchaseOrderDto>.Failure("Purchase Order sudah diterima sebelumnya.");

        if (po.Status == PurchaseOrderStatus.Cancelled)
            return Result<PurchaseOrderDto>.Failure("Purchase Order yang dibatalkan tidak dapat diterima.");

        if (po.Status != PurchaseOrderStatus.Approved)
            return Result<PurchaseOrderDto>.Failure("Hanya Purchase Order berstatus Disetujui yang dapat diterima.");

        await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var productIds = po.Details.Select(d => d.ProductId).Distinct().ToList();
            var products = await _context.Products
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            foreach (var product in products.Values)
            {
                await _context.Entry(product).ReloadAsync(cancellationToken);
            }

            foreach (var detail in po.Details)
            {
                if (!products.TryGetValue(detail.ProductId, out var product))
                    return Result<PurchaseOrderDto>.Failure($"Produk ID {detail.ProductId} tidak ditemukan.");

                var stockBefore = product.Stock;
                product.Stock += detail.Quantity;

                _context.StockCards.Add(new StockCard
                {
                    ProductId = product.Id,
                    MovementDate = DateTime.Today,
                    MovementType = StockMovementType.In,
                    Quantity = detail.Quantity,
                    StockBefore = stockBefore,
                    StockAfter = product.Stock,
                    Reference = po.PONumber,
                });
            }

            po.Status = PurchaseOrderStatus.Received;
            await _context.SaveChangesAsync(cancellationToken);

            await _context.Database.CommitTransactionAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return await GetByIdAsync(po.Id, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            await RollbackAsync(cancellationToken);
            return Result<PurchaseOrderDto>.Failure(ex.Message);
        }
        catch
        {
            await RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<Result<PurchaseOrderDto>> CancelAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result<PurchaseOrderDto>.Failure(ServiceGuard.WriteDenied);

        var po = await _context.PurchaseOrders.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (po is null)
            return Result<PurchaseOrderDto>.Failure("Purchase Order tidak ditemukan.");

        await _context.Entry(po).ReloadAsync(cancellationToken);

        if (po.Status == PurchaseOrderStatus.Received)
            return Result<PurchaseOrderDto>.Failure("Purchase Order yang sudah diterima tidak dapat dibatalkan.");

        if (po.Status == PurchaseOrderStatus.Cancelled)
            return Result<PurchaseOrderDto>.Failure("Purchase Order sudah dibatalkan sebelumnya.");

        if (po.PaidAmount > 0)
            return Result<PurchaseOrderDto>.Failure("Purchase Order tidak dapat dibatalkan karena sudah memiliki pembayaran.");

        if (po.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Approved))
            return Result<PurchaseOrderDto>.Failure("Purchase Order tidak dapat dibatalkan.");

        po.Status = PurchaseOrderStatus.Cancelled;
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(po.Id, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<OutstandingDto>>> GetOutstandingAsync(CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<OutstandingDto>>.Failure(ServiceGuard.ReadDenied);

        var purchaseOrders = await _context.PurchaseOrders.AsNoTracking()
            .Include(p => p.Supplier)
            .Where(p => p.Status == PurchaseOrderStatus.Received && p.RemainingAmount > 0)
            .ToListAsync(cancellationToken);

        var result = purchaseOrders
            .OrderBy(p => p.DueDate ?? DateTime.MaxValue)
            .ThenBy(p => p.OrderDate)
            .Select(p => new OutstandingDto
            {
                ReferenceType = ReferenceType.PurchaseOrder,
                ReferenceId = p.Id,
                ReferenceNumber = p.PONumber,
                PartyName = p.Supplier?.Name ?? string.Empty,
                TransactionDate = p.OrderDate,
                DueDate = p.DueDate,
                TotalAmount = p.TotalAmount,
                PaidAmount = p.PaidAmount,
                RemainingAmount = p.RemainingAmount,
                PaymentStatus = p.PaymentStatus,
            })
            .ToList();

        return Result<IReadOnlyList<OutstandingDto>>.Success(result);
    }

    private async Task RollbackAsync(CancellationToken cancellationToken)
    {
        await _context.Database.RollbackTransactionAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }

    private async Task<Dictionary<string, string>> GetUserNamesAsync(IEnumerable<string?> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        var users = await _context.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(
            u => u.Id,
            u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName ?? u.Id : u.FullName);
    }

    private static PurchaseOrderDto ToDto(PurchaseOrder p, IReadOnlyDictionary<string, string> userNames, bool includeDetails) => new()
    {
        Id = p.Id,
        PONumber = p.PONumber,
        OrderDate = p.OrderDate,
        DueDate = p.DueDate,
        SupplierId = p.SupplierId,
        SupplierName = p.Supplier?.Name ?? string.Empty,
        UserId = p.UserId,
        UserName = userNames.TryGetValue(p.UserId, out var creatorName) ? creatorName : null,
        TotalAmount = p.TotalAmount,
        PaidAmount = p.PaidAmount,
        RemainingAmount = p.RemainingAmount,
        Status = p.Status,
        ApprovedByUserId = p.ApprovedByUserId,
        ApprovedByUserName = p.ApprovedByUserId is not null && userNames.TryGetValue(p.ApprovedByUserId, out var approverName) ? approverName : null,
        PaymentStatus = p.PaymentStatus,
        Notes = p.Notes,
        Details = includeDetails
            ? p.Details
                .OrderBy(d => d.Id)
                .Select(d => new PurchaseOrderDetailDto
                {
                    Id = d.Id,
                    ProductId = d.ProductId,
                    ProductName = d.Product?.Name ?? string.Empty,
                    SKU = d.Product?.SKU,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice,
                    Subtotal = d.Subtotal,
                })
                .ToList()
            : new List<PurchaseOrderDetailDto>(),
    };
}
