using Microsoft.EntityFrameworkCore;
using Moq;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.Inventory;
using SIA.Application.DTOs.Reporting;
using SIA.Application.Services.Reporting;
using SIA.Domain.Entities.Accounting;
using SIA.Domain.Entities.Inventory;
using SIA.Domain.Entities.Purchasing;
using SIA.Domain.Entities.Sales;
using SIA.Domain.Enums;
using SIA.Infrastructure.Identity;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;
using Xunit;

namespace SIA.Tests.Services;

public class DashboardServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly Mock<IFinancialReportService> _mockFinancialReport = new();

    public void Dispose() => _db.Dispose();

    private DashboardService CreateService(ICurrentUserService user)
    {
        var context = _db.CreateContext();
        return new DashboardService(context, user, _mockFinancialReport.Object);
    }

    private async Task SeedBaseDataAsync()
    {
        await using var context = _db.CreateContext();

        // 1. Akun Kas & Bank
        context.Accounts.AddRange(
            new Account { Code = "1-1001", Name = "Kas", Category = AccountCategory.AsetLancar, NormalBalance = NormalBalance.Debit, Balance = 12_500_000m },
            new Account { Code = "1-1002", Name = "Bank", Category = AccountCategory.AsetLancar, NormalBalance = NormalBalance.Debit, Balance = 35_000_000m },
            new Account { Code = "1-1003", Name = "Piutang Usaha", Category = AccountCategory.AsetLancar, NormalBalance = NormalBalance.Debit, Balance = 0m }
        );

        // 2. User & Customer & Supplier
        context.Users.Add(new ApplicationUser { Id = "user-test", UserName = "tester@sia.local", FullName = "Tester SIA", Email = "tester@sia.local" });
        context.Customers.Add(new Customer { Id = 1, Name = "Bengkel Jaya Motor", Phone = "08123456789" });
        context.Suppliers.Add(new Supplier { Id = 1, Name = "PT Distributor Aki Nasional", Phone = "08987654321" });

        // 3. Produk Aki
        context.Products.AddRange(
            new Product { Id = 1, Name = "GS Astra MF NS40Z", Brand = "GS Astra", SKU = "GS-NS40Z", Stock = 2, ReorderPoint = 5, SellingPrice = 850_000m, PurchasePrice = 700_000m },
            new Product { Id = 2, Name = "Yuasa YTZ5S", Brand = "Yuasa", SKU = "YUA-YTZ5S", Stock = 0, ReorderPoint = 4, SellingPrice = 260_000m, PurchasePrice = 200_000m },
            new Product { Id = 3, Name = "Incoe Gold NS60", Brand = "Incoe", SKU = "INC-NS60", Stock = 15, ReorderPoint = 5, SellingPrice = 750_000m, PurchasePrice = 600_000m }
        );

        await context.SaveChangesAsync();

        // Setup mock default financial report
        _mockFinancialReport.Setup(f => f.GetIncomeStatementAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IncomeStatementDto>.Success(new IncomeStatementDto
            {
                Revenue = new ReportSectionDto { Total = 20_000_000m },
                CostOfGoodsSold = new ReportSectionDto { Total = 12_000_000m },
                OperatingExpenses = new ReportSectionDto { Total = 2_500_000m },
                OtherRevenue = new ReportSectionDto { Total = 500_000m },
                OtherExpenses = new ReportSectionDto { Total = 0m },
                NetIncome = 6_000_000m
            }));
    }

    #region 1. Authorization Tests

    private const string ReadDenied = "Anda tidak memiliki hak akses untuk melihat data ini.";

    [Fact]
    public async Task GetSummaryAsync_WhenUnauthenticated_ReturnsFailure()
    {
        await SeedBaseDataAsync();
        var unauthenticatedUser = TestUser.InRole(); // No roles -> IsAuthenticated = false
        var service = CreateService(unauthenticatedUser);

        var result = await service.GetSummaryAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(ReadDenied, result.Error);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenStaff_ReturnsSuccess_WithFinancialsHidden()
    {
        await SeedBaseDataAsync();
        var staffUser = TestUser.InRole(Roles.Staff);
        var service = CreateService(staffUser);

        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.False(result.Value.CanViewFinancials);
        Assert.Null(result.Value.CashAndBankBalance);
        Assert.Null(result.Value.CurrentMonthNetIncome);
        Assert.Null(result.Value.CurrentMonthRevenue);
        Assert.Null(result.Value.CurrentMonthExpenses);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Viewer)]
    public async Task GetSummaryAsync_WhenAdminOrViewer_ReturnsSuccess_WithFinancialsPopulated(string role)
    {
        await SeedBaseDataAsync();
        var privilegedUser = TestUser.InRole(role);
        var service = CreateService(privilegedUser);

        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.CanViewFinancials);
        Assert.Equal(47_500_000m, result.Value.CashAndBankBalance); // 12.5M + 35M
        Assert.Equal(6_000_000m, result.Value.CurrentMonthNetIncome);
        Assert.Equal(20_500_000m, result.Value.CurrentMonthRevenue);
        Assert.Equal(14_500_000m, result.Value.CurrentMonthExpenses);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenUserHasNoMatchingRole_ReturnsFailure()
    {
        await SeedBaseDataAsync();
        var guestUser = TestUser.InRole("Guest");
        var service = CreateService(guestUser);

        var result = await service.GetSummaryAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(ReadDenied, result.Error);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenFinancialReportFails_SetsMetricsToZero()
    {
        await SeedBaseDataAsync();
        _mockFinancialReport.Setup(f => f.GetIncomeStatementAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IncomeStatementDto>.Failure("Gagal memuat laporan laba rugi."));

        var service = CreateService(TestUser.InRole(Roles.Admin));
        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.CanViewFinancials);
        Assert.Equal(0m, result.Value.CurrentMonthNetIncome);
        Assert.Equal(0m, result.Value.CurrentMonthRevenue);
        Assert.Equal(0m, result.Value.CurrentMonthExpenses);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenFinancialReportReturnsNull_SetsMetricsToZero()
    {
        await SeedBaseDataAsync();
        _mockFinancialReport.Setup(f => f.GetIncomeStatementAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result<IncomeStatementDto>)null!);

        var service = CreateService(TestUser.InRole(Roles.Admin));
        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.CanViewFinancials);
        Assert.Equal(0m, result.Value.CurrentMonthNetIncome);
        Assert.Equal(0m, result.Value.CurrentMonthRevenue);
        Assert.Equal(0m, result.Value.CurrentMonthExpenses);
    }

    #endregion

    #region 2. Sales and Returns Calculations

    [Fact]
    public async Task GetSummaryAsync_CalculatesTodayAndMonthNetSales_Correctly()
    {
        await SeedBaseDataAsync();
        var today = DateTime.Today;

        await using (var context = _db.CreateContext())
        {
            // Penjualan Hari Ini: 2 nota (Total: 1.700.000 + 850.000 = 2.550.000)
            var sale1 = new Sale
            {
                InvoiceNumber = "INV-2026-0001",
                SaleDate = today.AddHours(9),
                CustomerId = 1,
                UserId = "user-test",
                TotalAmount = 1_700_000m,
                PaidAmount = 1_700_000m,
                RemainingAmount = 0m,
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Paid
            };
            var sale2 = new Sale
            {
                InvoiceNumber = "INV-2026-0002",
                SaleDate = today.AddHours(14),
                CustomerId = 1,
                UserId = "user-test",
                TotalAmount = 850_000m,
                PaidAmount = 850_000m,
                RemainingAmount = 0m,
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Paid
            };
            context.Sales.AddRange(sale1, sale2);
            await context.SaveChangesAsync();

            // Retur Penjualan Hari Ini: 250.000
            context.SaleReturns.Add(new SaleReturn
            {
                ReturnNumber = "SRT-2026-0001",
                ReturnDate = today.AddHours(16),
                SaleId = sale1.Id,
                UserId = "user-test",
                TotalAmount = 250_000m
            });

            // Penjualan bukan hari ini (tetapi masih bulan berjalan)
            var nonTodayThisMonth = today.Day == 1 ? today.AddDays(1) : today.AddDays(-1);
            context.Sales.Add(new Sale
            {
                InvoiceNumber = "INV-2026-0003",
                SaleDate = nonTodayThisMonth,
                CustomerId = 1,
                UserId = "user-test",
                TotalAmount = 1_000_000m,
                PaidAmount = 1_000_000m,
                RemainingAmount = 0m,
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Paid
            });

            await context.SaveChangesAsync();
        }

        var service = CreateService(TestUser.InRole(Roles.Admin));
        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        // Hari Ini: 2.550.000 - 250.000 = 2.300.000
        Assert.Equal(2, result.Value.TodaySaleCount);
        Assert.Equal(2_300_000m, result.Value.TodayNetSales);

        // Bulan Ini: (2.550.000 + 1.000.000) - 250.000 = 3.300.000
        Assert.Equal(3, result.Value.MonthSaleCount);
        Assert.Equal(3_300_000m, result.Value.MonthNetSales);
    }

    #endregion

    #region 3. Purchase Orders and Returns Calculations

    [Fact]
    public async Task GetSummaryAsync_CalculatesPOQueue_AndNetPurchases_Correctly()
    {
        await SeedBaseDataAsync();
        var today = DateTime.Today;

        await using (var context = _db.CreateContext())
        {
            // Draft PO (tidak dihitung dalam Net Purchases, masuk antrean draft)
            context.PurchaseOrders.Add(new PurchaseOrder
            {
                PONumber = "PO-2026-0001",
                OrderDate = today,
                SupplierId = 1,
                UserId = "user-test",
                TotalAmount = 5_000_000m,
                Status = PurchaseOrderStatus.Draft,
                PaymentStatus = PaymentStatus.Unpaid
            });

            // Approved PO (tidak dihitung dalam Net Purchases, masuk antrean approved)
            context.PurchaseOrders.Add(new PurchaseOrder
            {
                PONumber = "PO-2026-0002",
                OrderDate = today,
                SupplierId = 1,
                UserId = "user-test",
                TotalAmount = 3_000_000m,
                Status = PurchaseOrderStatus.Approved,
                PaymentStatus = PaymentStatus.Unpaid
            });

            // Received PO (dihitung dalam Net Purchases)
            var poReceived = new PurchaseOrder
            {
                PONumber = "PO-2026-0003",
                OrderDate = today,
                SupplierId = 1,
                UserId = "user-test",
                TotalAmount = 10_000_000m,
                RemainingAmount = 10_000_000m,
                Status = PurchaseOrderStatus.Received,
                PaymentStatus = PaymentStatus.Unpaid
            };
            context.PurchaseOrders.Add(poReceived);
            await context.SaveChangesAsync();

            // Retur Pembelian
            context.PurchaseReturns.Add(new PurchaseReturn
            {
                ReturnNumber = "PRT-2026-0001",
                ReturnDate = today,
                PurchaseOrderId = poReceived.Id,
                UserId = "user-test",
                TotalAmount = 1_500_000m
            });

            await context.SaveChangesAsync();
        }

        var service = CreateService(TestUser.InRole(Roles.Admin));
        var result = await service.GetSummaryAsync(DashboardTrendPeriod.Last7Days);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.DraftPoCount);
        Assert.Equal(1, result.Value.ApprovedPoCount);
        Assert.Equal(2, result.Value.PendingPurchaseOrders.Count); // 1 Draft + 1 Approved

        // Cek titik tren hari ini untuk net purchases: 10.000.000 - 1.500.000 = 8.500.000
        var todayTrend = result.Value.TrendPoints.Last();
        Assert.Equal(8_500_000m, todayTrend.NetPurchases);
    }

    #endregion

    #region 4. Receivables, Payables, and Overdue Detection

    [Fact]
    public async Task GetSummaryAsync_CalculatesReceivablesAndPayables_AndDetectsOverdue()
    {
        await SeedBaseDataAsync();
        var today = DateTime.Today;

        await using (var context = _db.CreateContext())
        {
            // Piutang 1: Overdue (Jatuh tempo kemarin)
            context.Sales.Add(new Sale
            {
                InvoiceNumber = "INV-2026-0010",
                SaleDate = today.AddDays(-10),
                DueDate = today.AddDays(-1),
                CustomerId = 1,
                UserId = "user-test",
                TotalAmount = 2_000_000m,
                PaidAmount = 500_000m,
                RemainingAmount = 1_500_000m,
                PaymentStatus = PaymentStatus.PartiallyPaid
            });

            // Piutang 2: Belum Overdue (Jatuh tempo 3 hari lagi)
            context.Sales.Add(new Sale
            {
                InvoiceNumber = "INV-2026-0011",
                SaleDate = today,
                DueDate = today.AddDays(3),
                CustomerId = 1,
                UserId = "user-test",
                TotalAmount = 1_000_000m,
                PaidAmount = 0m,
                RemainingAmount = 1_000_000m,
                PaymentStatus = PaymentStatus.Unpaid
            });

            // Utang 1: Overdue (PO Received, jatuh tempo 2 hari lalu)
            context.PurchaseOrders.Add(new PurchaseOrder
            {
                PONumber = "PO-2026-0010",
                OrderDate = today.AddDays(-15),
                DueDate = today.AddDays(-2),
                SupplierId = 1,
                UserId = "user-test",
                TotalAmount = 4_000_000m,
                PaidAmount = 1_000_000m,
                RemainingAmount = 3_000_000m,
                Status = PurchaseOrderStatus.Received,
                PaymentStatus = PaymentStatus.PartiallyPaid
            });

            // Utang 2: Belum Overdue (PO Received, jatuh tempo 5 hari lagi)
            context.PurchaseOrders.Add(new PurchaseOrder
            {
                PONumber = "PO-2026-0011",
                OrderDate = today,
                DueDate = today.AddDays(5),
                SupplierId = 1,
                UserId = "user-test",
                TotalAmount = 6_000_000m,
                PaidAmount = 0m,
                RemainingAmount = 6_000_000m,
                Status = PurchaseOrderStatus.Received,
                PaymentStatus = PaymentStatus.Unpaid
            });

            await context.SaveChangesAsync();
        }

        var service = CreateService(TestUser.InRole(Roles.Admin));
        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);

        // Piutang
        Assert.Equal(2_500_000m, result.Value.TotalReceivables);
        Assert.Equal(2, result.Value.UnpaidSaleCount);
        Assert.Equal(1, result.Value.OverdueReceivableCount);
        Assert.Equal(1_500_000m, result.Value.OverdueReceivablesAmount);

        // Utang
        Assert.Equal(9_000_000m, result.Value.TotalPayables);
        Assert.Equal(2, result.Value.UnpaidPoCount);
        Assert.Equal(1, result.Value.OverduePayableCount);
        Assert.Equal(3_000_000m, result.Value.OverduePayablesAmount);

        // Panel DueAndOverdueItems: Overdue item harus berada di urutan teratas
        Assert.Equal(4, result.Value.DueAndOverdueItems.Count);
        Assert.True(result.Value.DueAndOverdueItems[0].DueDate < today);
        Assert.True(result.Value.DueAndOverdueItems[1].DueDate < today);
    }

    #endregion

    #region 5. Inventory Stock Alerts

    [Fact]
    public async Task GetSummaryAsync_CalculatesLowStockAndOutOfStock_Correctly()
    {
        await SeedBaseDataAsync();

        var service = CreateService(TestUser.InRole(Roles.Staff));
        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        // Dari SeedBaseData:
        // Produk 1: Stock = 2, Reorder = 5 (Low Stock)
        // Produk 2: Stock = 0, Reorder = 4 (Out of Stock & Low Stock)
        // Produk 3: Stock = 15, Reorder = 5 (Normal)
        Assert.Equal(2, result.Value.LowStockProductCount);
        Assert.Equal(1, result.Value.OutOfStockProductCount);
        Assert.Equal(3, result.Value.TotalActiveProductCount);

        // Panel LowStockAlerts harus memuat 2 produk dengan stok terendah lebih dulu
        Assert.Equal(2, result.Value.LowStockAlerts.Count);
        Assert.Equal(0, result.Value.LowStockAlerts[0].Stock);
        Assert.Equal(2, result.Value.LowStockAlerts[1].Stock);
    }

    #endregion

    #region 6. Top Products & Trend Periods

    [Fact]
    public async Task GetSummaryAsync_CalculatesTopProducts_AndDeductsReturns()
    {
        await SeedBaseDataAsync();
        var today = DateTime.Today;

        await using (var context = _db.CreateContext())
        {
            var sale = new Sale
            {
                InvoiceNumber = "INV-TOP-01",
                SaleDate = today,
                CustomerId = 1,
                UserId = "user-test",
                TotalAmount = 2_810_000m,
                PaymentStatus = PaymentStatus.Paid
            };
            context.Sales.Add(sale);
            await context.SaveChangesAsync();

            // Produk 1: 3 unit @ 850.000 = 2.550.000
            // Produk 2: 1 unit @ 260.000 = 260.000
            context.SaleDetails.AddRange(
                new SaleDetail { SaleId = sale.Id, ProductId = 1, Quantity = 3, UnitPrice = 850_000m, Subtotal = 2_550_000m },
                new SaleDetail { SaleId = sale.Id, ProductId = 2, Quantity = 1, UnitPrice = 260_000m, Subtotal = 260_000m }
            );

            // Retur Produk 1: 1 unit @ 850.000 (sehingga Net Qty = 2)
            var saleReturn = new SaleReturn
            {
                ReturnNumber = "SRT-TOP-01",
                ReturnDate = today,
                SaleId = sale.Id,
                UserId = "user-test",
                TotalAmount = 850_000m
            };
            context.SaleReturns.Add(saleReturn);
            await context.SaveChangesAsync();

            context.SaleReturnDetails.Add(
                new SaleReturnDetail { SaleReturnId = saleReturn.Id, ProductId = 1, Quantity = 1, UnitPrice = 850_000m, Subtotal = 850_000m }
            );

            await context.SaveChangesAsync();
        }

        var service = CreateService(TestUser.InRole(Roles.Admin));
        var result = await service.GetSummaryAsync(DashboardTrendPeriod.Last7Days);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TopProducts.Count);

        // Produk 1 harus paling atas karena Net Qty = 2
        var top1 = result.Value.TopProducts[0];
        Assert.Equal(1, top1.ProductId);
        Assert.Equal(2, top1.NetQuantitySold);
        Assert.Equal(1_700_000m, top1.NetRevenue);

        // Produk 2 Net Qty = 1
        var top2 = result.Value.TopProducts[1];
        Assert.Equal(2, top2.ProductId);
        Assert.Equal(1, top2.NetQuantitySold);
        Assert.Equal(260_000m, top2.NetRevenue);

        // Share: Total Qty = 3. Top 1: 2/3 = 66.7%, Top 2: 1/3 = 33.3%
        Assert.Equal(66.7, top1.SharePercentage);
        Assert.Equal(33.3, top2.SharePercentage);
    }

    [Theory]
    [InlineData(DashboardTrendPeriod.Last7Days, 7)]
    [InlineData(DashboardTrendPeriod.ThisYear, 12)]
    public async Task GetSummaryAsync_GeneratesCorrectPointCount_ForPeriods(DashboardTrendPeriod period, int expectedPoints)
    {
        await SeedBaseDataAsync();
        var service = CreateService(TestUser.InRole(Roles.Admin));

        var result = await service.GetSummaryAsync(period);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedPoints, result.Value.TrendPoints.Count);
        Assert.All(result.Value.TrendPoints, p => Assert.False(string.IsNullOrWhiteSpace(p.Label)));
    }

    [Fact]
    public async Task GetSummaryAsync_GeneratesThisMonth_WeeklyTrendPoints()
    {
        await SeedBaseDataAsync();
        var service = CreateService(TestUser.InRole(Roles.Admin));

        var result = await service.GetSummaryAsync(DashboardTrendPeriod.ThisMonth);

        Assert.True(result.IsSuccess);
        // Bulan masehi memiliki 4 atau 5 titik mingguan
        Assert.InRange(result.Value.TrendPoints.Count, 4, 5);
        Assert.StartsWith("Mgg 1", result.Value.TrendPoints[0].Label);
    }

    #endregion
}
