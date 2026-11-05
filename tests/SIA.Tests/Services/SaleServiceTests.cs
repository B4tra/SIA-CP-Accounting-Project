using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.Sales;
using SIA.Domain.Entities.Inventory;
using SIA.Domain.Entities.Sales;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data.Seed;
using SIA.Infrastructure.Identity;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;

namespace SIA.Tests.Services;

public class SaleServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private SaleService CreateService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);
        var numbers = new TransactionNumberService(context, user);
        return new SaleService(context, user, numbers);
    }

    private async Task<(int customerId, int productAId, int productBId)> SeedFixtureAsync(int stockA = 10, int stockB = 5)
    {
        await using var context = _db.CreateContext();

        await MasterDataSeeder.SeedTransactionNumberConfigsAsync(context, 2026, NullLogger.Instance);

        if (!await context.Users.AnyAsync(u => u.Id == "user-test"))
        {
            context.Users.Add(new ApplicationUser
            {
                Id = "user-test",
                UserName = "tester@sia.local",
                Email = "tester@sia.local",
                FullName = "Kasir Uji",
            });
        }

        var customer = new Customer { Name = "Bengkel Jaya", Phone = "08123456789" };
        var productA = new Product
        {
            Name = "GS Astra NS40ZL",
            SKU = "GS-NS40ZL",
            SellingPrice = 750_000m,
            PurchasePrice = 600_000m,
            Stock = stockA,
        };
        var productB = new Product
        {
            Name = "Yuasa YTZ5S",
            SKU = "YUA-YTZ5S",
            SellingPrice = 250_000m,
            PurchasePrice = 190_000m,
            Stock = stockB,
        };

        context.Customers.Add(customer);
        context.Products.AddRange(productA, productB);
        await context.SaveChangesAsync();

        return (customer.Id, productA.Id, productB.Id);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    public async Task Create_Lunas_StokBerkurang_StockCardOutTertulis_InvoiceOtomatis_DanPaymentTercatat(string role)
    {
        var (customerId, productAId, productBId) = await SeedFixtureAsync(stockA: 10, stockB: 5);
        var saleDate = new DateTime(2026, 5, 10);

        var result = await CreateService(role).CreateAsync(new CreateSaleDto
        {
            CustomerId = customerId,
            SaleDate = saleDate,
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 1_750_000m,
            Notes = "Penjualan tunai",
            Details = new List<CreateSaleDetailDto>
            {
                new() { ProductId = productAId, Quantity = 2, UnitPrice = 750_000m },
                new() { ProductId = productBId, Quantity = 1, UnitPrice = 250_000m },
            },
        });

        Assert.True(result.IsSuccess, result.Error);
        var sale = result.Value!;
        Assert.Equal("INV-2026-0001", sale.InvoiceNumber);
        Assert.Equal(1_750_000m, sale.TotalAmount);
        Assert.Equal(1_750_000m, sale.PaidAmount);
        Assert.Equal(0m, sale.RemainingAmount);
        Assert.Equal(PaymentStatus.Paid, sale.PaymentStatus);
        Assert.Equal("Kasir Uji", sale.UserName);
        Assert.Equal(2, sale.Details.Count);

        await using var context = _db.CreateContext();
        var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
        var prodB = await context.Products.SingleAsync(p => p.Id == productBId);
        Assert.Equal(8, prodA.Stock);
        Assert.Equal(4, prodB.Stock);

        var cards = await context.StockCards.OrderBy(c => c.ProductId).ToListAsync();
        Assert.Equal(2, cards.Count);
        Assert.All(cards, c =>
        {
            Assert.Equal(StockMovementType.Out, c.MovementType);
            Assert.Equal("INV-2026-0001", c.Reference);
        });
        Assert.Equal(10, cards[0].StockBefore);
        Assert.Equal(8, cards[0].StockAfter);
        Assert.Equal(2, cards[0].Quantity);

        var payments = await context.Payments.ToListAsync();
        var payment = Assert.Single(payments);
        Assert.Equal("PAY-2026-0001", payment.PaymentNumber);
        Assert.Equal(ReferenceType.Sale, payment.ReferenceType);
        Assert.Equal(sale.Id, payment.ReferenceId);
        Assert.Equal(1_750_000m, payment.Amount);

        // Jurnal umum TIDAK boleh diposting otomatis
        Assert.False(await context.JournalEntries.AnyAsync());
    }

    [Fact]
    public async Task Create_KreditSebagian_DenganDueDate_StatusPartiallyPaid_DanPaymentAwalTercatat()
    {
        var (customerId, productAId, _) = await SeedFixtureAsync(stockA: 5);

        var result = await CreateService(Roles.Staff).CreateAsync(new CreateSaleDto
        {
            CustomerId = customerId,
            SaleDate = new DateTime(2026, 6, 1),
            DueDate = new DateTime(2026, 6, 15),
            PaymentMethod = PaymentMethod.Transfer,
            PaidAmount = 300_000m,
            Details = new List<CreateSaleDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 750_000m },
            },
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(750_000m, result.Value!.TotalAmount);
        Assert.Equal(300_000m, result.Value.PaidAmount);
        Assert.Equal(450_000m, result.Value.RemainingAmount);
        Assert.Equal(PaymentStatus.PartiallyPaid, result.Value.PaymentStatus);
        Assert.Equal(new DateTime(2026, 6, 15), result.Value.DueDate);

        await using var context = _db.CreateContext();
        var payment = Assert.Single(await context.Payments.ToListAsync());
        Assert.Equal(300_000m, payment.Amount);
    }

    [Fact]
    public async Task Create_KreditPenuh_PaidNol_StatusUnpaid_TanpaRecordPayment()
    {
        var (customerId, productAId, _) = await SeedFixtureAsync(stockA: 5);

        var result = await CreateService(Roles.Staff).CreateAsync(new CreateSaleDto
        {
            CustomerId = customerId,
            SaleDate = new DateTime(2026, 6, 1),
            DueDate = new DateTime(2026, 6, 30),
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 0m,
            Details = new List<CreateSaleDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 750_000m },
            },
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(PaymentStatus.Unpaid, result.Value!.PaymentStatus);
        Assert.Equal(750_000m, result.Value.RemainingAmount);

        await using var context = _db.CreateContext();
        Assert.False(await context.Payments.AnyAsync());
    }

    [Fact]
    public async Task Create_KreditTanpaDueDate_Ditolak_StokTidakBerubah()
    {
        var (customerId, productAId, _) = await SeedFixtureAsync(stockA: 5);

        var result = await CreateService(Roles.Staff).CreateAsync(new CreateSaleDto
        {
            CustomerId = customerId,
            SaleDate = new DateTime(2026, 6, 1),
            DueDate = null,
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 200_000m,
            Details = new List<CreateSaleDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 750_000m },
            },
        });

        Assert.True(result.IsFailure);
        await using var context = _db.CreateContext();
        Assert.Equal(5, (await context.Products.SingleAsync(p => p.Id == productAId)).Stock);
        Assert.False(await context.Sales.AnyAsync());
        Assert.False(await context.StockCards.AnyAsync());
    }

    [Fact]
    public async Task Create_DueDateSebelumSaleDate_Ditolak()
    {
        var (customerId, productAId, _) = await SeedFixtureAsync(stockA: 5);

        var result = await CreateService(Roles.Staff).CreateAsync(new CreateSaleDto
        {
            CustomerId = customerId,
            SaleDate = new DateTime(2026, 6, 10),
            DueDate = new DateTime(2026, 6, 5),
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 0m,
            Details = new List<CreateSaleDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 750_000m },
            },
        });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Create_StokTidakCukup_Ditolak_StokDanStockCardTidakBerubah()
    {
        var (customerId, productAId, _) = await SeedFixtureAsync(stockA: 2);

        var result = await CreateService(Roles.Staff).CreateAsync(new CreateSaleDto
        {
            CustomerId = customerId,
            SaleDate = new DateTime(2026, 6, 1),
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 2_250_000m,
            Details = new List<CreateSaleDetailDto>
            {
                new() { ProductId = productAId, Quantity = 3, UnitPrice = 750_000m },
            },
        });

        Assert.True(result.IsFailure);
        Assert.Contains("Stok", result.Error!);

        await using var context = _db.CreateContext();
        Assert.Equal(2, (await context.Products.SingleAsync(p => p.Id == productAId)).Stock);
        Assert.False(await context.Sales.AnyAsync());
        Assert.False(await context.StockCards.AnyAsync());
    }

    [Fact]
    public async Task Create_PaidAmountMelebihiTotal_Ditolak()
    {
        var (customerId, productAId, _) = await SeedFixtureAsync(stockA: 5);

        var result = await CreateService(Roles.Staff).CreateAsync(new CreateSaleDto
        {
            CustomerId = customerId,
            SaleDate = new DateTime(2026, 6, 1),
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 800_000m,
            Details = new List<CreateSaleDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 750_000m },
            },
        });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Create_ProdukDuplikatDalamSatuNota_Ditolak()
    {
        var (customerId, productAId, _) = await SeedFixtureAsync(stockA: 10);

        var result = await CreateService(Roles.Staff).CreateAsync(new CreateSaleDto
        {
            CustomerId = customerId,
            SaleDate = new DateTime(2026, 6, 1),
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 1_500_000m,
            Details = new List<CreateSaleDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 750_000m },
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 750_000m },
            },
        });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Create_Viewer_Ditolak()
    {
        var (customerId, productAId, _) = await SeedFixtureAsync(stockA: 5);

        var result = await CreateService(Roles.Viewer).CreateAsync(new CreateSaleDto
        {
            CustomerId = customerId,
            SaleDate = new DateTime(2026, 6, 1),
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 750_000m,
            Details = new List<CreateSaleDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 750_000m },
            },
        });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetAll_DanGetOutstanding_BerfungsiUntukViewer()
    {
        var (customerId, productAId, _) = await SeedFixtureAsync(stockA: 10);
        var staffService = CreateService(Roles.Staff);

        await staffService.CreateAsync(new CreateSaleDto
        {
            CustomerId = customerId,
            SaleDate = new DateTime(2026, 6, 1),
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 750_000m,
            Details = new List<CreateSaleDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 750_000m },
            },
        });

        await staffService.CreateAsync(new CreateSaleDto
        {
            CustomerId = customerId,
            SaleDate = new DateTime(2026, 6, 5),
            DueDate = new DateTime(2026, 6, 20),
            PaymentMethod = PaymentMethod.Transfer,
            PaidAmount = 250_000m,
            Details = new List<CreateSaleDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 750_000m },
            },
        });

        var viewerService = CreateService(Roles.Viewer);
        var all = await viewerService.GetAllAsync(new SaleFilterDto());
        Assert.True(all.IsSuccess);
        Assert.Equal(2, all.Value!.Count);

        var unpaidOnly = await viewerService.GetAllAsync(new SaleFilterDto { PaymentStatus = PaymentStatus.PartiallyPaid });
        Assert.Single(unpaidOnly.Value!);

        var outstanding = await viewerService.GetOutstandingAsync();
        Assert.True(outstanding.IsSuccess);
        var item = Assert.Single(outstanding.Value!);
        Assert.Equal("INV-2026-0002", item.ReferenceNumber);
        Assert.Equal(500_000m, item.RemainingAmount);
    }
}
