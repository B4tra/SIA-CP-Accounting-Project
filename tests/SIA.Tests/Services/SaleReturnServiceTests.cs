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

public class SaleReturnServiceTests : IDisposable
{
    private const string WriteDenied = "Anda tidak memiliki hak akses untuk mengubah data ini.";
    private const string ReadDenied = "Anda tidak memiliki hak akses untuk melihat data ini.";
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private SaleReturnService CreateService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);
        var numbers = new TransactionNumberService(context, user);
        return new SaleReturnService(context, user, numbers);
    }

    private async Task<(int customerId, int productAId, int productBId, int saleId)> SeedFixtureAsync(
        int stockA = 10,
        int stockB = 5,
        int soldA = 5,
        int soldB = 2,
        decimal priceA = 750_000m,
        decimal priceB = 250_000m,
        DateTime? saleDate = null)
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
            SellingPrice = priceA,
            PurchasePrice = 600_000m,
            Stock = stockA,
        };
        var productB = new Product
        {
            Name = "Yuasa YTZ5S",
            SKU = "YUA-YTZ5S",
            SellingPrice = priceB,
            PurchasePrice = 190_000m,
            Stock = stockB,
        };

        context.Customers.Add(customer);
        context.Products.AddRange(productA, productB);
        await context.SaveChangesAsync();

        var date = saleDate ?? new DateTime(2026, 5, 10);
        var total = (soldA * priceA) + (soldB * priceB);
        var sale = new Sale
        {
            InvoiceNumber = "INV-2026-0001",
            SaleDate = date,
            CustomerId = customer.Id,
            UserId = "user-test",
            PaymentMethod = PaymentMethod.Cash,
            TotalAmount = total,
            PaidAmount = total,
            RemainingAmount = 0m,
            PaymentStatus = PaymentStatus.Paid,
            Notes = "Penjualan tunai awal",
            Details = new List<SaleDetail>
            {
                new() { ProductId = productA.Id, Quantity = soldA, UnitPrice = priceA, Subtotal = soldA * priceA },
                new() { ProductId = productB.Id, Quantity = soldB, UnitPrice = priceB, Subtotal = soldB * priceB },
            }
        };

        context.Sales.Add(sale);
        await context.SaveChangesAsync();

        return (customer.Id, productA.Id, productB.Id, sale.Id);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    public async Task Create_AdminDanStaff_Berhasil_GenerateNomorSRT_HargaDariDetail_StokBertambah_StockCardReturnIn_OriginalSaleTidakBerubah_TanpaJurnal(string role)
    {
        var (customerId, productAId, productBId, saleId) = await SeedFixtureAsync(
            stockA: 10, stockB: 5, soldA: 5, soldB: 2, priceA: 750_000m, priceB: 250_000m);
        var returnDate = new DateTime(2026, 5, 12);

        var service = CreateService(role);
        var result = await service.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = returnDate,
            Reason = "Barang cacat pabrik",
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 2 },
                new() { ProductId = productBId, Quantity = 1 },
            }
        });

        Assert.True(result.IsSuccess, result.Error);
        var ret = result.Value!;
        Assert.Equal("SRT-2026-0001", ret.ReturnNumber);
        Assert.Equal(returnDate, ret.ReturnDate);
        Assert.Equal(saleId, ret.SaleId);
        Assert.Equal("INV-2026-0001", ret.InvoiceNumber);
        Assert.Equal("Bengkel Jaya", ret.CustomerName);
        Assert.Equal("Kasir Uji", ret.UserName);
        Assert.Equal("Barang cacat pabrik", ret.Reason);
        Assert.Equal(1_750_000m, ret.TotalAmount); // (2 * 750.000) + (1 * 250.000)
        Assert.Equal(2, ret.Details.Count);

        var detailA = ret.Details.Single(d => d.ProductId == productAId);
        Assert.Equal(2, detailA.Quantity);
        Assert.Equal(750_000m, detailA.UnitPrice);
        Assert.Equal(1_500_000m, detailA.Subtotal);

        var detailB = ret.Details.Single(d => d.ProductId == productBId);
        Assert.Equal(1, detailB.Quantity);
        Assert.Equal(250_000m, detailB.UnitPrice);
        Assert.Equal(250_000m, detailB.Subtotal);

        // Verifikasi mutasi fisik stok bertambah
        await using var context = _db.CreateContext();
        var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
        var prodB = await context.Products.SingleAsync(p => p.Id == productBId);
        Assert.Equal(12, prodA.Stock); // 10 + 2
        Assert.Equal(6, prodB.Stock);  // 5 + 1

        // Verifikasi mutasi kartu stok ReturnIn tercatat lengkap
        var stockCards = await context.StockCards.OrderBy(c => c.ProductId).ToListAsync();
        Assert.Equal(2, stockCards.Count);

        Assert.Equal(productAId, stockCards[0].ProductId);
        Assert.Equal(StockMovementType.ReturnIn, stockCards[0].MovementType);
        Assert.Equal(2, stockCards[0].Quantity);
        Assert.Equal(10, stockCards[0].StockBefore);
        Assert.Equal(12, stockCards[0].StockAfter);
        Assert.Equal("SRT-2026-0001", stockCards[0].Reference);
        Assert.Equal(returnDate, stockCards[0].MovementDate);

        Assert.Equal(productBId, stockCards[1].ProductId);
        Assert.Equal(StockMovementType.ReturnIn, stockCards[1].MovementType);
        Assert.Equal(1, stockCards[1].Quantity);
        Assert.Equal(5, stockCards[1].StockBefore);
        Assert.Equal(6, stockCards[1].StockAfter);
        Assert.Equal("SRT-2026-0001", stockCards[1].Reference);
        Assert.Equal(returnDate, stockCards[1].MovementDate);

        // Verifikasi data penjualan asal sama sekali TIDAK berubah
        var originalSale = await context.Sales.SingleAsync(s => s.Id == saleId);
        Assert.Equal(4_250_000m, originalSale.TotalAmount); // (5*750.000) + (2*250.000)
        Assert.Equal(4_250_000m, originalSale.PaidAmount);
        Assert.Equal(0m, originalSale.RemainingAmount);
        Assert.Equal(PaymentStatus.Paid, originalSale.PaymentStatus);

        // ATURAN INTEGRITAS UTAMA: TIDAK BOLEH ADA JURNAL UMUM OTOMATIS
        Assert.False(await context.JournalEntries.AnyAsync());
    }

    [Fact]
    public async Task Create_Viewer_Ditolak()
    {
        var (_, productAId, _, saleId) = await SeedFixtureAsync();
        var viewerService = CreateService(Roles.Viewer);

        var result = await viewerService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 12),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });

        Assert.True(result.IsFailure);
        Assert.Equal(WriteDenied, result.Error);

        await using var context = _db.CreateContext();
        Assert.False(await context.SaleReturns.AnyAsync());
        Assert.False(await context.StockCards.AnyAsync());
    }

    [Fact]
    public async Task Create_ReturnBertahap_HinggaBatasMaksimal_Berhasil()
    {
        var (_, productAId, _, saleId) = await SeedFixtureAsync(stockA: 10, soldA: 5);
        var staffService = CreateService(Roles.Staff);

        // Retur tahap 1: 2 unit
        var result1 = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 11),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 2 }
            }
        });
        Assert.True(result1.IsSuccess, result1.Error);
        Assert.Equal("SRT-2026-0001", result1.Value!.ReturnNumber);

        await using (var context = _db.CreateContext())
        {
            var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
            Assert.Equal(12, prodA.Stock);
        }

        // Retur tahap 2: 3 unit (mencapai batas total 5 unit yang terjual)
        var result2 = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 12),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 3 }
            }
        });
        Assert.True(result2.IsSuccess, result2.Error);
        Assert.Equal("SRT-2026-0002", result2.Value!.ReturnNumber);

        await using (var context = _db.CreateContext())
        {
            var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
            Assert.Equal(15, prodA.Stock);
        }

        // Retur tahap 3: coba retur 1 unit lagi melebihi total yang dapat diretur -> Ditolak
        var result3 = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 13),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });
        Assert.True(result3.IsFailure);
        Assert.Contains("melebihi sisa yang dapat diretur", result3.Error!, StringComparison.OrdinalIgnoreCase);

        // Stok tetap 15
        await using (var context = _db.CreateContext())
        {
            var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
            Assert.Equal(15, prodA.Stock);
        }
    }

    [Fact]
    public async Task Create_JumlahMelebihiSisaYangDapatDiretur_Ditolak()
    {
        var (_, productAId, _, saleId) = await SeedFixtureAsync(stockA: 10, soldA: 5);
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 11),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 6 }
            }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("melebihi sisa yang dapat diretur", result.Error!, StringComparison.OrdinalIgnoreCase);

        await using var context = _db.CreateContext();
        var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
        Assert.Equal(10, prodA.Stock);
        Assert.False(await context.SaleReturns.AnyAsync());
        Assert.False(await context.StockCards.AnyAsync());
    }

    [Fact]
    public async Task Create_ProdukTidakAdaDalamPenjualanAsal_Ditolak()
    {
        var (_, productAId, _, saleId) = await SeedFixtureAsync();

        // Tambah produk ketiga yang tidak ada di nota penjualan
        int productCId;
        await using (var context = _db.CreateContext())
        {
            var prodC = new Product
            {
                Name = "Incoe NS60",
                SKU = "INC-NS60",
                SellingPrice = 800_000m,
                PurchasePrice = 650_000m,
                Stock = 8
            };
            context.Products.Add(prodC);
            await context.SaveChangesAsync();
            productCId = prodC.Id;
        }

        var staffService = CreateService(Roles.Staff);
        var result = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 11),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productCId, Quantity = 1 }
            }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("tidak terdapat dalam penjualan asal", result.Error!, StringComparison.OrdinalIgnoreCase);

        await using var verifyContext = _db.CreateContext();
        Assert.False(await verifyContext.SaleReturns.AnyAsync());
    }

    [Fact]
    public async Task Create_ProdukDuplikatDalamSatuRetur_Ditolak()
    {
        var (_, productAId, _, saleId) = await SeedFixtureAsync(soldA: 5);
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 11),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 },
                new() { ProductId = productAId, Quantity = 2 },
            }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("tidak boleh muncul lebih dari satu kali", result.Error!, StringComparison.OrdinalIgnoreCase);

        await using var context = _db.CreateContext();
        Assert.False(await context.SaleReturns.AnyAsync());
    }

    [Fact]
    public async Task Create_TanggalReturSebelumTanggalPenjualan_Ditolak()
    {
        var (_, productAId, _, saleId) = await SeedFixtureAsync(saleDate: new DateTime(2026, 5, 10));
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 9),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("sebelum tanggal penjualan", result.Error!, StringComparison.OrdinalIgnoreCase);

        await using var context = _db.CreateContext();
        Assert.False(await context.SaleReturns.AnyAsync());
    }

    [Fact]
    public async Task Create_TanggalReturSamaDenganTanggalPenjualan_Berhasil()
    {
        var sameDate = new DateTime(2026, 5, 10);
        var (_, productAId, _, saleId) = await SeedFixtureAsync(saleDate: sameDate);
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = sameDate,
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("SRT-2026-0001", result.Value!.ReturnNumber);
    }

    [Fact]
    public async Task Create_SaleTidakDitemukan_Ditolak()
    {
        var (_, productAId, _, _) = await SeedFixtureAsync();
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = 99999,
            ReturnDate = new DateTime(2026, 5, 12),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("Penjualan tidak ditemukan", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_TanpaDetail_Ditolak()
    {
        var (_, _, _, saleId) = await SeedFixtureAsync();
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 12),
            Details = new List<CreateSaleReturnDetailDto>()
        });

        Assert.True(result.IsFailure);
        Assert.Contains("minimal memiliki 1 item", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAll_FilterTanggalDanSortingNewestFirst_BerfungsiUntukViewer()
    {
        var (_, productAId, _, saleId) = await SeedFixtureAsync(soldA: 5);
        var staffService = CreateService(Roles.Staff);

        // Buat 2 retur pada tanggal berbeda
        var res1 = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 11),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });
        Assert.True(res1.IsSuccess);

        var res2 = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 15),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 2 }
            }
        });
        Assert.True(res2.IsSuccess);

        var viewerService = CreateService(Roles.Viewer);

        // 1. Tanpa filter: harus mengembalikan semua (2 item), urut tanggal terbaru
        var allResult = await viewerService.GetAllAsync();
        Assert.True(allResult.IsSuccess);
        Assert.Equal(2, allResult.Value!.Count);
        Assert.Equal("SRT-2026-0002", allResult.Value[0].ReturnNumber);
        Assert.Equal("SRT-2026-0001", allResult.Value[1].ReturnNumber);
        Assert.Equal("INV-2026-0001", allResult.Value[0].InvoiceNumber);
        Assert.Equal("Bengkel Jaya", allResult.Value[0].CustomerName);
        Assert.Equal("Kasir Uji", allResult.Value[0].UserName);

        // 2. Filter rentang tanggal yang mencakup hanya retur kedua
        var filterResult = await viewerService.GetAllAsync(
            dateFrom: new DateTime(2026, 5, 12),
            dateTo: new DateTime(2026, 5, 16));
        Assert.True(filterResult.IsSuccess);
        var singleItem = Assert.Single(filterResult.Value!);
        Assert.Equal("SRT-2026-0002", singleItem.ReturnNumber);

        // 3. Filter rentang tanggal di mana tidak ada retur
        var emptyResult = await viewerService.GetAllAsync(
            dateFrom: new DateTime(2026, 5, 1),
            dateTo: new DateTime(2026, 5, 5));
        Assert.True(emptyResult.IsSuccess);
        Assert.Empty(emptyResult.Value!);
    }

    [Fact]
    public async Task GetById_DitemukanDanTermasukDetails_SertaDitolakBilaTidakAda()
    {
        var (_, productAId, productBId, saleId) = await SeedFixtureAsync();
        var staffService = CreateService(Roles.Staff);

        var createResult = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 12),
            Reason = "Retur sebagian",
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 2 },
                new() { ProductId = productBId, Quantity = 1 },
            }
        });
        Assert.True(createResult.IsSuccess);
        var returnId = createResult.Value!.Id;

        var viewerService = CreateService(Roles.Viewer);

        // Id valid
        var getResult = await viewerService.GetByIdAsync(returnId);
        Assert.True(getResult.IsSuccess);
        var ret = getResult.Value!;
        Assert.Equal("SRT-2026-0001", ret.ReturnNumber);
        Assert.Equal("INV-2026-0001", ret.InvoiceNumber);
        Assert.Equal("Bengkel Jaya", ret.CustomerName);
        Assert.Equal("Kasir Uji", ret.UserName);
        Assert.Equal(2, ret.Details.Count);
        Assert.Equal("GS Astra NS40ZL", ret.Details[0].ProductName);
        Assert.Equal("Yuasa YTZ5S", ret.Details[1].ProductName);

        // Id tidak ada
        var notFoundResult = await viewerService.GetByIdAsync(99999);
        Assert.True(notFoundResult.IsFailure);
        Assert.Contains("tidak ditemukan", notFoundResult.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetBySale_MengembalikanSemuaReturUntukSaleTertentu()
    {
        var (_, productAId, _, sale1Id) = await SeedFixtureAsync(soldA: 5);

        // Buat sale kedua tanpa retur
        int sale2Id;
        await using (var context = _db.CreateContext())
        {
            var customer = await context.Customers.FirstAsync();
            var sale2 = new Sale
            {
                InvoiceNumber = "INV-2026-0002",
                SaleDate = new DateTime(2026, 5, 15),
                CustomerId = customer.Id,
                UserId = "user-test",
                PaymentMethod = PaymentMethod.Cash,
                TotalAmount = 750_000m,
                PaidAmount = 750_000m,
                RemainingAmount = 0m,
                PaymentStatus = PaymentStatus.Paid,
                Details = new List<SaleDetail>
                {
                    new() { ProductId = productAId, Quantity = 1, UnitPrice = 750_000m, Subtotal = 750_000m }
                }
            };
            context.Sales.Add(sale2);
            await context.SaveChangesAsync();
            sale2Id = sale2.Id;
        }

        var staffService = CreateService(Roles.Staff);
        await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = sale1Id,
            ReturnDate = new DateTime(2026, 5, 12),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });

        var viewerService = CreateService(Roles.Viewer);

        // Sale 1 memiliki 1 retur
        var bySale1 = await viewerService.GetBySaleAsync(sale1Id);
        Assert.True(bySale1.IsSuccess);
        Assert.Single(bySale1.Value!);
        Assert.Equal("SRT-2026-0001", bySale1.Value![0].ReturnNumber);

        // Sale 2 belum memiliki retur
        var bySale2 = await viewerService.GetBySaleAsync(sale2Id);
        Assert.True(bySale2.IsSuccess);
        Assert.Empty(bySale2.Value!);
    }

    [Fact]
    public async Task ReadOperations_TanpaHakAkses_Ditolak()
    {
        var anonymousService = CreateService(); // Tanpa role

        var getAllResult = await anonymousService.GetAllAsync();
        Assert.True(getAllResult.IsFailure);
        Assert.Equal(ReadDenied, getAllResult.Error);

        var getByIdResult = await anonymousService.GetByIdAsync(1);
        Assert.True(getByIdResult.IsFailure);
        Assert.Equal(ReadDenied, getByIdResult.Error);

        var getBySaleResult = await anonymousService.GetBySaleAsync(1);
        Assert.True(getBySaleResult.IsFailure);
        Assert.Equal(ReadDenied, getBySaleResult.Error);
    }

    [Fact]
    public async Task Create_TanpaHakAkses_Ditolak()
    {
        var (_, productAId, _, saleId) = await SeedFixtureAsync();
        var anonymousService = CreateService(); // Tanpa role / unauthenticated

        var result = await anonymousService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 12),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });

        Assert.True(result.IsFailure);
        Assert.Equal(WriteDenied, result.Error);

        await using var context = _db.CreateContext();
        Assert.False(await context.SaleReturns.AnyAsync());
        Assert.False(await context.StockCards.AnyAsync());
    }

    [Fact]
    public async Task Create_MultiItem_SalahSatuMelebihiBatas_AtomicRollback_TanpaEfekSamping()
    {
        var (_, productAId, productBId, saleId) = await SeedFixtureAsync(
            stockA: 10, stockB: 5, soldA: 5, soldB: 2);
        var staffService = CreateService(Roles.Staff);

        // Product A: 2 valid (dari 5 terjual), Product B: 3 invalid (melebihi 2 terjual)
        var result = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 12),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 2 },
                new() { ProductId = productBId, Quantity = 3 },
            }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("melebihi sisa yang dapat diretur", result.Error!, StringComparison.OrdinalIgnoreCase);

        // Verifikasi atomisitas: Tidak ada perubahan stok sama sekali
        await using var context = _db.CreateContext();
        var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
        var prodB = await context.Products.SingleAsync(p => p.Id == productBId);
        Assert.Equal(10, prodA.Stock);
        Assert.Equal(5, prodB.Stock);
        Assert.False(await context.SaleReturns.AnyAsync());
        Assert.False(await context.StockCards.AnyAsync());
    }

    [Fact]
    public async Task Create_TanggalReturSamaHariJamLebihAwal_Berhasil()
    {
        var saleDate = new DateTime(2026, 5, 10, 16, 30, 0);
        var returnDate = new DateTime(2026, 5, 10, 8, 15, 0); // Jam lebih awal pada hari yang sama
        var (_, productAId, _, saleId) = await SeedFixtureAsync(saleDate: saleDate);
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = returnDate,
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("SRT-2026-0001", result.Value!.ReturnNumber);
        Assert.Equal(returnDate, result.Value.ReturnDate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5)]
    public async Task Create_JumlahNolAtauNegatif_Ditolak(int invalidQty)
    {
        var (_, productAId, _, saleId) = await SeedFixtureAsync();
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 12),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = invalidQty }
            }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("Jumlah retur minimal 1", result.Error!);

        await using var context = _db.CreateContext();
        Assert.False(await context.SaleReturns.AnyAsync());
        Assert.False(await context.StockCards.AnyAsync());
    }

    [Fact]
    public async Task Create_InterleavedGradualReturns_MultipleProducts_AccurateRemainingTracked()
    {
        var (_, productAId, productBId, saleId) = await SeedFixtureAsync(
            stockA: 20, stockB: 20, soldA: 10, soldB: 8);
        var staffService = CreateService(Roles.Staff);

        // Tahap 1: Retur A=4, B=3 -> Berhasil
        var res1 = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 11),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 4 },
                new() { ProductId = productBId, Quantity = 3 },
            }
        });
        Assert.True(res1.IsSuccess, res1.Error);
        Assert.Equal("SRT-2026-0001", res1.Value!.ReturnNumber);

        // Tahap 2: Retur A=4, B=5 -> Berhasil (B habis 3+5=8, A sisa 10-8=2)
        var res2 = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 12),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 4 },
                new() { ProductId = productBId, Quantity = 5 },
            }
        });
        Assert.True(res2.IsSuccess, res2.Error);
        Assert.Equal("SRT-2026-0002", res2.Value!.ReturnNumber);

        // Tahap 3: Coba retur A=2, B=1 -> Ditolak karena B sudah habis
        var res3 = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 13),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 2 },
                new() { ProductId = productBId, Quantity = 1 },
            }
        });
        Assert.True(res3.IsFailure);
        Assert.Contains($"produk ID {productBId} melebihi sisa", res3.Error!, StringComparison.OrdinalIgnoreCase);

        // Tahap 4: Retur sisa A=2 saja -> Berhasil (A sekarang habis 4+4+2=10)
        var res4 = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 14),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 2 },
            }
        });
        Assert.True(res4.IsSuccess, res4.Error);
        Assert.Equal("SRT-2026-0003", res4.Value!.ReturnNumber);

        // Tahap 5: Retur A=1 -> Ditolak karena A sudah habis
        var res5 = await staffService.CreateAsync(new CreateSaleReturnDto
        {
            SaleId = saleId,
            ReturnDate = new DateTime(2026, 5, 15),
            Details = new List<CreateSaleReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 },
            }
        });
        Assert.True(res5.IsFailure);
        Assert.Contains($"produk ID {productAId} melebihi sisa", res5.Error!, StringComparison.OrdinalIgnoreCase);

        // Verifikasi kondisi akhir stok & mutasi
        await using var context = _db.CreateContext();
        var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
        var prodB = await context.Products.SingleAsync(p => p.Id == productBId);
        Assert.Equal(30, prodA.Stock); // 20 + 4 + 4 + 2 = 30
        Assert.Equal(28, prodB.Stock); // 20 + 3 + 5 = 28

        var returns = await context.SaleReturns.ToListAsync();
        Assert.Equal(3, returns.Count);

        var stockCards = await context.StockCards.ToListAsync();
        Assert.Equal(5, stockCards.Count); // res1 (2) + res2 (2) + res4 (1) = 5
    }

    internal static async Task Main(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("STARTING EMPIRICAL SALE RETURN SERVICE TEST SUITE");
        Console.WriteLine("=================================================");

        var testMethods = new (string Name, Func<SaleReturnServiceTests, Task> Action)[]
        {
            ("Create_Admin_Success", t => t.Create_AdminDanStaff_Berhasil_GenerateNomorSRT_HargaDariDetail_StokBertambah_StockCardReturnIn_OriginalSaleTidakBerubah_TanpaJurnal(Roles.Admin)),
            ("Create_Staff_Success", t => t.Create_AdminDanStaff_Berhasil_GenerateNomorSRT_HargaDariDetail_StokBertambah_StockCardReturnIn_OriginalSaleTidakBerubah_TanpaJurnal(Roles.Staff)),
            ("Create_Viewer_Denied", t => t.Create_Viewer_Ditolak()),
            ("Create_Anonymous_Denied", t => t.Create_TanpaHakAkses_Ditolak()),
            ("Create_GradualReturns_UpToMax_Success", t => t.Create_ReturnBertahap_HinggaBatasMaksimal_Berhasil()),
            ("Create_InterleavedGradualReturns_MultiProduct_Success", t => t.Create_InterleavedGradualReturns_MultipleProducts_AccurateRemainingTracked()),
            ("Create_ExceedsRemaining_Denied", t => t.Create_JumlahMelebihiSisaYangDapatDiretur_Ditolak()),
            ("Create_MultiItem_PartialExceed_AtomicRollback", t => t.Create_MultiItem_SalahSatuMelebihiBatas_AtomicRollback_TanpaEfekSamping()),
            ("Create_ProductNotInSale_Denied", t => t.Create_ProdukTidakAdaDalamPenjualanAsal_Ditolak()),
            ("Create_DuplicateProductInReturn_Denied", t => t.Create_ProdukDuplikatDalamSatuRetur_Ditolak()),
            ("Create_DateBeforeSale_Denied", t => t.Create_TanggalReturSebelumTanggalPenjualan_Ditolak()),
            ("Create_DateSameAsSale_Success", t => t.Create_TanggalReturSamaDenganTanggalPenjualan_Berhasil()),
            ("Create_DateSameDayDifferentTime_Success", t => t.Create_TanggalReturSamaHariJamLebihAwal_Berhasil()),
            ("Create_ZeroQuantity_Denied", t => t.Create_JumlahNolAtauNegatif_Ditolak(0)),
            ("Create_NegativeQuantity_Denied", t => t.Create_JumlahNolAtauNegatif_Ditolak(-1)),
            ("Create_SaleNotFound_Denied", t => t.Create_SaleTidakDitemukan_Ditolak()),
            ("Create_NoDetails_Denied", t => t.Create_TanpaDetail_Ditolak()),
            ("GetAll_FilterDateAndSorting_Success", t => t.GetAll_FilterTanggalDanSortingNewestFirst_BerfungsiUntukViewer()),
            ("GetById_ValidAndInvalid_Success", t => t.GetById_DitemukanDanTermasukDetails_SertaDitolakBilaTidakAda()),
            ("GetBySale_IsolatedBySaleId_Success", t => t.GetBySale_MengembalikanSemuaReturUntukSaleTertentu()),
            ("ReadOperations_Anonymous_Denied", t => t.ReadOperations_TanpaHakAkses_Ditolak()),
        };

        int passed = 0;
        int failed = 0;

        foreach (var (name, action) in testMethods)
        {
            try
            {
                using var fixture = new SaleReturnServiceTests();
                await action(fixture);
                Console.WriteLine($"[PASS] {name}");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] {name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                failed++;
            }
        }

        Console.WriteLine("=================================================");
        Console.WriteLine($"SUMMARY: Total={testMethods.Length}, Passed={passed}, Failed={failed}");
        Console.WriteLine("=================================================");

        if (failed > 0)
        {
            Environment.Exit(1);
        }
    }
}
