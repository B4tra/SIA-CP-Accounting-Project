using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.Payments;
using SIA.Application.DTOs.Sales;
using SIA.Application.Services.Sales;
using SIA.Application.Services.System;
using SIA.Domain.Entities.Inventory;
using SIA.Domain.Entities.Payments;
using SIA.Domain.Entities.Sales;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Services;

/// <summary>
/// Penjualan. Baca: semua role. Buat: Admin, Staff.
/// Membuat penjualan mengurangi stok dan menulis StockCard (Out) dalam satu transaksi database.
/// Uang yang dibayar saat transaksi dicatat sebagai record Payment agar SUM(Payment) = PaidAmount.
/// Jurnal TIDAK diposting otomatis.
/// </summary>
public class SaleService : ISaleService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITransactionNumberService _transactionNumbers;

    public SaleService(AppDbContext context, ICurrentUserService currentUser, ITransactionNumberService transactionNumbers)
    {
        _context = context;
        _currentUser = currentUser;
        _transactionNumbers = transactionNumbers;
    }

    public async Task<Result<IReadOnlyList<SaleDto>>> GetAllAsync(SaleFilterDto filter, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<SaleDto>>.Failure(ServiceGuard.ReadDenied);

        filter ??= new SaleFilterDto();
        var query = _context.Sales.AsNoTracking().Include(s => s.Customer).AsQueryable();

        if (filter.DateFrom is not null)
            query = query.Where(s => s.SaleDate >= filter.DateFrom.Value.Date);
        if (filter.DateTo is not null)
        {
            var end = filter.DateTo.Value.Date.AddDays(1);
            query = query.Where(s => s.SaleDate < end);
        }
        if (filter.CustomerId is not null)
            query = query.Where(s => s.CustomerId == filter.CustomerId.Value);
        if (filter.PaymentStatus is not null)
            query = query.Where(s => s.PaymentStatus == filter.PaymentStatus.Value);

        var term = ServiceGuard.Clean(filter.Search);
        if (term is not null)
        {
            var pattern = $"%{term}%";
            query = query.Where(s =>
                EF.Functions.Like(s.InvoiceNumber, pattern) ||
                EF.Functions.Like(s.Customer.Name, pattern));
        }

        var sales = await query
            .OrderByDescending(s => s.SaleDate)
            .ThenByDescending(s => s.Id)
            .ToListAsync(cancellationToken);

        var userNames = await GetUserNamesAsync(sales.Select(s => s.UserId), cancellationToken);
        return Result<IReadOnlyList<SaleDto>>.Success(sales.Select(s => ToDto(s, userNames, includeDetails: false)).ToList());
    }

    public async Task<Result<SaleDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<SaleDto>.Failure(ServiceGuard.ReadDenied);

        var sale = await _context.Sales.AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Details).ThenInclude(d => d.Product)
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sale is null)
            return Result<SaleDto>.Failure("Penjualan tidak ditemukan.");

        var userNames = await GetUserNamesAsync(new[] { sale.UserId }, cancellationToken);
        return Result<SaleDto>.Success(ToDto(sale, userNames, includeDetails: true));
    }

    public async Task<Result<SaleDto>> CreateAsync(CreateSaleDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result<SaleDto>.Failure(ServiceGuard.WriteDenied);

        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            return Result<SaleDto>.Failure("User yang sedang login tidak dikenali.");

        var errors = ServiceGuard.Validate(dto).ToList();
        foreach (var detail in dto.Details)
            errors.AddRange(ServiceGuard.Validate(detail));
        if (errors.Count > 0)
            return Result<SaleDto>.Failure(errors.Distinct());

        if (dto.Details.GroupBy(d => d.ProductId).Any(g => g.Count() > 1))
            return Result<SaleDto>.Failure("Produk yang sama tidak boleh muncul lebih dari satu kali dalam satu penjualan.");

        if (!await _context.Customers.AnyAsync(c => c.Id == dto.CustomerId, cancellationToken))
            return Result<SaleDto>.Failure("Pelanggan tidak ditemukan.");

        var productIds = dto.Details.Select(d => d.ProductId).ToList();
        var products = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        foreach (var product in products.Values)
        {
            // Context Blazor Server berumur panjang: pastikan stok diambil dari nilai terbaru di database
            await _context.Entry(product).ReloadAsync(cancellationToken);
        }

        foreach (var detail in dto.Details)
        {
            if (!products.TryGetValue(detail.ProductId, out var product))
            {
                errors.Add("Produk yang dipilih tidak ditemukan.");
                continue;
            }
            if (detail.Quantity > product.Stock)
                errors.Add($"Stok {product.Name} tidak cukup (tersedia {product.Stock}, diminta {detail.Quantity}).");
        }
        if (errors.Count > 0)
            return Result<SaleDto>.Failure(errors);

        var total = dto.Details.Sum(d => d.Quantity * d.UnitPrice);
        if (dto.PaidAmount > total)
            return Result<SaleDto>.Failure("Jumlah dibayar tidak boleh melebihi total penjualan.");

        var isCredit = dto.PaidAmount < total;
        if (isCredit && dto.DueDate is null)
            return Result<SaleDto>.Failure("Tanggal jatuh tempo wajib diisi untuk penjualan kredit (belum lunas).");
        if (dto.DueDate is not null && dto.DueDate.Value.Date < dto.SaleDate.Date)
            return Result<SaleDto>.Failure("Tanggal jatuh tempo tidak boleh sebelum tanggal penjualan.");

        await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Nomor dibuat lebih dulu: GenerateAsync menyimpan sequence (di dalam transaksi yang sama)
            var invoiceNumber = await _transactionNumbers.GenerateAsync(TransactionTypes.Sale, dto.SaleDate, cancellationToken);
            var paymentNumber = dto.PaidAmount > 0
                ? await _transactionNumbers.GenerateAsync(TransactionTypes.Payment, dto.SaleDate, cancellationToken)
                : null;

            var sale = new Sale
            {
                InvoiceNumber = invoiceNumber,
                SaleDate = dto.SaleDate,
                DueDate = dto.DueDate,
                CustomerId = dto.CustomerId,
                UserId = userId,
                TotalAmount = total,
                PaidAmount = dto.PaidAmount,
                RemainingAmount = total - dto.PaidAmount,
                PaymentMethod = dto.PaymentMethod,
                PaymentStatus = PaymentStatusCalculator.Calculate(total, dto.PaidAmount),
                Notes = ServiceGuard.Clean(dto.Notes),
            };

            foreach (var detail in dto.Details)
            {
                var product = products[detail.ProductId];
                var stockBefore = product.Stock;
                product.Stock -= detail.Quantity;

                sale.Details.Add(new SaleDetail
                {
                    ProductId = product.Id,
                    Quantity = detail.Quantity,
                    UnitPrice = detail.UnitPrice,
                    Subtotal = detail.Quantity * detail.UnitPrice,
                });

                _context.StockCards.Add(new StockCard
                {
                    ProductId = product.Id,
                    MovementDate = dto.SaleDate,
                    MovementType = StockMovementType.Out,
                    Quantity = detail.Quantity,
                    StockBefore = stockBefore,
                    StockAfter = product.Stock,
                    Reference = invoiceNumber,
                });
            }

            _context.Sales.Add(sale);
            await _context.SaveChangesAsync(cancellationToken);

            if (paymentNumber is not null)
            {
                _context.Payments.Add(new Payment
                {
                    PaymentNumber = paymentNumber,
                    PaymentDate = dto.SaleDate,
                    PaymentMethod = dto.PaymentMethod,
                    ReferenceType = ReferenceType.Sale,
                    ReferenceId = sale.Id,
                    Amount = dto.PaidAmount,
                    Notes = $"Pembayaran saat transaksi {invoiceNumber}",
                });
                await _context.SaveChangesAsync(cancellationToken);
            }

            await _context.Database.CommitTransactionAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return await GetByIdAsync(sale.Id, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            await RollbackAsync(cancellationToken);
            return Result<SaleDto>.Failure(ex.Message);
        }
        catch
        {
            await RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<Result<IReadOnlyList<OutstandingDto>>> GetOutstandingAsync(CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<OutstandingDto>>.Failure(ServiceGuard.ReadDenied);

        var sales = await _context.Sales.AsNoTracking()
            .Include(s => s.Customer)
            .Where(s => s.PaymentStatus != PaymentStatus.Paid)
            .ToListAsync(cancellationToken);

        var result = sales
            .Where(s => s.RemainingAmount > 0)
            .OrderBy(s => s.DueDate ?? DateTime.MaxValue)
            .ThenBy(s => s.SaleDate)
            .Select(s => new OutstandingDto
            {
                ReferenceType = ReferenceType.Sale,
                ReferenceId = s.Id,
                ReferenceNumber = s.InvoiceNumber,
                PartyName = s.Customer.Name,
                TransactionDate = s.SaleDate,
                DueDate = s.DueDate,
                TotalAmount = s.TotalAmount,
                PaidAmount = s.PaidAmount,
                RemainingAmount = s.RemainingAmount,
                PaymentStatus = s.PaymentStatus,
            })
            .ToList();

        return Result<IReadOnlyList<OutstandingDto>>.Success(result);
    }

    /// <summary>Rollback transaksi dan buang perubahan yang belum tersimpan dari change tracker.</summary>
    private async Task RollbackAsync(CancellationToken cancellationToken)
    {
        await _context.Database.RollbackTransactionAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }

    private async Task<Dictionary<string, string>> GetUserNamesAsync(IEnumerable<string> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        var users = await _context.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(
            u => u.Id,
            u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName ?? u.Id : u.FullName);
    }

    private static SaleDto ToDto(Sale s, IReadOnlyDictionary<string, string> userNames, bool includeDetails) => new()
    {
        Id = s.Id,
        InvoiceNumber = s.InvoiceNumber,
        SaleDate = s.SaleDate,
        DueDate = s.DueDate,
        CustomerId = s.CustomerId,
        CustomerName = s.Customer?.Name ?? string.Empty,
        UserId = s.UserId,
        UserName = userNames.TryGetValue(s.UserId, out var name) ? name : null,
        TotalAmount = s.TotalAmount,
        PaidAmount = s.PaidAmount,
        RemainingAmount = s.RemainingAmount,
        PaymentMethod = s.PaymentMethod,
        PaymentStatus = s.PaymentStatus,
        Notes = s.Notes,
        Details = includeDetails
            ? s.Details
                .OrderBy(d => d.Id)
                .Select(d => new SaleDetailDto
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
            : new List<SaleDetailDto>(),
    };
}
