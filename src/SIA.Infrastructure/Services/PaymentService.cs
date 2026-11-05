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
/// Pembayaran piutang (Sale) dan utang (PurchaseOrder). Baca: semua role. Tulis: Admin, Staff.
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

    public async Task<Result<IReadOnlyList<PaymentDto>>> GetAllAsync(DateTime? dateFrom = null, DateTime? dateTo = null, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<PaymentDto>>.Failure(ServiceGuard.ReadDenied);

        var query = _context.Payments.AsNoTracking();
        if (dateFrom is not null)
            query = query.Where(p => p.PaymentDate >= dateFrom.Value.Date);
        if (dateTo is not null)
        {
            var end = dateTo.Value.Date.AddDays(1);
            query = query.Where(p => p.PaymentDate < end);
        }

        var payments = await query
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.Id)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PaymentDto>>.Success(await ToDtosAsync(payments, cancellationToken));
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
        IPayable? target;
        switch (dto.ReferenceType)
        {
            case ReferenceType.Sale:
                var sale = await _context.Sales.SingleOrDefaultAsync(s => s.Id == dto.ReferenceId, cancellationToken);
                if (sale is null)
                    return Result<PaymentDto>.Failure("Penjualan yang dirujuk tidak ditemukan.");
                // Context Blazor Server berumur panjang: pastikan sisa tagihan diambil dari nilai terbaru di database
                await _context.Entry(sale).ReloadAsync(cancellationToken);
                target = new SalePayable(sale);
                break;

            case ReferenceType.PurchaseOrder:
                var po = await _context.PurchaseOrders.SingleOrDefaultAsync(p => p.Id == dto.ReferenceId, cancellationToken);
                if (po is null)
                    return Result<PaymentDto>.Failure("Purchase Order yang dirujuk tidak ditemukan.");
                await _context.Entry(po).ReloadAsync(cancellationToken);
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

            return Result<PaymentDto>.Success(ToDto(payment, target.Number));
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

        // IgnoreQueryFilters: nomor dokumen tetap tampil walau dokumen sumber di-soft-delete
        var saleNumbers = saleIds.Count == 0
            ? new Dictionary<int, string>()
            : await _context.Sales.IgnoreQueryFilters().AsNoTracking()
                .Where(s => saleIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.InvoiceNumber, cancellationToken);

        var poNumbers = poIds.Count == 0
            ? new Dictionary<int, string>()
            : await _context.PurchaseOrders.IgnoreQueryFilters().AsNoTracking()
                .Where(p => poIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.PONumber, cancellationToken);

        return payments.Select(p =>
        {
            var numbers = p.ReferenceType == ReferenceType.Sale ? saleNumbers : poNumbers;
            return ToDto(p, numbers.TryGetValue(p.ReferenceId, out var n) ? n : null);
        }).ToList();
    }

    private static PaymentDto ToDto(Payment p, string? referenceNumber) => new()
    {
        Id = p.Id,
        PaymentNumber = p.PaymentNumber,
        PaymentDate = p.PaymentDate,
        PaymentMethod = p.PaymentMethod,
        ReferenceType = p.ReferenceType,
        ReferenceId = p.ReferenceId,
        ReferenceNumber = referenceNumber,
        Amount = p.Amount,
        Notes = p.Notes,
    };

    private static string FormatRupiah(decimal value) =>
        "Rp " + value.ToString("N0", CultureInfo.GetCultureInfo("id-ID"));

    /// <summary>Abstraksi dokumen yang dapat dibayar (Sale / PurchaseOrder).</summary>
    private interface IPayable
    {
        string Number { get; }
        DateTime TransactionDate { get; }
        decimal RemainingAmount { get; }
        void ApplyPayment(decimal amount);
    }

    private sealed class SalePayable : IPayable
    {
        private readonly Domain.Entities.Sales.Sale _sale;
        public SalePayable(Domain.Entities.Sales.Sale sale) => _sale = sale;

        public string Number => _sale.InvoiceNumber;
        public DateTime TransactionDate => _sale.SaleDate;
        public decimal RemainingAmount => _sale.RemainingAmount;

        public void ApplyPayment(decimal amount)
        {
            _sale.PaidAmount += amount;
            _sale.RemainingAmount = _sale.TotalAmount - _sale.PaidAmount;
            _sale.PaymentStatus = PaymentStatusCalculator.Calculate(_sale.TotalAmount, _sale.PaidAmount);
        }
    }

    private sealed class PurchaseOrderPayable : IPayable
    {
        private readonly Domain.Entities.Purchasing.PurchaseOrder _po;
        public PurchaseOrderPayable(Domain.Entities.Purchasing.PurchaseOrder po) => _po = po;

        public string Number => _po.PONumber;
        public DateTime TransactionDate => _po.OrderDate;
        public decimal RemainingAmount => _po.RemainingAmount;

        public void ApplyPayment(decimal amount)
        {
            _po.PaidAmount += amount;
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
