using System.Collections;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Moq;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
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

/// <summary>
/// Adversarial stress-tests challenging authorization models, data masking,
/// and boundary edge cases of DashboardService.
/// </summary>
public class DashboardAdversarialTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly Mock<IFinancialReportService> _mockFinancialReport = new();

    public DashboardAdversarialTests()
    {
        // Default safe mock setup
        _mockFinancialReport.Setup(f => f.GetIncomeStatementAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IncomeStatementDto>.Success(new IncomeStatementDto()));
    }

    public void Dispose() => _db.Dispose();

    private DashboardService CreateService(ICurrentUserService user)
    {
        var context = _db.CreateContext();
        return new DashboardService(context, user, _mockFinancialReport.Object);
    }

    #region 1. Security & Authorization Boundaries

    [Theory]
    [InlineData("")]
    [InlineData("Customer")]
    [InlineData("UnknownRole")]
    public async Task Security_UnauthenticatedAndUnauthorizedRoles_StrictlyFailWithReadDenied(string role)
    {
        var user = string.IsNullOrEmpty(role) ? TestUser.InRole() : TestUser.InRole(role);
        var service = CreateService(user);

        var result = await service.GetSummaryAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("Anda tidak memiliki hak akses untuk melihat data ini.", result.Error);
        _mockFinancialReport.Verify(f => f.GetIncomeStatementAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Security_StaffRole_NeverInvokesFinancialReportService_AndMasksAllFinancialData()
    {
        await using (var context = _db.CreateContext())
        {
            context.Accounts.AddRange(
                new Account { Code = "1-1001", Name = "Kas", Category = AccountCategory.AsetLancar, NormalBalance = NormalBalance.Debit, Balance = 25_000_000m },
                new Account { Code = "1-1002", Name = "Bank", Category = AccountCategory.AsetLancar, NormalBalance = NormalBalance.Debit, Balance = 75_000_000m }
            );
            await context.SaveChangesAsync();
        }

        var staffUser = TestUser.InRole(Roles.Staff);
        var service = CreateService(staffUser);

        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.False(result.Value.CanViewFinancials);

        // Strict data masking: All financial properties must be null
        Assert.Null(result.Value.CashAndBankBalance);
        Assert.Null(result.Value.CurrentMonthNetIncome);
        Assert.Null(result.Value.CurrentMonthRevenue);
        Assert.Null(result.Value.CurrentMonthExpenses);

        // Verify financial reporting service was NEVER called (zero information leakage)
        _mockFinancialReport.Verify(f => f.GetIncomeStatementAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Viewer)]
    public async Task Security_AdminAndViewer_CanViewFinancials_PopulatesAllMetrics(string role)
    {
        await using (var context = _db.CreateContext())
        {
            context.Accounts.AddRange(
                new Account { Code = "1-1001", Name = "Kas", Category = AccountCategory.AsetLancar, NormalBalance = NormalBalance.Debit, Balance = 15_000_000m },
                new Account { Code = "1-1002", Name = "Bank", Category = AccountCategory.AsetLancar, NormalBalance = NormalBalance.Debit, Balance = 35_000_000m }
            );
            await context.SaveChangesAsync();
        }

        _mockFinancialReport.Setup(f => f.GetIncomeStatementAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IncomeStatementDto>.Success(new IncomeStatementDto
            {
                Revenue = new ReportSectionDto { Total = 50_000_000m },
                CostOfGoodsSold = new ReportSectionDto { Total = 30_000_000m },
                OperatingExpenses = new ReportSectionDto { Total = 5_000_000m },
                OtherRevenue = new ReportSectionDto { Total = 1_000_000m },
                OtherExpenses = new ReportSectionDto { Total = 500_000m },
                NetIncome = 15_500_000m
            }));

        var user = TestUser.InRole(role);
        var service = CreateService(user);

        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.CanViewFinancials);

        // Cash & Bank = 15M + 35M = 50M
        Assert.Equal(50_000_000m, result.Value.CashAndBankBalance);
        Assert.Equal(15_500_000m, result.Value.CurrentMonthNetIncome);
        Assert.Equal(51_000_000m, result.Value.CurrentMonthRevenue); // 50M + 1M
        Assert.Equal(35_500_000m, result.Value.CurrentMonthExpenses); // 30M + 5M + 500K

        _mockFinancialReport.Verify(f => f.GetIncomeStatementAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region 2. Boundary Cases: Empty Database

    [Theory]
    [InlineData(DashboardTrendPeriod.Last7Days, 7)]
    [InlineData(DashboardTrendPeriod.ThisMonth, 0)] // Will test >= 4 and <= 5
    [InlineData(DashboardTrendPeriod.ThisYear, 12)]
    public async Task EdgeCase_EmptyDatabase_ReturnsZeroMetricsWithoutExceptions_AcrossAllPeriods(DashboardTrendPeriod period, int expectedPoints)
    {
        var service = CreateService(TestUser.InRole(Roles.Admin));
        var result = await service.GetSummaryAsync(period);

        Assert.True(result.IsSuccess);
        var s = result.Value;

        Assert.Equal(0, s.TodaySaleCount);
        Assert.Equal(0m, s.TodayNetSales);
        Assert.Equal(0, s.MonthSaleCount);
        Assert.Equal(0m, s.MonthNetSales);

        Assert.Equal(0m, s.TotalReceivables);
        Assert.Equal(0, s.UnpaidSaleCount);
        Assert.Equal(0, s.OverdueReceivableCount);
        Assert.Equal(0m, s.OverdueReceivablesAmount);

        Assert.Equal(0m, s.TotalPayables);
        Assert.Equal(0, s.UnpaidPoCount);
        Assert.Equal(0, s.OverduePayableCount);
        Assert.Equal(0m, s.OverduePayablesAmount);

        Assert.Equal(0, s.DraftPoCount);
        Assert.Equal(0, s.ApprovedPoCount);
        Assert.Equal(0, s.LowStockProductCount);
        Assert.Equal(0, s.OutOfStockProductCount);
        Assert.Equal(0, s.TotalActiveProductCount);

        Assert.Equal(0m, s.CashAndBankBalance);
        Assert.Equal(0m, s.CurrentMonthNetIncome);
        Assert.Equal(0m, s.CurrentMonthRevenue);
        Assert.Equal(0m, s.CurrentMonthExpenses);

        if (period == DashboardTrendPeriod.ThisMonth)
        {
            Assert.InRange(s.TrendPoints.Count, 4, 5);
        }
        else
        {
            Assert.Equal(expectedPoints, s.TrendPoints.Count);
        }

        Assert.All(s.TrendPoints, p =>
        {
            Assert.Equal(0m, p.NetSales);
            Assert.Equal(0m, p.NetPurchases);
            Assert.False(string.IsNullOrWhiteSpace(p.Label));
        });

        Assert.Empty(s.TopProducts);
        Assert.Empty(s.LowStockAlerts);
        Assert.Empty(s.DueAndOverdueItems);
        Assert.Empty(s.PendingPurchaseOrders);
        Assert.Empty(s.RecentSales);
    }

    #endregion

    #region 3. Boundary Cases: DueDate Boundary (Today vs Yesterday vs Tomorrow)

    [Fact]
    public async Task EdgeCase_OverdueItems_BoundaryDueDate_SalesAndPOs()
    {
        var today = DateTime.Today;

        await using (var context = _db.CreateContext())
        {
            context.Users.Add(new ApplicationUser { Id = "u1", UserName = "tester" });
            context.Customers.Add(new Customer { Id = 1, Name = "Cust 1" });
            context.Suppliers.Add(new Supplier { Id = 1, Name = "Supp 1" });

            // Sale 1: DueDate == Today -> NOT overdue
            context.Sales.Add(new Sale
            {
                InvoiceNumber = "INV-TODAY",
                SaleDate = today.AddDays(-2),
                DueDate = today,
                CustomerId = 1,
                UserId = "u1",
                TotalAmount = 1_000_000m,
                RemainingAmount = 1_000_000m,
                PaymentStatus = PaymentStatus.Unpaid
            });

            // Sale 2: DueDate == Yesterday -> OVERDUE
            context.Sales.Add(new Sale
            {
                InvoiceNumber = "INV-YESTERDAY",
                SaleDate = today.AddDays(-2),
                DueDate = today.AddDays(-1),
                CustomerId = 1,
                UserId = "u1",
                TotalAmount = 2_000_000m,
                RemainingAmount = 2_000_000m,
                PaymentStatus = PaymentStatus.Unpaid
            });

            // Sale 3: DueDate == Tomorrow -> NOT overdue
            context.Sales.Add(new Sale
            {
                InvoiceNumber = "INV-TOMORROW",
                SaleDate = today,
                DueDate = today.AddDays(1),
                CustomerId = 1,
                UserId = "u1",
                TotalAmount = 500_000m,
                RemainingAmount = 500_000m,
                PaymentStatus = PaymentStatus.Unpaid
            });

            // PO 1: DueDate == Today -> NOT overdue
            context.PurchaseOrders.Add(new PurchaseOrder
            {
                PONumber = "PO-TODAY",
                OrderDate = today.AddDays(-5),
                DueDate = today,
                SupplierId = 1,
                UserId = "u1",
                TotalAmount = 3_000_000m,
                RemainingAmount = 3_000_000m,
                Status = PurchaseOrderStatus.Received,
                PaymentStatus = PaymentStatus.Unpaid
            });

            // PO 2: DueDate == Yesterday -> OVERDUE
            context.PurchaseOrders.Add(new PurchaseOrder
            {
                PONumber = "PO-YESTERDAY",
                OrderDate = today.AddDays(-5),
                DueDate = today.AddDays(-1),
                SupplierId = 1,
                UserId = "u1",
                TotalAmount = 4_000_000m,
                RemainingAmount = 4_000_000m,
                Status = PurchaseOrderStatus.Received,
                PaymentStatus = PaymentStatus.Unpaid
            });

            // PO 3: DueDate == Tomorrow -> NOT overdue
            context.PurchaseOrders.Add(new PurchaseOrder
            {
                PONumber = "PO-TOMORROW",
                OrderDate = today,
                DueDate = today.AddDays(1),
                SupplierId = 1,
                UserId = "u1",
                TotalAmount = 6_000_000m,
                RemainingAmount = 6_000_000m,
                Status = PurchaseOrderStatus.Received,
                PaymentStatus = PaymentStatus.Unpaid
            });

            await context.SaveChangesAsync();
        }

        var service = CreateService(TestUser.InRole(Roles.Admin));
        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        var s = result.Value;

        // Sales Receivables
        Assert.Equal(3_500_000m, s.TotalReceivables);
        Assert.Equal(3, s.UnpaidSaleCount);
        // Only Sale 2 (INV-YESTERDAY) is overdue; Sale 1 (INV-TODAY) is NOT overdue
        Assert.Equal(1, s.OverdueReceivableCount);
        Assert.Equal(2_000_000m, s.OverdueReceivablesAmount);

        // PO Payables
        Assert.Equal(13_000_000m, s.TotalPayables);
        Assert.Equal(3, s.UnpaidPoCount);
        // Only PO 2 (PO-YESTERDAY) is overdue; PO 1 (PO-TODAY) is NOT overdue
        Assert.Equal(1, s.OverduePayableCount);
        Assert.Equal(4_000_000m, s.OverduePayablesAmount);

        // In DueAndOverdueItems: The 2 overdue items must be ordered at the very top (index 0 and 1)
        Assert.Equal(6, s.DueAndOverdueItems.Count);
        var firstTwo = s.DueAndOverdueItems.Take(2).ToList();
        Assert.Contains(firstTwo, x => x.ReferenceNumber == "INV-YESTERDAY");
        Assert.Contains(firstTwo, x => x.ReferenceNumber == "PO-YESTERDAY");

        // The remaining 4 items must NOT be overdue
        var remainingFour = s.DueAndOverdueItems.Skip(2).ToList();
        Assert.All(remainingFour, x => Assert.True(!x.DueDate.HasValue || x.DueDate.Value.Date >= today));
    }

    #endregion

    #region 4. Boundary Cases: Stock Levels (Out of stock vs Low Stock)

    [Fact]
    public async Task EdgeCase_StockLevels_ZeroStockIsBothLowAndOutOfStock()
    {
        await using (var context = _db.CreateContext())
        {
            context.Products.AddRange(
                // 1. Stock = 0, ReorderPoint = 5 -> Both LowStock and OutOfStock
                new Product { Id = 1, Name = "P1", Stock = 0, ReorderPoint = 5 },
                // 2. Stock = 0, ReorderPoint = 0 -> Both LowStock (0 <= 0) and OutOfStock (0 == 0)
                new Product { Id = 2, Name = "P2", Stock = 0, ReorderPoint = 0 },
                // 3. Stock = 3, ReorderPoint = 3 -> LowStock (3 <= 3), but NOT OutOfStock
                new Product { Id = 3, Name = "P3", Stock = 3, ReorderPoint = 3 },
                // 4. Stock = 5, ReorderPoint = 10 -> LowStock (5 <= 10), but NOT OutOfStock
                new Product { Id = 4, Name = "P4", Stock = 5, ReorderPoint = 10 },
                // 5. Stock = 10, ReorderPoint = 5 -> Normal stock (neither)
                new Product { Id = 5, Name = "P5", Stock = 10, ReorderPoint = 5 }
            );
            await context.SaveChangesAsync();
        }

        var service = CreateService(TestUser.InRole(Roles.Staff));
        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        var s = result.Value;

        // P1, P2, P3, P4 are low stock (total 4)
        Assert.Equal(4, s.LowStockProductCount);
        // Only P1 and P2 have Stock == 0 (total 2)
        Assert.Equal(2, s.OutOfStockProductCount);
        // Total products = 5
        Assert.Equal(5, s.TotalActiveProductCount);

        // LowStockAlerts should list P1, P2 (stock 0) first, then P3, P4
        Assert.Equal(4, s.LowStockAlerts.Count);
        Assert.Equal(0, s.LowStockAlerts[0].Stock);
        Assert.Equal(0, s.LowStockAlerts[1].Stock);
        Assert.Equal(3, s.LowStockAlerts[2].Stock);
        Assert.Equal(5, s.LowStockAlerts[3].Stock);
    }

    #endregion

    #region 5. Boundary Cases: Trend Period Transitions and Leap Years

    [Theory]
    [InlineData(2024, 2, 29, 5, "Mgg 5 (29-29)")] // Leap year Feb (29 days -> 5 weeks)
    [InlineData(2023, 2, 28, 4, null)]             // Non-leap Feb (28 days -> exactly 4 weeks)
    [InlineData(2024, 4, 30, 5, "Mgg 5 (29-30)")] // 30-day month (April -> 5 weeks)
    [InlineData(2024, 12, 31, 5, "Mgg 5 (29-31)")]// 31-day month (December -> 5 weeks)
    [InlineData(2025, 1, 1, 5, "Mgg 5 (29-31)")]  // January start (31 days -> 5 weeks)
    public void EdgeCase_BuildTrendPoints_ThisMonth_HandlesDifferingMonthLengthsAndLeapYears(
        int year, int month, int day, int expectedPoints, string? expectedLastLabel)
    {
        var testDate = new DateTime(year, month, day);

        var datedAmountType = typeof(DashboardService).GetNestedType("DatedAmount", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(datedAmountType);
        var emptyList = (IList)Activator.CreateInstance(listType)!;

        var method = typeof(DashboardService).GetMethod("BuildTrendPoints", BindingFlags.NonPublic | BindingFlags.Static)!;

        var points = (List<DashboardTrendPointDto>)method.Invoke(null, new object[]
        {
            DashboardTrendPeriod.ThisMonth,
            testDate,
            emptyList,
            emptyList,
            emptyList,
            emptyList
        })!;

        Assert.Equal(expectedPoints, points.Count);
        Assert.Equal("Mgg 1 (1-7)", points[0].Label);
        Assert.Equal("Mgg 2 (8-14)", points[1].Label);
        Assert.Equal("Mgg 3 (15-21)", points[2].Label);
        Assert.Equal("Mgg 4 (22-28)", points[3].Label);

        if (expectedLastLabel != null)
        {
            Assert.Equal(expectedLastLabel, points[4].Label);
        }
    }

    [Theory]
    [InlineData(2024, 1, 1)]  // Leap year start
    [InlineData(2024, 2, 29)] // Leap day
    [InlineData(2024, 12, 31)]// Leap year end
    [InlineData(2025, 2, 28)] // Non-leap year Feb end
    [InlineData(2025, 7, 15)] // Mid year
    public void EdgeCase_BuildTrendPoints_ThisYear_AlwaysProduces12Months(int year, int month, int day)
    {
        var testDate = new DateTime(year, month, day);

        var datedAmountType = typeof(DashboardService).GetNestedType("DatedAmount", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(datedAmountType);
        var emptyList = (IList)Activator.CreateInstance(listType)!;

        var method = typeof(DashboardService).GetMethod("BuildTrendPoints", BindingFlags.NonPublic | BindingFlags.Static)!;

        var points = (List<DashboardTrendPointDto>)method.Invoke(null, new object[]
        {
            DashboardTrendPeriod.ThisYear,
            testDate,
            emptyList,
            emptyList,
            emptyList,
            emptyList
        })!;

        Assert.Equal(12, points.Count);
        string[] expectedMonths = { "Jan", "Feb", "Mar", "Apr", "Mei", "Jun", "Jul", "Agu", "Sep", "Okt", "Nov", "Des" };
        for (int i = 0; i < 12; i++)
        {
            Assert.Equal(expectedMonths[i], points[i].Label);
        }
    }

    [Theory]
    [InlineData(2024, 3, 2)]  // Crosses leap day Feb 29 (Feb 25 - Mar 2)
    [InlineData(2025, 1, 3)]  // Crosses year end Dec 31 (Dec 28 - Jan 3)
    [InlineData(2024, 3, 1)]  // Day after leap day
    [InlineData(2025, 3, 1)]  // Day after non-leap Feb 28
    public void EdgeCase_BuildTrendPoints_Last7Days_AlwaysProduces7ConsecutiveDays(int year, int month, int day)
    {
        var testDate = new DateTime(year, month, day);

        var datedAmountType = typeof(DashboardService).GetNestedType("DatedAmount", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(datedAmountType);
        var emptyList = (IList)Activator.CreateInstance(listType)!;

        var method = typeof(DashboardService).GetMethod("BuildTrendPoints", BindingFlags.NonPublic | BindingFlags.Static)!;

        var points = (List<DashboardTrendPointDto>)method.Invoke(null, new object[]
        {
            DashboardTrendPeriod.Last7Days,
            testDate,
            emptyList,
            emptyList,
            emptyList,
            emptyList
        })!;

        Assert.Equal(7, points.Count);
        for (int i = 0; i < 7; i++)
        {
            var expectedDate = testDate.AddDays(-(6 - i));
            Assert.Equal(expectedDate.ToString("dd/MM"), points[i].Label);
        }
    }

    #endregion

    #region 6. Resilience: Financial Report Service Null Safety

    [Fact]
    public async Task Resilience_WhenFinancialReportServiceReturnsNull_DoesNotCrash()
    {
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

    [Fact]
    public async Task Resilience_WhenFinancialReportServiceReturnsSuccessWithNullValue_DoesNotCrash()
    {
        _mockFinancialReport.Setup(f => f.GetIncomeStatementAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IncomeStatementDto>.Success(null!));

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
    public async Task Resilience_WhenFinancialReportServiceReturnsSuccessWithNullSubSections_DoesNotCrash()
    {
        var incomeDto = new IncomeStatementDto
        {
            Revenue = null!,
            CostOfGoodsSold = null!,
            OperatingExpenses = null!,
            OtherRevenue = null!,
            OtherExpenses = null!,
            NetIncome = 500_000m
        };

        _mockFinancialReport.Setup(f => f.GetIncomeStatementAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IncomeStatementDto>.Success(incomeDto));

        var service = CreateService(TestUser.InRole(Roles.Admin));

        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.CanViewFinancials);
        Assert.Equal(500_000m, result.Value.CurrentMonthNetIncome);
        Assert.Equal(0m, result.Value.CurrentMonthRevenue);
        Assert.Equal(0m, result.Value.CurrentMonthExpenses);
    }

    [Fact]
    public async Task Resilience_WhenFinancialReportServiceReturnsSuccessWithNull_ForViewerRole_DoesNotCrash()
    {
        _mockFinancialReport.Setup(f => f.GetIncomeStatementAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IncomeStatementDto>.Success(null!));

        var service = CreateService(TestUser.InRole(Roles.Viewer));

        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.CanViewFinancials);
        Assert.Equal(0m, result.Value.CurrentMonthNetIncome);
        Assert.Equal(0m, result.Value.CurrentMonthRevenue);
        Assert.Equal(0m, result.Value.CurrentMonthExpenses);
    }

    [Fact]
    public async Task Resilience_WhenStaffRole_DoesNotCallFinancialReportService()
    {
        var service = CreateService(TestUser.InRole(Roles.Staff));

        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.False(result.Value.CanViewFinancials);
        Assert.Null(result.Value.CashAndBankBalance);
        Assert.Null(result.Value.CurrentMonthNetIncome);
        Assert.Null(result.Value.CurrentMonthRevenue);
        Assert.Null(result.Value.CurrentMonthExpenses);
        _mockFinancialReport.Verify(f => f.GetIncomeStatementAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
