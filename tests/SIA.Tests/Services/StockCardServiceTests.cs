using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.Inventory;
using SIA.Application.DTOs.Purchasing;
using SIA.Application.DTOs.Sales;
using SIA.Domain.Entities.Inventory;
using SIA.Domain.Entities.Purchasing;
using SIA.Domain.Entities.Sales;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data.Seed;
using SIA.Infrastructure.Identity;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;

namespace SIA.Tests.Services;

public class StockCardServiceTests : IDisposable
{
    private const string ReadDenied = "Anda tidak memiliki hak akses untuk melihat data ini.";
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private StockCardService CreateService(params string[] roles) =>
        new(_db.CreateContext(), TestUser.InRole(roles));

    private PurchaseOrderService CreatePoService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);
        return new PurchaseOrderService(context, user, new TransactionNumberService(context, user));
    }

    private SaleService CreateSaleService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);
        return new SaleService(context, user, new TransactionNumberService(context, user));
    }

    private SaleReturnService CreateSaleReturnService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);
        return new SaleReturnService(context, user, new TransactionNumberService(context, user));
    }

    private PurchaseReturnService CreatePurchaseReturnService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);
        return new PurchaseReturnService(context, user, new TransactionNumberService(context, user));
    }

    private async Task<(Product prodA, Product prodB)> SeedStockCardsFixtureAsync()
    {
        await using var context = _db.CreateContext();

        var prodA = new Product
        {
            Name = "GS Astra NS40",
            SKU = "GS-NS40",
            Brand = "GS Astra",
            PurchasePrice = 600_000m,
            SellingPrice = 750_000m,
            Stock = 8,
            ReorderPoint = 3,
        };
        var prodB = new Product
        {
            Name = "Yuasa YTZ5S",
            SKU = "YU-YTZ5S",
            Brand = "Yuasa",
            PurchasePrice = 180_000m,
            SellingPrice = 240_000m,
            Stock = 2,
            ReorderPoint = 5,
        };
        var deletedProduct = new Product
        {
            Name = "Aki Terhapus",
            SKU = "DEL-01",
            Brand = "Obsolete",
            PurchasePrice = 100_000m,
            SellingPrice = 150_000m,
            Stock = 0,
            ReorderPoint = 5,
        };

        context.Products.AddRange(prodA, prodB, deletedProduct);
        await context.SaveChangesAsync();

        context.StockCards.AddRange(
            new StockCard
            {
                ProductId = prodA.Id,
                MovementDate = new DateTime(2026, 5, 1, 9, 0, 0),
                MovementType = StockMovementType.In,
                Quantity = 10,
                StockBefore = 0,
                StockAfter = 10,
                Reference = "PO-2026-0001",
            },
            new StockCard
            {
                ProductId = prodA.Id,
                MovementDate = new DateTime(2026, 5, 5, 14, 30, 0),
                MovementType = StockMovementType.Out,
                Quantity = 4,
                StockBefore = 10,
                StockAfter = 6,
                Reference = "INV-2026-0001",
            },
            new StockCard
            {
                ProductId = prodA.Id,
                MovementDate = new DateTime(2026, 5, 5, 23, 59, 0),
                MovementType = StockMovementType.ReturnIn,
                Quantity = 2,
                StockBefore = 6,
                StockAfter = 8,
                Reference = "SRT-2026-0001",
            },
            new StockCard
            {
                ProductId = prodB.Id,
                MovementDate = new DateTime(2026, 5, 6, 8, 0, 0),
                MovementType = StockMovementType.ReturnOut,
                Quantity = 1,
                StockBefore = 3,
                StockAfter = 2,
                Reference = "PRT-2026-0001",
            },
            new StockCard
            {
                ProductId = deletedProduct.Id,
                MovementDate = new DateTime(2026, 5, 7, 10, 0, 0),
                MovementType = StockMovementType.In,
                Quantity = 5,
                StockBefore = 0,
                StockAfter = 5,
                Reference = "PO-2026-9999",
            });

        await context.SaveChangesAsync();

        await using var deleteContext = _db.CreateContext();
        var productToDelete = await deleteContext.Products.SingleAsync(p => p.Id == deletedProduct.Id);
        deleteContext.Products.Remove(productToDelete);
        await deleteContext.SaveChangesAsync();

        return (prodA, prodB);
    }

    [Fact]
    public async Task Anonymous_SemuaMethod_Ditolak()
    {
        var service = CreateService();

        var allResult = await service.GetAllAsync();
        var byProductResult = await service.GetByProductAsync(1);
        var alertsResult = await service.GetReorderAlertsAsync();

        Assert.True(allResult.IsFailure);
        Assert.Equal(ReadDenied, allResult.Error);

        Assert.True(byProductResult.IsFailure);
        Assert.Equal(ReadDenied, byProductResult.Error);

        Assert.True(alertsResult.IsFailure);
        Assert.Equal(ReadDenied, alertsResult.Error);
    }

    [Theory]
    [InlineData("Guest")]
    [InlineData("Unknown")]
    public async Task NonPolicyRoles_SemuaMethod_Ditolak(string role)
    {
        var service = CreateService(role);

        var allResult = await service.GetAllAsync();
        var byProductResult = await service.GetByProductAsync(1);
        var alertsResult = await service.GetReorderAlertsAsync();

        Assert.True(allResult.IsFailure);
        Assert.Equal(ReadDenied, allResult.Error);

        Assert.True(byProductResult.IsFailure);
        Assert.Equal(ReadDenied, byProductResult.Error);

        Assert.True(alertsResult.IsFailure);
        Assert.Equal(ReadDenied, alertsResult.Error);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Viewer)]
    public async Task AuthorizedRoles_SemuaMethod_Diizinkan(string role)
    {
        var (prodA, _) = await SeedStockCardsFixtureAsync();
        var service = CreateService(role);

        var allResult = await service.GetAllAsync();
        var byProductResult = await service.GetByProductAsync(prodA.Id);
        var alertsResult = await service.GetReorderAlertsAsync();

        Assert.True(allResult.IsSuccess, allResult.Error);
        Assert.True(byProductResult.IsSuccess, byProductResult.Error);
        Assert.True(alertsResult.IsSuccess, alertsResult.Error);
    }

    [Fact]
    public async Task GetByProduct_ProdukTidakAdaAtauSoftDeleted_Gagal()
    {
        await SeedStockCardsFixtureAsync();
        await using var context = _db.CreateContext();
        var deletedId = await context.Products
            .IgnoreQueryFilters()
            .Where(p => p.SKU == "DEL-01")
            .Select(p => p.Id)
            .SingleAsync();

        var service = CreateService(Roles.Viewer);

        var missingResult = await service.GetByProductAsync(999_999);
        Assert.True(missingResult.IsFailure);
        Assert.Equal("Produk tidak ditemukan.", missingResult.Error);

        var deletedResult = await service.GetByProductAsync(deletedId);
        Assert.True(deletedResult.IsFailure);
        Assert.Equal("Produk tidak ditemukan.", deletedResult.Error);
    }

    [Fact]
    public async Task GetByProduct_UrutanTerbaruDanFilterTanggalInklusif_Akurat()
    {
        var (prodA, _) = await SeedStockCardsFixtureAsync();
        var service = CreateService(Roles.Viewer);

        var allForProdA = await service.GetByProductAsync(prodA.Id);
        Assert.True(allForProdA.IsSuccess, allForProdA.Error);
        Assert.Equal(3, allForProdA.Value!.Count);

        // Ordered newest-first: ReturnIn (May 5 23:59), Out (May 5 14:30), In (May 1 09:00)
        Assert.Equal("SRT-2026-0001", allForProdA.Value[0].Reference);
        Assert.Equal("GS Astra NS40", allForProdA.Value[0].ProductName);
        Assert.Equal("GS-NS40", allForProdA.Value[0].SKU);
        Assert.Equal("GS Astra", allForProdA.Value[0].Brand);

        Assert.Equal("INV-2026-0001", allForProdA.Value[1].Reference);
        Assert.Equal("PO-2026-0001", allForProdA.Value[2].Reference);

        // Filter dateFrom=May 5, dateTo=May 5 must include 23:59 on May 5 and exclude May 1
        var filtered = await service.GetByProductAsync(
            prodA.Id,
            dateFrom: new DateTime(2026, 5, 5),
            dateTo: new DateTime(2026, 5, 5));

        Assert.True(filtered.IsSuccess, filtered.Error);
        Assert.Equal(2, filtered.Value!.Count);
        Assert.Equal(new[] { "SRT-2026-0001", "INV-2026-0001" }, filtered.Value.Select(x => x.Reference).ToArray());
    }

    [Fact]
    public async Task GetAll_FilterKombinasiDanPencarian_BerfungsiAkurat()
    {
        var (prodA, prodB) = await SeedStockCardsFixtureAsync();
        var service = CreateService(Roles.Staff);

        // Null filter returns all 4 active cards (excludes deleted product's card), newest-first
        var unfiltered = await service.GetAllAsync();
        Assert.True(unfiltered.IsSuccess, unfiltered.Error);
        Assert.Equal(4, unfiltered.Value!.Count);
        Assert.Equal(
            new[] { "PRT-2026-0001", "SRT-2026-0001", "INV-2026-0001", "PO-2026-0001" },
            unfiltered.Value.Select(x => x.Reference).ToArray());

        // Filter by ProductId
        var byProdB = await service.GetAllAsync(new StockCardFilterDto { ProductId = prodB.Id });
        Assert.True(byProdB.IsSuccess);
        Assert.Single(byProdB.Value!);
        Assert.Equal("PRT-2026-0001", byProdB.Value![0].Reference);

        // Filter by MovementType
        var byType = await service.GetAllAsync(new StockCardFilterDto { MovementType = StockMovementType.Out });
        Assert.True(byType.IsSuccess);
        Assert.Single(byType.Value!);
        Assert.Equal("INV-2026-0001", byType.Value![0].Reference);

        // Filter by Search on Reference, Product Name, SKU, and Brand (case-insensitive)
        var byRef = await service.GetAllAsync(new StockCardFilterDto { Search = "inv-2026-0001" });
        Assert.Single(byRef.Value!);
        Assert.Equal("INV-2026-0001", byRef.Value![0].Reference);

        var byName = await service.GetAllAsync(new StockCardFilterDto { Search = "ns40" });
        Assert.Equal(3, byName.Value!.Count);

        var bySku = await service.GetAllAsync(new StockCardFilterDto { Search = "yu-ytz5s" });
        Assert.Single(bySku.Value!);
        Assert.Equal(prodB.Id, bySku.Value![0].ProductId);

        var byBrand = await service.GetAllAsync(new StockCardFilterDto { Search = "yuasa" });
        Assert.Single(byBrand.Value!);
        Assert.Equal(prodB.Id, byBrand.Value![0].ProductId);

        // Empty and whitespace Search should be ignored
        var byEmptySearch = await service.GetAllAsync(new StockCardFilterDto { Search = "" });
        Assert.True(byEmptySearch.IsSuccess);
        Assert.Equal(4, byEmptySearch.Value!.Count);

        var byWhitespaceSearch = await service.GetAllAsync(new StockCardFilterDto { Search = "   " });
        Assert.True(byWhitespaceSearch.IsSuccess);
        Assert.Equal(4, byWhitespaceSearch.Value!.Count);

        // Zero-match Search returns empty list
        var byNoMatch = await service.GetAllAsync(new StockCardFilterDto { Search = "TIDAK-ADA-999" });
        Assert.True(byNoMatch.IsSuccess);
        Assert.Empty(byNoMatch.Value!);

        // Combined filter: ProductA + DateFrom May 2 + DateTo May 5 + MovementType ReturnIn
        var combined = await service.GetAllAsync(new StockCardFilterDto
        {
            ProductId = prodA.Id,
            DateFrom = new DateTime(2026, 5, 2),
            DateTo = new DateTime(2026, 5, 5),
            MovementType = StockMovementType.ReturnIn,
        });
        Assert.True(combined.IsSuccess);
        Assert.Single(combined.Value!);
        Assert.Equal("SRT-2026-0001", combined.Value![0].Reference);
    }

    [Fact]
    public async Task GetReorderAlerts_HanyaProdukStokDiBawahAtauSamaBatas_UrutStokTerendah()
    {
        await using (var context = _db.CreateContext())
        {
            var deletedAlertProduct = new Product
            {
                Name = "Aki Dihapus",
                SKU = "DEL-ALERT",
                Stock = 0,
                ReorderPoint = 5,
            };

            context.Products.AddRange(
                new Product { Name = "Aki Aman", SKU = "AMAN-1", Brand = "GS", Stock = 6, ReorderPoint = 5 },
                new Product { Name = "Aki Batas Sama", SKU = "BATAS-1", Brand = "GS", Stock = 5, ReorderPoint = 5 },
                new Product { Name = "Aki Habis", SKU = "HABIS-1", Brand = "Yuasa", Stock = 0, ReorderPoint = 3 },
                new Product { Name = "Aki Menipis B", SKU = "TIPIS-B", Brand = "Incoe", Stock = 2, ReorderPoint = 4 },
                new Product { Name = "Aki Menipis A", SKU = "TIPIS-A", Brand = "Incoe", Stock = 2, ReorderPoint = 4 },
                deletedAlertProduct);
            await context.SaveChangesAsync();

            context.Products.Remove(deletedAlertProduct);
            await context.SaveChangesAsync();
        }

        var result = await CreateService(Roles.Viewer).GetReorderAlertsAsync();

        Assert.True(result.IsSuccess, result.Error);
        var alerts = result.Value!;
        Assert.Equal(4, alerts.Count);
        Assert.Equal(
            new[] { "Aki Habis", "Aki Menipis A", "Aki Menipis B", "Aki Batas Sama" },
            alerts.Select(a => a.ProductName).ToArray());
        Assert.Equal(new[] { 0, 2, 2, 5 }, alerts.Select(a => a.Stock).ToArray());
    }

    [Fact]
    public async Task GetReorderAlerts_StokNegatif_MunculPertama_DanKosongSaatSemuaAman()
    {
        await using (var context = _db.CreateContext())
        {
            context.Products.AddRange(
                new Product { Name = "Aki Minus", SKU = "MINUS-1", Brand = "GS", Stock = -2, ReorderPoint = 3 },
                new Product { Name = "Aki Nol", SKU = "NOL-1", Brand = "GS", Stock = 0, ReorderPoint = 3 },
                new Product { Name = "Aki Aman", SKU = "AMAN-2", Brand = "Yuasa", Stock = 10, ReorderPoint = 3 });
            await context.SaveChangesAsync();
        }

        var result = await CreateService(Roles.Viewer).GetReorderAlertsAsync();

        Assert.True(result.IsSuccess, result.Error);
        var alerts = result.Value!;
        Assert.Equal(2, alerts.Count);
        Assert.Equal(new[] { "Aki Minus", "Aki Nol" }, alerts.Select(a => a.ProductName).ToArray());
        Assert.Equal(new[] { -2, 0 }, alerts.Select(a => a.Stock).ToArray());
    }

    [Fact]
    public async Task GetReorderAlerts_SemuaProdukDiAtasBatas_MengembalikanDaftarKosong()
    {
        await using (var context = _db.CreateContext())
        {
            var deletedLowStock = new Product
            {
                Name = "Aki Dihapus Stok Nol",
                SKU = "DEL-ZERO",
                Stock = 0,
                ReorderPoint = 5,
            };

            context.Products.AddRange(
                new Product { Name = "Aki Aman A", SKU = "AMAN-A", Stock = 10, ReorderPoint = 5 },
                new Product { Name = "Aki Aman B", SKU = "AMAN-B", Stock = 6, ReorderPoint = 5 },
                deletedLowStock);
            await context.SaveChangesAsync();

            context.Products.Remove(deletedLowStock);
            await context.SaveChangesAsync();
        }

        var result = await CreateService(Roles.Viewer).GetReorderAlertsAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task GetAll_DanGetByProduct_BatasTanggalNonMidnight_RentangTerbalik_DanDateTimeMaxValue_Akurat()
    {
        int prodId;
        await using (var context = _db.CreateContext())
        {
            var prod = new Product
            {
                Name = "GS Astra Boundary",
                SKU = "BND-01",
                Brand = "GS Astra",
                Stock = 5,
                ReorderPoint = 2,
            };
            context.Products.Add(prod);
            await context.SaveChangesAsync();
            prodId = prod.Id;

            context.StockCards.AddRange(
                new StockCard
                {
                    ProductId = prodId,
                    MovementDate = new DateTime(2026, 5, 5, 0, 0, 0),
                    MovementType = StockMovementType.In,
                    Quantity = 5,
                    StockBefore = 0,
                    StockAfter = 5,
                    Reference = "PO-2026-0101",
                },
                new StockCard
                {
                    ProductId = prodId,
                    MovementDate = new DateTime(2026, 5, 5, 23, 59, 59),
                    MovementType = StockMovementType.Out,
                    Quantity = 2,
                    StockBefore = 5,
                    StockAfter = 3,
                    Reference = "INV-2026-0101",
                },
                new StockCard
                {
                    ProductId = prodId,
                    MovementDate = new DateTime(2026, 5, 6, 0, 0, 0),
                    MovementType = StockMovementType.ReturnIn,
                    Quantity = 1,
                    StockBefore = 3,
                    StockAfter = 4,
                    Reference = "SRT-2026-0101",
                });
            await context.SaveChangesAsync();
        }

        var service = CreateService(Roles.Viewer);

        // Non-midnight DateFrom (18:30) and DateTo (08:15) on May 5 must cover [00:00:00, 23:59:59] of May 5
        var nonMidnightFrom = new DateTime(2026, 5, 5, 18, 30, 0);
        var nonMidnightTo = new DateTime(2026, 5, 5, 8, 15, 0);

        var allMay5 = await service.GetAllAsync(new StockCardFilterDto
        {
            ProductId = prodId,
            DateFrom = nonMidnightFrom,
            DateTo = nonMidnightTo,
        });
        Assert.True(allMay5.IsSuccess, allMay5.Error);
        Assert.Equal(new[] { "INV-2026-0101", "PO-2026-0101" }, allMay5.Value!.Select(x => x.Reference).ToArray());

        var byProdMay5 = await service.GetByProductAsync(prodId, nonMidnightFrom, nonMidnightTo);
        Assert.True(byProdMay5.IsSuccess, byProdMay5.Error);
        Assert.Equal(new[] { "INV-2026-0101", "PO-2026-0101" }, byProdMay5.Value!.Select(x => x.Reference).ToArray());

        // DateFrom > DateTo must return empty list
        var invertedAll = await service.GetAllAsync(new StockCardFilterDto
        {
            ProductId = prodId,
            DateFrom = new DateTime(2026, 5, 10),
            DateTo = new DateTime(2026, 5, 1),
        });
        Assert.True(invertedAll.IsSuccess, invertedAll.Error);
        Assert.Empty(invertedAll.Value!);

        var invertedByProd = await service.GetByProductAsync(prodId, new DateTime(2026, 5, 10), new DateTime(2026, 5, 1));
        Assert.True(invertedByProd.IsSuccess, invertedByProd.Error);
        Assert.Empty(invertedByProd.Value!);

        // DateTime.MaxValue boundary guard must not throw ArgumentOutOfRangeException
        var maxDateResult = await service.GetAllAsync(new StockCardFilterDto
        {
            ProductId = prodId,
            DateTo = DateTime.MaxValue,
        });
        Assert.True(maxDateResult.IsSuccess, maxDateResult.Error);
        Assert.Equal(3, maxDateResult.Value!.Count);
    }

    [Fact]
    public async Task GetByProduct_DanGetAll_StockCardSoftDeletedDikecualikan_DanProdukTanpaMutasiKosong()
    {
        int prodWithDeletedCardId;
        int emptyProdId;
        await using (var context = _db.CreateContext())
        {
            var prodWithDeletedCard = new Product
            {
                Name = "Produk Kartu Terhapus",
                SKU = "CARD-DEL",
                Stock = 5,
                ReorderPoint = 2,
            };
            var emptyProd = new Product
            {
                Name = "Produk Tanpa Mutasi",
                SKU = "NO-CARD",
                Stock = 0,
                ReorderPoint = 2,
            };
            context.Products.AddRange(prodWithDeletedCard, emptyProd);
            await context.SaveChangesAsync();

            prodWithDeletedCardId = prodWithDeletedCard.Id;
            emptyProdId = emptyProd.Id;

            var activeCard = new StockCard
            {
                ProductId = prodWithDeletedCardId,
                MovementDate = new DateTime(2026, 5, 10, 9, 0, 0),
                MovementType = StockMovementType.In,
                Quantity = 5,
                StockBefore = 0,
                StockAfter = 5,
                Reference = "PO-2026-0201",
            };
            var deletedCard = new StockCard
            {
                ProductId = prodWithDeletedCardId,
                MovementDate = new DateTime(2026, 5, 10, 10, 0, 0),
                MovementType = StockMovementType.Out,
                Quantity = 1,
                StockBefore = 5,
                StockAfter = 4,
                Reference = "INV-2026-0201",
            };
            context.StockCards.AddRange(activeCard, deletedCard);
            await context.SaveChangesAsync();

            context.StockCards.Remove(deletedCard);
            await context.SaveChangesAsync();
        }

        var service = CreateService(Roles.Viewer);

        var byProdResult = await service.GetByProductAsync(prodWithDeletedCardId);
        Assert.True(byProdResult.IsSuccess, byProdResult.Error);
        var singleByProd = Assert.Single(byProdResult.Value!);
        Assert.Equal("PO-2026-0201", singleByProd.Reference);

        var getAllResult = await service.GetAllAsync(new StockCardFilterDto { ProductId = prodWithDeletedCardId });
        Assert.True(getAllResult.IsSuccess, getAllResult.Error);
        var singleGetAll = Assert.Single(getAllResult.Value!);
        Assert.Equal("PO-2026-0201", singleGetAll.Reference);

        var emptyResult = await service.GetByProductAsync(emptyProdId);
        Assert.True(emptyResult.IsSuccess, emptyResult.Error);
        Assert.Empty(emptyResult.Value!);
    }

    [Fact]
    public async Task IntegrasiTransaksiNyata_PoReceive_Sale_SaleReturn_PurchaseReturn_TercatatAkuratDiKartuStok()
    {
        var today = DateTime.Today;
        int productId;
        int supplierId;
        int customerId;

        await using (var context = _db.CreateContext())
        {
            await MasterDataSeeder.SeedTransactionNumberConfigsAsync(context, today.Year, NullLogger.Instance);
            context.Users.Add(new ApplicationUser
            {
                Id = "user-test",
                UserName = "tester@sia.local",
                Email = "tester@sia.local",
                FullName = "Tester SIA",
            });

            var product = new Product
            {
                Name = "GS Astra Gold N50Z",
                SKU = "GS-N50Z",
                Brand = "GS Astra",
                PurchasePrice = 700_000m,
                SellingPrice = 900_000m,
                Stock = 0,
                ReorderPoint = 3,
            };
            var supplier = new Supplier { Name = "PT Sumber Aki", Phone = "08123456789" };
            var customer = new Customer { Name = "Bengkel Jaya", Phone = "08987654321" };

            context.Products.Add(product);
            context.Suppliers.Add(supplier);
            context.Customers.Add(customer);
            await context.SaveChangesAsync();

            productId = product.Id;
            supplierId = supplier.Id;
            customerId = customer.Id;
        }

        // 1. Purchase Order: Draft -> Approve (Admin) -> Receive (Staff) => +10 unit (0 -> 10)
        var poDraftResult = await CreatePoService(Roles.Staff).CreateDraftAsync(new CreatePurchaseOrderDto
        {
            OrderDate = today,
            SupplierId = supplierId,
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productId, Quantity = 10, UnitPrice = 700_000m },
            },
        });
        Assert.True(poDraftResult.IsSuccess, poDraftResult.Error);
        var poId = poDraftResult.Value!.Id;
        var poNumber = poDraftResult.Value.PONumber;

        var approveResult = await CreatePoService(Roles.Admin).ApproveAsync(poId);
        Assert.True(approveResult.IsSuccess, approveResult.Error);

        var receiveResult = await CreatePoService(Roles.Staff).ReceiveAsync(poId);
        Assert.True(receiveResult.IsSuccess, receiveResult.Error);

        // 2. Sale: -6 unit (10 -> 4)
        var saleResult = await CreateSaleService(Roles.Staff).CreateAsync(new CreateSaleDto
        {
            SaleDate = today.AddHours(1),
            CustomerId = customerId,
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 5_400_000m,
            Details = new List<CreateSaleDetailDto>
            {
                new() { ProductId = productId, Quantity = 6, UnitPrice = 900_000m },
            },
        });
        Assert.True(saleResult.IsSuccess, saleResult.Error);
        var saleId = saleResult.Value!.Id;
        var invoiceNumber = saleResult.Value.InvoiceNumber;

        // 3. Sale Return: +2 unit (4 -> 6)
        var saleReturnResult = await CreateSaleReturnService(Roles.Staff).CreateAsync(new CreateSaleReturnDto
        {
            ReturnDate = today.AddHours(2),
            SaleId = saleId,
            Reason = "Barang berlebih",
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productId, Quantity = 2 },
            },
        });
        Assert.True(saleReturnResult.IsSuccess, saleReturnResult.Error);
        var saleReturnNumber = saleReturnResult.Value!.ReturnNumber;

        // 4. Purchase Return: -3 unit (6 -> 3)
        var purchaseReturnResult = await CreatePurchaseReturnService(Roles.Staff).CreateAsync(new CreatePurchaseReturnDto
        {
            ReturnDate = today.AddHours(3),
            PurchaseOrderId = poId,
            Reason = "Cacat fisik",
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productId, Quantity = 3 },
            },
        });
        Assert.True(purchaseReturnResult.IsSuccess, purchaseReturnResult.Error);
        var purchaseReturnNumber = purchaseReturnResult.Value!.ReturnNumber;

        // Verify full StockCard chain via StockCardService as Viewer
        var cardService = CreateService(Roles.Viewer);
        var historyResult = await cardService.GetByProductAsync(productId);
        Assert.True(historyResult.IsSuccess, historyResult.Error);

        var cards = historyResult.Value!;
        Assert.Equal(4, cards.Count);

        // Newest-first order: PurchaseReturn -> SaleReturn -> Sale -> PurchaseOrder
        Assert.Equal(StockMovementType.ReturnOut, cards[0].MovementType);
        Assert.Equal(purchaseReturnNumber, cards[0].Reference);
        Assert.Equal(6, cards[0].StockBefore);
        Assert.Equal(3, cards[0].Quantity);
        Assert.Equal(3, cards[0].StockAfter);

        Assert.Equal(StockMovementType.ReturnIn, cards[1].MovementType);
        Assert.Equal(saleReturnNumber, cards[1].Reference);
        Assert.Equal(4, cards[1].StockBefore);
        Assert.Equal(2, cards[1].Quantity);
        Assert.Equal(6, cards[1].StockAfter);

        Assert.Equal(StockMovementType.Out, cards[2].MovementType);
        Assert.Equal(invoiceNumber, cards[2].Reference);
        Assert.Equal(10, cards[2].StockBefore);
        Assert.Equal(6, cards[2].Quantity);
        Assert.Equal(4, cards[2].StockAfter);

        Assert.Equal(StockMovementType.In, cards[3].MovementType);
        Assert.Equal(poNumber, cards[3].Reference);
        Assert.Equal(0, cards[3].StockBefore);
        Assert.Equal(10, cards[3].Quantity);
        Assert.Equal(10, cards[3].StockAfter);

        // Final stock = 3 == ReorderPoint (3), so product appears in reorder alerts
        var alertsResult = await cardService.GetReorderAlertsAsync();
        Assert.True(alertsResult.IsSuccess, alertsResult.Error);
        var alert = Assert.Single(alertsResult.Value!);
        Assert.Equal(productId, alert.ProductId);
        Assert.Equal(3, alert.Stock);
        Assert.Equal(3, alert.ReorderPoint);
    }
}
