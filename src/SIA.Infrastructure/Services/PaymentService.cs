using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.Payments;
using SIA.Application.Services.Payments;
using SIA.Application.Services.System;
using SIA.Domain.Entities.Payments;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Services;

/// <summary>
/// Pembayaran piutang (Sale) dan utang (PurchaseOrder).
/// Baca: semua role (Policies.ViewAllData).
/// Tulis: Admin, Staff (Policies.ManageTransactions).
/// Hapus: Admin (Policies.RequireAdmin).
/// Referensi polimorfik (ReferenceType + ReferenceId, tanpa FK) divalidasi di sini; setelah pembayaran,
/// PaidAmount, RemainingAmount, dan PaymentStatus pada dokumen sumber diperbarui.
/// Jurnal TIDAK diposting otomatis.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITransactionNumberService _transactionNumbers;

    public PaymentService(AppDbContext context, ICurrentUserService currentUser, ITransactionNumberService transactionNumbers)
    {
        _context = context;
        _currentUser = currentUser;
        _transactionNumbers = transactionNumbers;
    }

    /// <summary>
    /// Backward-compatibility overload untuk query berbasis rentang tanggal.
    /// </summary>
    public async Task<Result<IReadOnlyList<PaymentDto>>> GetAllAsync(
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        return await GetAllAsync(new PaymentFilterDto
        {
            DateFrom = dateFrom,
            DateTo = dateTo
        }, cancellationToken);
    }

    /// <summary>
    /// Mengambil riwayat pembayaran dengan filter DB (tanggal, jenis referensi, metode)
    /// dan pencarian in-memory multi-kolom (PaymentNumber, ReferenceNumber, PartyName, Notes).
    /// </summary>
    public async Task<Result<IReadOnlyList<PaymentDto>>> GetAllAsync(
        PaymentFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<PaymentDto>>.Failure(ServiceGuard.ReadDenied);

        filter ??= new PaymentFilterDto();

        var query = _context.Payments.AsNoTracking();

        if (filter.DateFrom is not null)
            query = query.Where(p => p.PaymentDate >= filter.DateFrom.Value.Date);

        if (filter.DateTo is not null)
        {
            var end = filter.DateTo.Value.Date.AddDays(1);
            query = query.Where(p => p.PaymentDate < end);
        }

        if (filter.ReferenceType is not null)
            query = query.Where(p => p.ReferenceType == filter.ReferenceType.Value);

        if (filter.PaymentMethod is not null)
            query = query.Where(p => p.PaymentMethod == filter.PaymentMethod.Value);

        var payments = await query
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.Id)
            .ToListAsync(cancellationToken);

        var dtos = await ToDtosAsync(payments, cancellationToken);

        var term = ServiceGuard.Clean(filter.Search);
        if (term is not null)
        {
            dtos = dtos.Where(p =>
                p.PaymentNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (p.ReferenceNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.PartyName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.Notes?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            ).ToList();
        }

        return Result<IReadOnlyList<PaymentDto>>.Success(dtos);
    }

    public async Task<Result<PaymentDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<PaymentDto>.Failure(ServiceGuard.ReadDenied);

        var payment = await _context.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (payment is null)
            return Result<PaymentDto>.Failure("Pembayaran tidak ditemukan.");

        var dtos = await ToDtosAsync(new[] { payment }, cancellationToken);
        return Result<PaymentDto>.Success(dtos[0]);
    }

    public async Task<Result<IReadOnlyList<PaymentDto>>> GetByReferenceAsync(
        ReferenceType referenceType,
        int referenceId,
        CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<PaymentDto>>.Failure(ServiceGuard.ReadDenied);

        var payments = await _context.Payments.AsNoTracking()
            .Where(p => p.ReferenceType == referenceType && p.ReferenceId == referenceId)
            .OrderBy(p => p.PaymentDate)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PaymentDto>>.Success(await ToDtosAsync(payments, cancellationToken));
    }

    public async Task<Result<PaymentDto>> CreateAsync(CreatePaymentDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result<PaymentDto>.Failure(ServiceGuard.WriteDenied);

        var errors = ServiceGuard.Validate(dto);
        if (errors.Count > 0)
            return Result<PaymentDto>.Failure(errors);

        // Validasi referensi polimorfik (tanpa FK) dan ambil dokumen sumber yang akan diperbarui
        IPayable target;
        switch (dto.ReferenceType)
        {
            case ReferenceType.Sale:
                var sale = await _context.Sales
                    .Include(s => s.Customer)
                    .SingleOrDefaultAsync(s => s.Id == dto.ReferenceId, cancellationToken);
                if (sale is null)
                    return Result<PaymentDto>.Failure("Penjualan yang dirujuk tidak ditemukan.");
                // Context Blazor Server berumur panjang: pastikan sisa tagihan diambil dari nilai terbaru di database
                await _context.Entry(sale).ReloadAsync(cancellationToken);
                if (sale.Customer is null && sale.CustomerId > 0)
                    await _context.Entry(sale).Reference(s => s.Customer).LoadAsync(cancellationToken);
                target = new SalePayable(sale);
                break;

            case ReferenceType.PurchaseOrder:
                var po = await _context.PurchaseOrders
                    .Include(p => p.Supplier)
                    .SingleOrDefaultAsync(p => p.Id == dto.ReferenceId, cancellationToken);
                if (po is null)
                    return Result<PaymentDto>.Failure("Purchase Order yang dirujuk tidak ditemukan.");
                await _context.Entry(po).ReloadAsync(cancellationToken);
                if (po.Supplier is null && po.SupplierId > 0)
                    await _context.Entry(po).Reference(p => p.Supplier).LoadAsync(cancellationToken);
                if (po.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Cancelled)
                    return Result<PaymentDto>.Failure("Pembayaran hanya dapat dicatat untuk Purchase Order yang sudah disetujui atau diterima.");
                target = new PurchaseOrderPayable(po);
                break;

            default:
                return Result<PaymentDto>.Failure("Jenis referensi pembayaran tidak dikenal.");
        }

        if (target.RemainingAmount <= 0)
            return Result<PaymentDto>.Failure($"Tagihan {target.Number} sudah lunas.");
        if (dto.Amount > target.RemainingAmount)
            return Result<PaymentDto>.Failure(
                $"Jumlah pembayaran melebihi sisa tagihan {target.Number} ({FormatRupiah(target.RemainingAmount)}).");
        if (dto.PaymentDate.Date < target.TransactionDate.Date)
            return Result<PaymentDto>.Failure("Tanggal pembayaran tidak boleh sebelum tanggal transaksi.");

        var ownsTransaction = _context.Database.CurrentTransaction is null;
        if (ownsTransaction)
            await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var number = await _transactionNumbers.GenerateAsync(TransactionTypes.Payment, dto.PaymentDate, cancellationToken);

            var payment = new Payment
            {
                PaymentNumber = number,
                PaymentDate = dto.PaymentDate,
                PaymentMethod = dto.PaymentMethod,
                ReferenceType = dto.ReferenceType,
                ReferenceId = dto.ReferenceId,
                Amount = dto.Amount,
                Notes = ServiceGuard.Clean(dto.Notes),
            };
            _context.Payments.Add(payment);

            target.ApplyPayment(dto.Amount);

            await _context.SaveChangesAsync(cancellationToken);
            if (ownsTransaction)
                await _context.Database.CommitTransactionAsync(cancellationToken);

            return Result<PaymentDto>.Success(ToDto(payment, target.Number, target.PartyName));
        }
        catch (InvalidOperationException ex)
        {
            await RollbackAsync(ownsTransaction, cancellationToken);
            return Result<PaymentDto>.Failure(ex.Message);
        }
        catch
        {
            await RollbackAsync(ownsTransaction, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Mengambil daftar piutang penjualan dan utang PO yang belum lunas (RemainingAmount > 0).
    /// Untuk PO, hanya menyertakan status Approved atau Received (Draft dan Cancelled diabaikan).
    /// Mendukung filter ReferenceType, OnlyOverdue, Search, serta pengurutan jatuh tempo teratas.
    /// </summary>
    public async Task<Result<IReadOnlyList<OutstandingDto>>> GetOutstandingAsync(
        OutstandingFilterDto? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<OutstandingDto>>.Failure(ServiceGuard.ReadDenied);

        filter ??= new OutstandingFilterDto();

        var salesList = new List<OutstandingDto>();
        var poList = new List<OutstandingDto>();

        if (filter.ReferenceType is null or ReferenceType.Sale)
        {
            salesList = await _context.Sales.AsNoTracking()
                .Include(s => s.Customer)
                .Where(s => s.RemainingAmount > 0)
                .Select(s => new OutstandingDto
                {
                    ReferenceType = ReferenceType.Sale,
                    ReferenceId = s.Id,
                    ReferenceNumber = s.InvoiceNumber,
                    PartyName = s.Customer != null ? s.Customer.Name : string.Empty,
                    TransactionDate = s.SaleDate,
                    DueDate = s.DueDate,
                    TotalAmount = s.TotalAmount,
                    PaidAmount = s.PaidAmount,
                    RemainingAmount = s.RemainingAmount,
                    PaymentStatus = s.PaymentStatus,
                    PurchaseOrderStatus = null,
                })
                .ToListAsync(cancellationToken);
        }

        if (filter.ReferenceType is null or ReferenceType.PurchaseOrder)
        {
            poList = await _context.PurchaseOrders.AsNoTracking()
                .Include(p => p.Supplier)
                .Where(p => (p.Status == PurchaseOrderStatus.Approved || p.Status == PurchaseOrderStatus.Received)
                            && p.RemainingAmount > 0)
                .Select(p => new OutstandingDto
                {
                    ReferenceType = ReferenceType.PurchaseOrder,
                    ReferenceId = p.Id,
                    ReferenceNumber = p.PONumber,
                    PartyName = p.Supplier != null ? p.Supplier.Name : string.Empty,
                    TransactionDate = p.OrderDate,
                    DueDate = p.DueDate,
                    TotalAmount = p.TotalAmount,
                    PaidAmount = p.PaidAmount,
                    RemainingAmount = p.RemainingAmount,
                    PaymentStatus = p.PaymentStatus,
                    PurchaseOrderStatus = p.Status,
                })
                .ToListAsync(cancellationToken);
        }

        IEnumerable<OutstandingDto> items = salesList.Concat(poList);

        var today = DateTime.Today;
        if (filter.OnlyOverdue)
        {
            items = items.Where(x => x.DueDate.HasValue && x.DueDate.Value.Date < today);
        }

        var term = ServiceGuard.Clean(filter.Search);
        if (term is not null)
        {
            items = items.Where(x =>
                x.ReferenceNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.PartyName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var sorted = items
            .OrderBy(x => x.DueDate.HasValue && x.DueDate.Value.Date < today ? 0 : 1)
            .ThenBy(x => x.DueDate ?? DateTime.MaxValue)
            .ThenBy(x => x.TransactionDate)
            .ToList();

        return Result<IReadOnlyList<OutstandingDto>>.Success(sorted);
    }

    /// <summary>
    /// Menghapus pembayaran (soft delete) dan memulihkan PaidAmount, RemainingAmount, serta PaymentStatus
    /// pada dokumen sumber (Sale atau PurchaseOrder) dalam transaksi atomik.
    /// Khusus Admin (Policies.RequireAdmin). Tidak membuat jurnal otomatis.
    /// </summary>
    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.RequireAdmin))
            return Result.Failure(ServiceGuard.WriteDenied);

        var payment = await _context.Payments.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (payment is null)
            return Result.Failure("Pembayaran tidak ditemukan.");

        IPayable target;
        switch (payment.ReferenceType)
        {
            case ReferenceType.Sale:
                var sale = await _context.Sales.SingleOrDefaultAsync(s => s.Id == payment.ReferenceId, cancellationToken);
                if (sale is null)
                    return Result.Failure("Penjualan yang dirujuk tidak ditemukan.");
                await _context.Entry(sale).ReloadAsync(cancellationToken);
                target = new SalePayable(sale);
                break;

            case ReferenceType.PurchaseOrder:
                var po = await _context.PurchaseOrders.SingleOrDefaultAsync(p => p.Id == payment.ReferenceId, cancellationToken);
                if (po is null)
                    return Result.Failure("Purchase Order yang dirujuk tidak ditemukan.");
                await _context.Entry(po).ReloadAsync(cancellationToken);
                target = new PurchaseOrderPayable(po);
                break;

            default:
                return Result.Failure("Jenis referensi pembayaran tidak dikenal.");
        }

        var ownsTransaction = _context.Database.CurrentTransaction is null;
        if (ownsTransaction)
            await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            _context.Payments.Remove(payment); // Dikonversi menjadi soft delete oleh AppDbContext
            target.ReversePayment(payment.Amount);

            await _context.SaveChangesAsync(cancellationToken);
            if (ownsTransaction)
                await _context.Database.CommitTransactionAsync(cancellationToken);

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            await RollbackAsync(ownsTransaction, cancellationToken);
            return Result.Failure(ex.Message);
        }
        catch
        {
            await RollbackAsync(ownsTransaction, cancellationToken);
            throw;
        }
    }

    /// <summary>Rollback transaksi dan buang perubahan yang belum tersimpan dari change tracker.</summary>
    private async Task RollbackAsync(bool ownsTransaction, CancellationToken cancellationToken)
    {
        if (ownsTransaction)
            await _context.Database.RollbackTransactionAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }

    private async Task<IReadOnlyList<PaymentDto>> ToDtosAsync(IReadOnlyCollection<Payment> payments, CancellationToken cancellationToken)
    {
        var saleIds = payments.Where(p => p.ReferenceType == ReferenceType.Sale).Select(p => p.ReferenceId).Distinct().ToList();
        var poIds = payments.Where(p => p.ReferenceType == ReferenceType.PurchaseOrder).Select(p => p.ReferenceId).Distinct().ToList();

        // IgnoreQueryFilters: nomor dokumen dan nama pihak tetap tampil walau dokumen/pihak sumber di-soft-delete
        var saleInfo = saleIds.Count == 0
            ? new Dictionary<int, (string Number, string? PartyName)>()
            : await _context.Sales.IgnoreQueryFilters().AsNoTracking()
                .Where(s => saleIds.Contains(s.Id))
                .Select(s => new
                {
                    s.Id,
                    Number = s.InvoiceNumber,
                    PartyName = s.Customer != null ? s.Customer.Name : null
                })
                .ToDictionaryAsync(s => s.Id, s => (s.Number, s.PartyName), cancellationToken);

        var poInfo = poIds.Count == 0
            ? new Dictionary<int, (string Number, string? PartyName)>()
            : await _context.PurchaseOrders.IgnoreQueryFilters().AsNoTracking()
                .Where(p => poIds.Contains(p.Id))
                .Select(p => new
                {
                    p.Id,
                    Number = p.PONumber,
                    PartyName = p.Supplier != null ? p.Supplier.Name : null
                })
                .ToDictionaryAsync(p => p.Id, p => (p.Number, p.PartyName), cancellationToken);

        return payments.Select(p =>
        {
            var info = p.ReferenceType == ReferenceType.Sale ? saleInfo : poInfo;
            var found = info.TryGetValue(p.ReferenceId, out var item);
            return ToDto(p, found ? item.Number : null, found ? item.PartyName : null);
        }).ToList();
    }

    private static PaymentDto ToDto(Payment p, string? referenceNumber, string? partyName = null) => new()
    {
        Id = p.Id,
        PaymentNumber = p.PaymentNumber,
        PaymentDate = p.PaymentDate,
        PaymentMethod = p.PaymentMethod,
        ReferenceType = p.ReferenceType,
        ReferenceId = p.ReferenceId,
        ReferenceNumber = referenceNumber,
        PartyName = partyName,
        Amount = p.Amount,
        Notes = p.Notes,
    };

    private static string FormatRupiah(decimal value) =>
        "Rp " + value.ToString("N0", CultureInfo.GetCultureInfo("id-ID"));

    /// <summary>Abstraksi dokumen yang dapat dibayar (Sale / PurchaseOrder).</summary>
    private interface IPayable
    {
        string Number { get; }
        string PartyName { get; }
        DateTime TransactionDate { get; }
        decimal RemainingAmount { get; }
        void ApplyPayment(decimal amount);
        void ReversePayment(decimal amount);
    }

    private sealed class SalePayable : IPayable
    {
        private readonly Domain.Entities.Sales.Sale _sale;
        public SalePayable(Domain.Entities.Sales.Sale sale) => _sale = sale;

        public string Number => _sale.InvoiceNumber;
        public string PartyName => _sale.Customer?.Name ?? string.Empty;
        public DateTime TransactionDate => _sale.SaleDate;
        public decimal RemainingAmount => _sale.RemainingAmount;

        public void ApplyPayment(decimal amount)
        {
            _sale.PaidAmount += amount;
            _sale.RemainingAmount = _sale.TotalAmount - _sale.PaidAmount;
            _sale.PaymentStatus = PaymentStatusCalculator.Calculate(_sale.TotalAmount, _sale.PaidAmount);
        }

        public void ReversePayment(decimal amount)
        {
            _sale.PaidAmount = Math.Max(0m, _sale.PaidAmount - amount);
            _sale.RemainingAmount = _sale.TotalAmount - _sale.PaidAmount;
            _sale.PaymentStatus = PaymentStatusCalculator.Calculate(_sale.TotalAmount, _sale.PaidAmount);
        }
    }

    private sealed class PurchaseOrderPayable : IPayable
    {
        private readonly Domain.Entities.Purchasing.PurchaseOrder _po;
        public PurchaseOrderPayable(Domain.Entities.Purchasing.PurchaseOrder po) => _po = po;

        public string Number => _po.PONumber;
        public string PartyName => _po.Supplier?.Name ?? string.Empty;
        public DateTime TransactionDate => _po.OrderDate;
        public decimal RemainingAmount => _po.RemainingAmount;

        public void ApplyPayment(decimal amount)
        {
            _po.PaidAmount += amount;
            _po.RemainingAmount = _po.TotalAmount - _po.PaidAmount;
            _po.PaymentStatus = PaymentStatusCalculator.Calculate(_po.TotalAmount, _po.PaidAmount);
        }

        public void ReversePayment(decimal amount)
        {
            _po.PaidAmount = Math.Max(0m, _po.PaidAmount - amount);
            _po.RemainingAmount = _po.TotalAmount - _po.PaidAmount;
            _po.PaymentStatus = PaymentStatusCalculator.Calculate(_po.TotalAmount, _po.PaidAmount);
        }
    }
}

/// <summary>Aturan status pembayaran bersama untuk Sale dan PurchaseOrder.</summary>
internal static class PaymentStatusCalculator
{
    /// <summary>Paid jika dibayar ≥ total; Unpaid jika belum ada pembayaran; selain itu PartiallyPaid.</summary>
    public static PaymentStatus Calculate(decimal totalAmount, decimal paidAmount)
    {
        if (paidAmount >= totalAmount) return PaymentStatus.Paid;
        if (paidAmount <= 0) return PaymentStatus.Unpaid;
        return PaymentStatus.PartiallyPaid;
    }
}
