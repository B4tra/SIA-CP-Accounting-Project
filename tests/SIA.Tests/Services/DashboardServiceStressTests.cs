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

public class DashboardServiceStressTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly Mock<IFinancialReportService> _mockFinancialReport = new();

    public DashboardServiceStressTests()
    {
        _mockFinancialReport.Setup(f => f.GetIncomeStatementAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IncomeStatementDto>.Success(new IncomeStatementDto
            {
                Revenue = new ReportSectionDto { Total = 10_000_000m },
                CostOfGoodsSold = new ReportSectionDto { Total = 6_000_000m },
                OperatingExpenses = new ReportSectionDto { Total = 1_000_000m },
                OtherRevenue = new ReportSectionDto { Total = 0m },
                OtherExpenses = new ReportSectionDto { Total = 0m },
                NetIncome = 3_000_000m
            }));
    }

    public void Dispose() => _db.Dispose();

    private DashboardService CreateService(ICurrentUserService user)
    {
        var context = _db.CreateContext();
        return new DashboardService(context, user, _mockFinancialReport.Object);
    }

    private async Task SeedBaseDataAsync()
    {
        await using var context = _db.CreateContext();

        context.Users.Add(new ApplicationUser
        {
            Id = "user-stress",
            UserName = "stress@sia.local",
            FullName = "Stress Tester",
            Email = "stress@sia.local"
        });

        context.Customers.Add(new Customer { Id = 1, Name = "Pelanggan A", Phone = "0811111111" });
        context.Suppliers.Add(new Supplier { Id = 1, Name = "Pemasok A", Phone = "0822222222" });

        await context.SaveChangesAsync();

        _mockFinancialReport.Setup(f => f.GetIncomeStatementAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IncomeStatementDto>.Success(new IncomeStatementDto
            {
                Revenue = new ReportSectionDto { Total = 10_000_000m },
                CostOfGoodsSold = new ReportSectionDto { Total = 6_000_000m },
                OperatingExpenses = new ReportSectionDto { Total = 1_000_000m },
                OtherRevenue = new ReportSectionDto { Total = 0m },
                OtherExpenses = new ReportSectionDto { Total = 0m },
                NetIncome = 3_000_000m
            }));
    }

    [Fact]
    public async Task NetSales_WhenReturnsExceedSales_CalculatesNegativeNetSalesCorrectly()
    {
        await SeedBaseDataAsync();
        var today = DateTime.Today;

        await using (var context = _db.CreateContext())
        {
            var sale = new Sale
            {
                InvoiceNumber = "INV-NEG-01",
                SaleDate = today.AddHours(8),
                CustomerId = 1,
                UserId = "user-stress",
                TotalAmount = 500_000m,
                PaidAmount = 500_000m,
                RemainingAmount = 0m,
                PaymentStatus = PaymentStatus.Paid
            };
            context.Sales.Add(sale);
            await context.SaveChangesAsync();

            // Return with amount 800.000 (e.g. from previously accumulated sales)
            context.SaleReturns.Add(new SaleReturn
            {
                ReturnNumber = "SRT-NEG-01",
                ReturnDate = today.AddHours(10),
                SaleId = sale.Id,
                UserId = "user-stress",
                TotalAmount = 800_000m
            });
            await context.SaveChangesAsync();
        }

        var service = CreateService(TestUser.InRole(Roles.Admin));
        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.TodaySaleCount);
        // 500.000 - 800.000 = -300.000
        Assert.Equal(-300_000m, result.Value.TodayNetSales);
        Assert.Equal(-300_000m, result.Value.MonthNetSales);
    }

    [Fact]
    public async Task OverdueDetection_BoundaryCases_HandlesExactTodayAndZeroBalanceCorrectly()
    {
        await SeedBaseDataAsync();
        var today = DateTime.Today;

        await using (var context = _db.CreateContext())
        {
            // Case 1: DueDate is today -> NOT overdue
            context.Sales.Add(new Sale
            {
                InvoiceNumber = "INV-BND-01",
                SaleDate = today.AddDays(-5),
                DueDate = today,
                CustomerId = 1,
                UserId = "user-stress",
                TotalAmount = 1_000_000m,
                RemainingAmount = 1_000_000m,
                PaymentStatus = PaymentStatus.Unpaid
            });

            // Case 2: DueDate is yesterday -> OVERDUE
            context.Sales.Add(new Sale
            {
                InvoiceNumber = "INV-BND-02",
                SaleDate = today.AddDays(-10),
                DueDate = today.AddDays(-1),
                CustomerId = 1,
                UserId = "user-stress",
                TotalAmount = 2_000_000m,
                RemainingAmount = 1_500_000m,
                PaymentStatus = PaymentStatus.PartiallyPaid
            });

            // Case 3: DueDate is yesterday, but fully paid (RemainingAmount = 0) -> NOT overdue & NOT in receivables
            context.Sales.Add(new Sale
            {
                InvoiceNumber = "INV-BND-03",
                SaleDate = today.AddDays(-10),
                DueDate = today.AddDays(-1),
                CustomerId = 1,
                UserId = "user-stress",
                TotalAmount = 3_000_000m,
                PaidAmount = 3_000_000m,
                RemainingAmount = 0m,
                PaymentStatus = PaymentStatus.Paid
            });

            // Case 4: No DueDate, but unpaid -> in receivables, NOT in overdue
            context.Sales.Add(new Sale
            {
                InvoiceNumber = "INV-BND-04",
                SaleDate = today,
                DueDate = null,
                CustomerId = 1,
                UserId = "user-stress",
                TotalAmount = 500_000m,
                RemainingAmount = 500_000m,
                PaymentStatus = PaymentStatus.Unpaid
            });

            await context.SaveChangesAsync();
        }

        var service = CreateService(TestUser.InRole(Roles.Admin));
        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        // Total receivables: 1.000.000 (Case 1) + 1.500.000 (Case 2) + 500.000 (Case 4) = 3.000.000
        Assert.Equal(3_000_000m, result.Value.TotalReceivables);
        Assert.Equal(3, result.Value.UnpaidSaleCount);

        // Only Case 2 is overdue
        Assert.Equal(1, result.Value.OverdueReceivableCount);
        Assert.Equal(1_500_000m, result.Value.OverdueReceivablesAmount);
    }

    [Fact]
    public async Task SoftDelete_ExcludesSoftDeletedRecordsFromAllMetrics()
    {
        await SeedBaseDataAsync();
        var today = DateTime.Today;

        await using (var context = _db.CreateContext())
        {
            var delSale = new Sale
            {
                InvoiceNumber = "INV-DEL-01",
                SaleDate = today,
                CustomerId = 1,
                UserId = "user-stress",
                TotalAmount = 5_000_000m,
                RemainingAmount = 5_000_000m,
                PaymentStatus = PaymentStatus.Unpaid
            };
            var actSale = new Sale
            {
                InvoiceNumber = "INV-ACT-01",
                SaleDate = today,
                CustomerId = 1,
                UserId = "user-stress",
                TotalAmount = 1_000_000m,
                RemainingAmount = 1_000_000m,
                PaymentStatus = PaymentStatus.Unpaid
            };
            context.Sales.AddRange(delSale, actSale);

            var delPo = new PurchaseOrder
            {
                PONumber = "PO-DEL-01",
                OrderDate = today,
                SupplierId = 1,
                UserId = "user-stress",
                TotalAmount = 10_000_000m,
                RemainingAmount = 10_000_000m,
                Status = PurchaseOrderStatus.Received,
                PaymentStatus = PaymentStatus.Unpaid
            };
            context.PurchaseOrders.Add(delPo);

            var delProd = new Product
            {
                Name = "Deleted Battery",
                Stock = 0,
                ReorderPoint = 5
            };
            var actProd = new Product
            {
                Name = "Active Battery",
                Stock = 2,
                ReorderPoint = 5
            };
            context.Products.AddRange(delProd, actProd);

            await context.SaveChangesAsync();

            // Soft-delete the entities via context.Remove
            context.Sales.Remove(delSale);
            context.PurchaseOrders.Remove(delPo);
            context.Products.Remove(delProd);
            await context.SaveChangesAsync();
        }

        var service = CreateService(TestUser.InRole(Roles.Admin));
        var result = await service.GetSummaryAsync();

        Assert.True(result.IsSuccess);
        // Only active sale counted
        Assert.Equal(1, result.Value.TodaySaleCount);
        Assert.Equal(1_000_000m, result.Value.TodayNetSales);
        Assert.Equal(1_000_000m, result.Value.TotalReceivables);

        // Soft-deleted PO not counted
        Assert.Equal(0, result.Value.TotalPayables);
        Assert.Equal(0, result.Value.UnpaidPoCount);

        // Soft-deleted product not counted
        Assert.Equal(1, result.Value.LowStockProductCount);
        Assert.Equal(0, result.Value.OutOfStockProductCount);
        Assert.Equal(1, result.Value.TotalActiveProductCount);
    }

    [Fact]
    public async Task TopProducts_StressScenarios_FiltersZeroAndNegativeAndCapsAtTop5()
    {
        await SeedBaseDataAsync();
        var today = DateTime.Today;

        await using (var context = _db.CreateContext())
        {
            // Create 7 products
            for (int i = 1; i <= 7; i++)
            {
                context.Products.Add(new Product
                {
                    Id = i,
                    Name = $"Product {i}",
                    Stock = 10,
                    ReorderPoint = 5
                });
            }
            await context.SaveChangesAsync();

            var sale = new Sale
            {
                InvoiceNumber = "INV-TOP-STRESS",
                SaleDate = today,
                CustomerId = 1,
                UserId = "user-stress",
                TotalAmount = 10_000_000m,
                PaymentStatus = PaymentStatus.Paid
            };
            context.Sales.Add(sale);
            await context.SaveChangesAsync();

            // Product 1: 10 sold, 0 returned -> net 10
            // Product 2: 8 sold, 0 returned -> net 8
            // Product 3: 6 sold, 0 returned -> net 6
            // Product 4: 4 sold, 0 returned -> net 4
            // Product 5: 3 sold, 0 returned -> net 3
            // Product 6: 2 sold, 0 returned -> net 2 (should be 6th, so excluded from top 5)
            // Product 7: 5 sold, 5 returned -> net 0 (should be excluded)
            context.SaleDetails.AddRange(
                new SaleDetail { SaleId = sale.Id, ProductId = 1, Quantity = 10, UnitPrice = 100_000m, Subtotal = 1_000_000m },
                new SaleDetail { SaleId = sale.Id, ProductId = 2, Quantity = 8, UnitPrice = 100_000m, Subtotal = 800_000m },
                new SaleDetail { SaleId = sale.Id, ProductId = 3, Quantity = 6, UnitPrice = 100_000m, Subtotal = 600_000m },
                new SaleDetail { SaleId = sale.Id, ProductId = 4, Quantity = 4, UnitPrice = 100_000m, Subtotal = 400_000m },
                new SaleDetail { SaleId = sale.Id, ProductId = 5, Quantity = 3, UnitPrice = 100_000m, Subtotal = 300_000m },
                new SaleDetail { SaleId = sale.Id, ProductId = 6, Quantity = 2, UnitPrice = 100_000m, Subtotal = 200_000m },
                new SaleDetail { SaleId = sale.Id, ProductId = 7, Quantity = 5, UnitPrice = 100_000m, Subtotal = 500_000m }
            );

            var saleReturn = new SaleReturn
            {
                ReturnNumber = "SRT-TOP-STRESS",
                ReturnDate = today,
                SaleId = sale.Id,
                UserId = "user-stress",
                TotalAmount = 500_000m
            };
            context.SaleReturns.Add(saleReturn);
            await context.SaveChangesAsync();

            context.SaleReturnDetails.Add(new SaleReturnDetail
            {
                SaleReturnId = saleReturn.Id,
                ProductId = 7,
                Quantity = 5,
                UnitPrice = 100_000m,
                Subtotal = 500_000m
            });
            await context.SaveChangesAsync();
        }

        var service = CreateService(TestUser.InRole(Roles.Admin));
        var result = await service.GetSummaryAsync(DashboardTrendPeriod.Last7Days);

        Assert.True(result.IsSuccess);
        // Must contain exactly 5 products
        Assert.Equal(5, result.Value.TopProducts.Count);

        // Net quantities should be: 10, 8, 6, 4, 3
        Assert.Equal(1, result.Value.TopProducts[0].ProductId);
        Assert.Equal(10, result.Value.TopProducts[0].NetQuantitySold);

        Assert.Equal(2, result.Value.TopProducts[1].ProductId);
        Assert.Equal(8, result.Value.TopProducts[1].NetQuantitySold);

        Assert.Equal(3, result.Value.TopProducts[2].ProductId);
        Assert.Equal(6, result.Value.TopProducts[2].NetQuantitySold);

        Assert.Equal(4, result.Value.TopProducts[3].ProductId);
        Assert.Equal(4, result.Value.TopProducts[3].NetQuantitySold);

        Assert.Equal(5, result.Value.TopProducts[4].ProductId);
        Assert.Equal(3, result.Value.TopProducts[4].NetQuantitySold);

        // Product 6 (net 2) and Product 7 (net 0) must NOT be in top 5
        Assert.DoesNotContain(result.Value.TopProducts, p => p.ProductId == 6);
        Assert.DoesNotContain(result.Value.TopProducts, p => p.ProductId == 7);

        // Total sold across all aggregated products = 10 + 8 + 6 + 4 + 3 + 2 = 33
        // Check share percentage calculations
        var expectedShare1 = Math.Round(10.0 / 33.0 * 100.0, 1);
        Assert.Equal(expectedShare1, result.Value.TopProducts[0].SharePercentage);
        Assert.All(result.Value.TopProducts, p => Assert.InRange(p.SharePercentage, 0.0, 100.0));
    }

    [Fact]
    public async Task EmptyDatabase_ReturnsZeroMetricsWithoutExceptions()
    {
        // Completely empty database
        var service = CreateService(TestUser.InRole(Roles.Admin));
        var result = await service.GetSummaryAsync(DashboardTrendPeriod.Last7Days);

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
        Assert.Equal(7, s.TrendPoints.Count);
        Assert.Empty(s.TopProducts);
        Assert.Empty(s.LowStockAlerts);
        Assert.Empty(s.DueAndOverdueItems);
        Assert.Empty(s.PendingPurchaseOrders);
        Assert.Empty(s.RecentSales);
    }
}
