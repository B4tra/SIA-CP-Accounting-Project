using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.Inventory;
using SIA.Application.DTOs.Payments;
using SIA.Application.DTOs.Purchasing;
using SIA.Application.DTOs.Reporting;
using SIA.Application.DTOs.Sales;
using SIA.Application.Services.Reporting;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Services;

/// <summary>
/// Layanan agregasi data Dashboard operasional dan finansial.
/// Otorisasi dasar (KPI operasional, tren, top produk, panel): Policies.ViewAllData (Admin, Staff, Viewer).
/// Otorisasi finansial (Saldo Kas & Bank, Laba Bersih): Policies.ViewReports (Admin, Viewer).
/// </summary>
public class DashboardService : IDashboardService
{
    private sealed record DatedAmount(DateTime Date, decimal Amount);

    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IFinancialReportService _financialReportService;

    public DashboardService(
        AppDbContext context,
        ICurrentUserService currentUser,
        IFinancialReportService financialReportService)
    {
        _context = context;
        _currentUser = currentUser;
        _financialReportService = financialReportService;
    }

    public async Task<Result<DashboardSummaryDto>> GetSummaryAsync(
        DashboardTrendPeriod period = DashboardTrendPeriod.Last7Days,
        CancellationToken cancellationToken = default)
    {
        // 1. Verifikasi Otorisasi Server-Side
        if (!_currentUser.IsAuthenticated || !ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<DashboardSummaryDto>.Failure(ServiceGuard.ReadDenied);

        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        var firstDayOfMonth = new DateTime(today.Year, today.Month, 1);
        var firstDayOfNextMonth = firstDayOfMonth.AddMonths(1);
        var lastDayOfMonth = firstDayOfNextMonth.AddDays(-1);

        var dto = new DashboardSummaryDto
        {
            TrendPeriod = period,
            CanViewFinancials = ServiceGuard.Has(_currentUser, Policies.ViewReports)
        };

        // 2. KPI Penjualan Bersih (Hari Ini & Bulan Berjalan)
        var todaySales = await _context.Sales.AsNoTracking()
            .Where(s => s.SaleDate >= today && s.SaleDate < tomorrow)
            .Select(s => s.TotalAmount)
            .ToListAsync(cancellationToken);
        var todayReturnAmounts = await _context.SaleReturns.AsNoTracking()
            .Where(r => r.ReturnDate >= today && r.ReturnDate < tomorrow)
            .Select(r => r.TotalAmount)
            .ToListAsync(cancellationToken);

        dto.TodaySaleCount = todaySales.Count;
        dto.TodayNetSales = todaySales.Sum() - todayReturnAmounts.Sum();

        var monthSales = await _context.Sales.AsNoTracking()
            .Where(s => s.SaleDate >= firstDayOfMonth && s.SaleDate < firstDayOfNextMonth)
            .Select(s => s.TotalAmount)
            .ToListAsync(cancellationToken);
        var monthReturnAmounts = await _context.SaleReturns.AsNoTracking()
            .Where(r => r.ReturnDate >= firstDayOfMonth && r.ReturnDate < firstDayOfNextMonth)
            .Select(r => r.TotalAmount)
            .ToListAsync(cancellationToken);

        dto.MonthSaleCount = monthSales.Count;
        dto.MonthNetSales = monthSales.Sum() - monthReturnAmounts.Sum();

        // 3. KPI Piutang (Sale dengan RemainingAmount > 0)
        var unpaidSales = await _context.Sales.AsNoTracking()
            .Include(s => s.Customer)
            .Where(s => s.RemainingAmount > 0)
            .ToListAsync(cancellationToken);

        dto.TotalReceivables = unpaidSales.Sum(s => s.RemainingAmount);
        dto.UnpaidSaleCount = unpaidSales.Count;

        var overdueSales = unpaidSales
            .Where(s => s.DueDate.HasValue && s.DueDate.Value.Date < today)
            .ToList();
        dto.OverdueReceivableCount = overdueSales.Count;
        dto.OverdueReceivablesAmount = overdueSales.Sum(s => s.RemainingAmount);

        // 4. KPI Utang (PO Status Received dengan RemainingAmount > 0) & Antrean PO
        var unpaidPos = await _context.PurchaseOrders.AsNoTracking()
            .Include(p => p.Supplier)
            .Where(p => p.Status == PurchaseOrderStatus.Received && p.RemainingAmount > 0)
            .ToListAsync(cancellationToken);

        dto.TotalPayables = unpaidPos.Sum(p => p.RemainingAmount);
        dto.UnpaidPoCount = unpaidPos.Count;

        var overduePos = unpaidPos
            .Where(p => p.DueDate.HasValue && p.DueDate.Value.Date < today)
            .ToList();
        dto.OverduePayableCount = overduePos.Count;
        dto.OverduePayablesAmount = overduePos.Sum(p => p.RemainingAmount);

        dto.DraftPoCount = await _context.PurchaseOrders.AsNoTracking()
            .CountAsync(p => p.Status == PurchaseOrderStatus.Draft, cancellationToken);
        dto.ApprovedPoCount = await _context.PurchaseOrders.AsNoTracking()
            .CountAsync(p => p.Status == PurchaseOrderStatus.Approved, cancellationToken);

        // 5. KPI Status Stok Aki
        dto.LowStockProductCount = await _context.Products.AsNoTracking()
            .CountAsync(p => p.Stock <= p.ReorderPoint, cancellationToken);
        dto.OutOfStockProductCount = await _context.Products.AsNoTracking()
            .CountAsync(p => p.Stock == 0, cancellationToken);
        dto.TotalActiveProductCount = await _context.Products.AsNoTracking()
            .CountAsync(cancellationToken);

        // 6. KPI Keuangan (Khusus Admin & Viewer)
        if (dto.CanViewFinancials)
        {
            var cashAndBankBalances = await _context.Accounts.AsNoTracking()
                .Where(a => a.Code == "1-1001" || a.Code == "1-1002")
                .Select(a => a.Balance)
                .ToListAsync(cancellationToken);
            dto.CashAndBankBalance = cashAndBankBalances.Sum();

            var incomeResult = await _financialReportService.GetIncomeStatementAsync(
                firstDayOfMonth, lastDayOfMonth, cancellationToken);

            if (incomeResult is { IsSuccess: true, Value: not null })
            {
                var inc = incomeResult.Value;
                dto.CurrentMonthNetIncome = inc.NetIncome;
                dto.CurrentMonthRevenue = (inc.Revenue?.Total ?? 0m) + (inc.OtherRevenue?.Total ?? 0m);
                dto.CurrentMonthExpenses = (inc.CostOfGoodsSold?.Total ?? 0m) + (inc.OperatingExpenses?.Total ?? 0m) + (inc.OtherExpenses?.Total ?? 0m);
            }
            else
            {
                dto.CurrentMonthNetIncome = 0m;
                dto.CurrentMonthRevenue = 0m;
                dto.CurrentMonthExpenses = 0m;
            }
        }

        // 7. Data Visualisasi Tren (Sesuai Periode yang Dipilih)
        DateTime periodStart;
        DateTime periodEnd;

        switch (period)
        {
            case DashboardTrendPeriod.ThisMonth:
                periodStart = firstDayOfMonth;
                periodEnd = firstDayOfNextMonth;
                break;
            case DashboardTrendPeriod.ThisYear:
                periodStart = new DateTime(today.Year, 1, 1);
                periodEnd = new DateTime(today.Year + 1, 1, 1);
                break;
            case DashboardTrendPeriod.Last7Days:
            default:
                periodStart = today.AddDays(-6);
                periodEnd = tomorrow;
                break;
        }

        // Query transaksi periode untuk tren
        var periodSales = await _context.Sales.AsNoTracking()
            .Where(s => s.SaleDate >= periodStart && s.SaleDate < periodEnd)
            .Select(s => new DatedAmount(s.SaleDate, s.TotalAmount))
            .ToListAsync(cancellationToken);

        var periodSaleReturns = await _context.SaleReturns.AsNoTracking()
            .Where(r => r.ReturnDate >= periodStart && r.ReturnDate < periodEnd)
            .Select(r => new DatedAmount(r.ReturnDate, r.TotalAmount))
            .ToListAsync(cancellationToken);

        var periodPurchases = await _context.PurchaseOrders.AsNoTracking()
            .Where(p => p.Status == PurchaseOrderStatus.Received && p.OrderDate >= periodStart && p.OrderDate < periodEnd)
            .Select(p => new DatedAmount(p.OrderDate, p.TotalAmount))
            .ToListAsync(cancellationToken);

        var periodPurchaseReturns = await _context.PurchaseReturns.AsNoTracking()
            .Where(r => r.ReturnDate >= periodStart && r.ReturnDate < periodEnd)
            .Select(r => new DatedAmount(r.ReturnDate, r.TotalAmount))
            .ToListAsync(cancellationToken);

        dto.TrendPoints = BuildTrendPoints(
            period, today, periodSales, periodSaleReturns, periodPurchases, periodPurchaseReturns);

        // 8. Top 5 Produk Aki Terlaris pada Periode Terpilih
        dto.TopProducts = await BuildTopProductsAsync(periodStart, periodEnd, cancellationToken);

        // 9. Panel 1: Peringatan Stok Minimum (Top 10 terendah)
        dto.LowStockAlerts = await _context.Products.AsNoTracking()
            .Where(p => p.Stock <= p.ReorderPoint)
            .OrderBy(p => p.Stock)
            .ThenBy(p => p.Name)
            .Take(10)
            .Select(p => new ReorderAlertDto
            {
                ProductId = p.Id,
                ProductName = p.Name,
                SKU = p.SKU,
                Brand = p.Brand,
                Stock = p.Stock,
                ReorderPoint = p.ReorderPoint
            })
            .ToListAsync(cancellationToken);

        // 10. Panel 2: Piutang & Utang Jatuh Tempo (Overdue teratas, disusul tanggal jatuh tempo terdekat)
        var saleOutstanding = unpaidSales.Select(s => new OutstandingDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = s.Id,
            ReferenceNumber = s.InvoiceNumber,
            PartyName = s.Customer?.Name ?? string.Empty,
            TransactionDate = s.SaleDate,
            DueDate = s.DueDate,
            TotalAmount = s.TotalAmount,
            PaidAmount = s.PaidAmount,
            RemainingAmount = s.RemainingAmount,
            PaymentStatus = s.PaymentStatus
        });

        var poOutstanding = unpaidPos.Select(p => new OutstandingDto
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
            PaymentStatus = p.PaymentStatus
        });

        dto.DueAndOverdueItems = saleOutstanding.Concat(poOutstanding)
            .OrderBy(item => item.DueDate.HasValue && item.DueDate.Value.Date < today ? 0 : 1)
            .ThenBy(item => item.DueDate ?? DateTime.MaxValue)
            .ThenBy(item => item.TransactionDate)
            .Take(10)
            .ToList();

        // 11. Panel 3: Antrean Purchase Order (Draft & Approved, top 10)
        var pendingPOs = await _context.PurchaseOrders.AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Details)
                .ThenInclude(d => d.Product)
            .Where(p => p.Status == PurchaseOrderStatus.Draft || p.Status == PurchaseOrderStatus.Approved)
            .OrderBy(p => p.Status == PurchaseOrderStatus.Draft ? 0 : 1)
            .ThenByDescending(p => p.OrderDate)
            .Take(10)
            .ToListAsync(cancellationToken);

        var pendingUserIds = pendingPOs.Select(p => p.UserId)
            .Concat(pendingPOs.Select(p => p.ApprovedByUserId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        var pendingUserNames = pendingUserIds.Count > 0
            ? await _context.Users.AsNoTracking()
                .Where(u => pendingUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName ?? u.Id : u.FullName, cancellationToken)
            : new Dictionary<string, string>();

        dto.PendingPurchaseOrders = pendingPOs.Select(p => new PurchaseOrderDto
        {
            Id = p.Id,
            PONumber = p.PONumber,
            OrderDate = p.OrderDate,
            DueDate = p.DueDate,
            SupplierId = p.SupplierId,
            SupplierName = p.Supplier?.Name ?? string.Empty,
            UserId = p.UserId,
            UserName = pendingUserNames.TryGetValue(p.UserId, out var creatorName) ? creatorName : null,
            TotalAmount = p.TotalAmount,
            PaidAmount = p.PaidAmount,
            RemainingAmount = p.RemainingAmount,
            Status = p.Status,
            ApprovedByUserId = p.ApprovedByUserId,
            ApprovedByUserName = p.ApprovedByUserId is not null && pendingUserNames.TryGetValue(p.ApprovedByUserId, out var approverName) ? approverName : null,
            PaymentStatus = p.PaymentStatus,
            Notes = p.Notes,
            Details = p.Details.Select(d => new PurchaseOrderDetailDto
            {
                Id = d.Id,
                ProductId = d.ProductId,
                ProductName = d.Product?.Name ?? string.Empty,
                SKU = d.Product?.SKU,
                Quantity = d.Quantity,
                UnitPrice = d.UnitPrice,
                Subtotal = d.Subtotal
            }).ToList()
        }).ToList();

        // 12. Panel 4: 5 Transaksi Penjualan Terbaru
        var recentSales = await _context.Sales.AsNoTracking()
            .Include(s => s.Customer)
            .OrderByDescending(s => s.SaleDate)
            .ThenByDescending(s => s.Id)
            .Take(5)
            .ToListAsync(cancellationToken);

        dto.RecentSales = recentSales.Select(s => new SaleDto
        {
            Id = s.Id,
            InvoiceNumber = s.InvoiceNumber,
            SaleDate = s.SaleDate,
            DueDate = s.DueDate,
            CustomerId = s.CustomerId,
            CustomerName = s.Customer?.Name ?? string.Empty,
            UserId = s.UserId,
            TotalAmount = s.TotalAmount,
            PaidAmount = s.PaidAmount,
            RemainingAmount = s.RemainingAmount,
            PaymentMethod = s.PaymentMethod,
            PaymentStatus = s.PaymentStatus,
            Notes = s.Notes
        }).ToList();

        return Result<DashboardSummaryDto>.Success(dto);
    }

    private static List<DashboardTrendPointDto> BuildTrendPoints(
        DashboardTrendPeriod period,
        DateTime today,
        IReadOnlyList<DatedAmount> sales,
        IReadOnlyList<DatedAmount> saleReturns,
        IReadOnlyList<DatedAmount> purchases,
        IReadOnlyList<DatedAmount> purchaseReturns)
    {
        var points = new List<DashboardTrendPointDto>();

        if (period == DashboardTrendPeriod.Last7Days)
        {
            for (int i = 6; i >= 0; i--)
            {
                var day = today.AddDays(-i);
                var nextDay = day.AddDays(1);
                var label = day.ToString("dd/MM");

                var netSales = sales.Where(s => s.Date >= day && s.Date < nextDay).Sum(s => s.Amount)
                             - saleReturns.Where(r => r.Date >= day && r.Date < nextDay).Sum(r => r.Amount);
                var netPurchases = purchases.Where(p => p.Date >= day && p.Date < nextDay).Sum(p => p.Amount)
                                 - purchaseReturns.Where(r => r.Date >= day && r.Date < nextDay).Sum(r => r.Amount);

                points.Add(new DashboardTrendPointDto
                {
                    Label = label,
                    NetSales = netSales,
                    NetPurchases = netPurchases
                });
            }
        }
        else if (period == DashboardTrendPeriod.ThisMonth)
        {
            int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
            var weekRanges = new List<(int Start, int End, string Label)>
            {
                (1, 7, "Mgg 1 (1-7)"),
                (8, 14, "Mgg 2 (8-14)"),
                (15, 21, "Mgg 3 (15-21)"),
                (22, 28, "Mgg 4 (22-28)")
            };

            if (daysInMonth > 28)
            {
                weekRanges.Add((29, daysInMonth, $"Mgg 5 (29-{daysInMonth})"));
            }

            foreach (var w in weekRanges)
            {
                var start = new DateTime(today.Year, today.Month, w.Start);
                var end = new DateTime(today.Year, today.Month, w.End).AddDays(1);

                var netSales = sales.Where(s => s.Date >= start && s.Date < end).Sum(s => s.Amount)
                             - saleReturns.Where(r => r.Date >= start && r.Date < end).Sum(r => r.Amount);
                var netPurchases = purchases.Where(p => p.Date >= start && p.Date < end).Sum(p => p.Amount)
                                 - purchaseReturns.Where(r => r.Date >= start && r.Date < end).Sum(r => r.Amount);

                points.Add(new DashboardTrendPointDto
                {
                    Label = w.Label,
                    NetSales = netSales,
                    NetPurchases = netPurchases
                });
            }
        }
        else // ThisYear
        {
            string[] monthLabels = { "Jan", "Feb", "Mar", "Apr", "Mei", "Jun", "Jul", "Agu", "Sep", "Okt", "Nov", "Des" };
            for (int m = 1; m <= 12; m++)
            {
                var start = new DateTime(today.Year, m, 1);
                var end = start.AddMonths(1);

                var netSales = sales.Where(s => s.Date >= start && s.Date < end).Sum(s => s.Amount)
                             - saleReturns.Where(r => r.Date >= start && r.Date < end).Sum(r => r.Amount);
                var netPurchases = purchases.Where(p => p.Date >= start && p.Date < end).Sum(p => p.Amount)
                                 - purchaseReturns.Where(r => r.Date >= start && r.Date < end).Sum(r => r.Amount);

                points.Add(new DashboardTrendPointDto
                {
                    Label = monthLabels[m - 1],
                    NetSales = netSales,
                    NetPurchases = netPurchases
                });
            }
        }

        return points;
    }

    private async Task<List<TopProductDto>> BuildTopProductsAsync(
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken cancellationToken)
    {
        var saleDetails = await _context.SaleDetails.AsNoTracking()
            .Where(d => d.Sale.SaleDate >= periodStart && d.Sale.SaleDate < periodEnd)
            .Select(d => new
            {
                d.ProductId,
                d.Product.Name,
                d.Product.SKU,
                d.Product.Brand,
                d.Quantity,
                d.Subtotal
            })
            .ToListAsync(cancellationToken);

        var returnDetails = await _context.SaleReturnDetails.AsNoTracking()
            .Where(d => d.SaleReturn.ReturnDate >= periodStart && d.SaleReturn.ReturnDate < periodEnd)
            .Select(d => new
            {
                d.ProductId,
                d.Quantity,
                d.Subtotal
            })
            .ToListAsync(cancellationToken);

        var productIds = saleDetails.Select(d => d.ProductId).Distinct().ToList();
        var aggregated = new List<TopProductDto>();

        foreach (var pid in productIds)
        {
            var pSales = saleDetails.Where(d => d.ProductId == pid).ToList();
            var pReturns = returnDetails.Where(d => d.ProductId == pid).ToList();

            var grossQty = pSales.Sum(d => d.Quantity);
            var returnQty = pReturns.Sum(d => d.Quantity);
            var netQty = grossQty - returnQty;

            var grossRevenue = pSales.Sum(d => d.Subtotal);
            var returnRevenue = pReturns.Sum(d => d.Subtotal);
            var netRevenue = grossRevenue - returnRevenue;

            if (netQty > 0)
            {
                var first = pSales.First();
                aggregated.Add(new TopProductDto
                {
                    ProductId = pid,
                    ProductName = first.Name,
                    SKU = first.SKU,
                    Brand = first.Brand,
                    NetQuantitySold = netQty,
                    NetRevenue = netRevenue
                });
            }
        }

        var totalQtySold = aggregated.Sum(p => p.NetQuantitySold);

        var top5 = aggregated
            .OrderByDescending(p => p.NetQuantitySold)
            .ThenByDescending(p => p.NetRevenue)
            .Take(5)
            .ToList();

        foreach (var item in top5)
        {
            item.SharePercentage = totalQtySold > 0
                ? Math.Round((double)item.NetQuantitySold / totalQtySold * 100.0, 1)
                : 0.0;
        }

        return top5;
    }
}
