using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.Purchasing;
using SIA.Domain.Entities.Inventory;
using SIA.Domain.Entities.Purchasing;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data.Seed;
using SIA.Infrastructure.Identity;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;

namespace SIA.Tests.Services;

public class PurchaseReturnServiceTests : IDisposable
{
    private const string WriteDenied = "Anda tidak memiliki hak akses untuk mengubah data ini.";
    private const string ReadDenied = "Anda tidak memiliki hak akses untuk melihat data ini.";
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private PurchaseReturnService CreateService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);
        var numbers = new TransactionNumberService(context, user);
        return new PurchaseReturnService(context, user, numbers);
    }

    private async Task<(int supplierId, int productAId, int productBId, int poId)> SeedFixtureAsync(
        int stockA = 10,
        int stockB = 5,
        int orderedA = 5,
        int orderedB = 2,
        decimal priceA = 500_000m,
        decimal priceB = 180_000m,
        PurchaseOrderStatus poStatus = PurchaseOrderStatus.Received,
        DateTime? orderDate = null)
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
                FullName = "Staf Pengadaan",
            });
        }

        var supplier = new Supplier { Name = "PT Distributor Aki", Phone = "0812345678" };
        var prodA = new Product
        {
            Name = "GS Astra NS40",
            SKU = "GS-NS40",
            PurchasePrice = priceA,
            SellingPrice = 650_000m,
            Stock = stockA,
        };
        var prodB = new Product
        {
            Name = "Yuasa YTZ5S",
            SKU = "YUA-YTZ5S",
            PurchasePrice = priceB,
            SellingPrice = 240_000m,
            Stock = stockB,
        };

        context.Suppliers.Add(supplier);
        context.Products.AddRange(prodA, prodB);
        await context.SaveChangesAsync();

        var date = orderDate ?? new DateTime(2026, 5, 10);
        var total = (orderedA * priceA) + (orderedB * priceB);
        var po = new PurchaseOrder
        {
            PONumber = "PO-2026-0001",
            OrderDate = date,
            SupplierId = supplier.Id,
            UserId = "user-test",
            TotalAmount = total,
            PaidAmount = 0m,
            RemainingAmount = total,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = poStatus,
            ApprovedByUserId = poStatus != PurchaseOrderStatus.Draft ? "user-test" : null,
            Notes = "Pengadaan aki",
            Details = new List<PurchaseOrderDetail>
            {
                new() { ProductId = prodA.Id, Quantity = orderedA, UnitPrice = priceA, Subtotal = orderedA * priceA },
                new() { ProductId = prodB.Id, Quantity = orderedB, UnitPrice = priceB, Subtotal = orderedB * priceB },
            }
        };

        context.PurchaseOrders.Add(po);
        await context.SaveChangesAsync();

        return (supplier.Id, prodA.Id, prodB.Id, po.Id);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    public async Task Create_AdminDanStaff_PadaPOReceived_Berhasil_GenerateNomorPRT_HargaDariDetail_StokBerkurang_StockCardReturnOut_OriginalPOTidakBerubah_TanpaJurnal(string role)
    {
        var (supplierId, productAId, productBId, poId) = await SeedFixtureAsync(
            stockA: 10, stockB: 5, orderedA: 5, orderedB: 2, priceA: 500_000m, priceB: 180_000m);
        var returnDate = new DateTime(2026, 5, 12);

        var service = CreateService(role);
        var result = await service.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = returnDate,
            Reason = "Barang tidak sesuai spesifikasi",
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 2 },
                new() { ProductId = productBId, Quantity = 1 },
            }
        });

        Assert.True(result.IsSuccess, result.Error);
        var ret = result.Value!;
        Assert.Equal("PRT-2026-0001", ret.ReturnNumber);
        Assert.Equal(returnDate, ret.ReturnDate);
        Assert.Equal(poId, ret.PurchaseOrderId);
        Assert.Equal("PO-2026-0001", ret.PONumber);
        Assert.Equal("PT Distributor Aki", ret.SupplierName);
        Assert.Equal("Staf Pengadaan", ret.UserName);
        Assert.Equal("Barang tidak sesuai spesifikasi", ret.Reason);
        Assert.Equal(1_180_000m, ret.TotalAmount); // (2 * 500.000) + (1 * 180.000)
        Assert.Equal(2, ret.Details.Count);

        var detailA = ret.Details.Single(d => d.ProductId == productAId);
        Assert.Equal(2, detailA.Quantity);
        Assert.Equal(500_000m, detailA.UnitPrice);
        Assert.Equal(1_000_000m, detailA.Subtotal);

        var detailB = ret.Details.Single(d => d.ProductId == productBId);
        Assert.Equal(1, detailB.Quantity);
        Assert.Equal(180_000m, detailB.UnitPrice);
        Assert.Equal(180_000m, detailB.Subtotal);

        // Verifikasi mutasi fisik stok di gudang berkurang
        await using var context = _db.CreateContext();
        var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
        var prodB = await context.Products.SingleAsync(p => p.Id == productBId);
        Assert.Equal(8, prodA.Stock); // 10 - 2
        Assert.Equal(4, prodB.Stock); // 5 - 1

        // Verifikasi mutasi kartu stok ReturnOut tercatat lengkap
        var stockCards = await context.StockCards.OrderBy(c => c.ProductId).ToListAsync();
        Assert.Equal(2, stockCards.Count);

        Assert.Equal(productAId, stockCards[0].ProductId);
        Assert.Equal(StockMovementType.ReturnOut, stockCards[0].MovementType);
        Assert.Equal(2, stockCards[0].Quantity);
        Assert.Equal(10, stockCards[0].StockBefore);
        Assert.Equal(8, stockCards[0].StockAfter);
        Assert.Equal("PRT-2026-0001", stockCards[0].Reference);
        Assert.Equal(returnDate, stockCards[0].MovementDate);

        Assert.Equal(productBId, stockCards[1].ProductId);
        Assert.Equal(StockMovementType.ReturnOut, stockCards[1].MovementType);
        Assert.Equal(1, stockCards[1].Quantity);
        Assert.Equal(5, stockCards[1].StockBefore);
        Assert.Equal(4, stockCards[1].StockAfter);
        Assert.Equal("PRT-2026-0001", stockCards[1].Reference);
        Assert.Equal(returnDate, stockCards[1].MovementDate);

        // Verifikasi data Purchase Order asal sama sekali TIDAK berubah
        var originalPo = await context.PurchaseOrders.SingleAsync(p => p.Id == poId);
        Assert.Equal(PurchaseOrderStatus.Received, originalPo.Status);
        Assert.Equal(2_860_000m, originalPo.TotalAmount); // (5*500.000) + (2*180.000)
        Assert.Equal(0m, originalPo.PaidAmount);
        Assert.Equal(2_860_000m, originalPo.RemainingAmount);
        Assert.Equal(PaymentStatus.Unpaid, originalPo.PaymentStatus);

        // ATURAN INTEGRITAS UTAMA: TIDAK BOLEH ADA JURNAL UMUM OTOMATIS
        Assert.False(await context.JournalEntries.AnyAsync());
    }

    [Fact]
    public async Task Create_Viewer_Ditolak()
    {
        var (_, productAId, _, poId) = await SeedFixtureAsync();
        var viewerService = CreateService(Roles.Viewer);

        var result = await viewerService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = new DateTime(2026, 5, 12),
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });

        Assert.True(result.IsFailure);
        Assert.Equal(WriteDenied, result.Error);

        await using var context = _db.CreateContext();
        Assert.False(await context.PurchaseReturns.AnyAsync());
        Assert.False(await context.StockCards.AnyAsync());
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Approved)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public async Task Create_POBukanStatusReceived_Ditolak(PurchaseOrderStatus nonReceivedStatus)
    {
        var (_, productAId, _, poId) = await SeedFixtureAsync(stockA: 10, poStatus: nonReceivedStatus);
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = new DateTime(2026, 5, 12),
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("sudah diterima", result.Error!, StringComparison.OrdinalIgnoreCase);

        // Stok tidak berkurang dan tidak ada retur
        await using var context = _db.CreateContext();
        var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
        Assert.Equal(10, prodA.Stock);
        Assert.False(await context.PurchaseReturns.AnyAsync());
        Assert.False(await context.StockCards.AnyAsync());
    }

    [Fact]
    public async Task Create_ReturnBertahap_HinggaBatasMaksimal_Berhasil()
    {
        var (_, productAId, _, poId) = await SeedFixtureAsync(stockA: 10, orderedA: 5);
        var staffService = CreateService(Roles.Staff);

        // Retur tahap 1: 2 unit
        var result1 = await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = new DateTime(2026, 5, 11),
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 2 }
            }
        });
        Assert.True(result1.IsSuccess, result1.Error);
        Assert.Equal("PRT-2026-0001", result1.Value!.ReturnNumber);

        await using (var context = _db.CreateContext())
        {
            var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
            Assert.Equal(8, prodA.Stock); // 10 - 2
        }

        // Retur tahap 2: 3 unit (mencapai batas total 5 unit yang dipesan)
        var result2 = await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = new DateTime(2026, 5, 12),
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 3 }
            }
        });
        Assert.True(result2.IsSuccess, result2.Error);
        Assert.Equal("PRT-2026-0002", result2.Value!.ReturnNumber);

        await using (var context = _db.CreateContext())
        {
            var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
            Assert.Equal(5, prodA.Stock); // 8 - 3
        }

        // Retur tahap 3: coba retur 1 unit lagi melebihi total yang dapat diretur -> Ditolak
        var result3 = await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = new DateTime(2026, 5, 13),
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });
        Assert.True(result3.IsFailure);
        Assert.Contains("melebihi sisa yang dapat diretur", result3.Error!, StringComparison.OrdinalIgnoreCase);

        // Stok tetap 5
        await using (var context = _db.CreateContext())
        {
            var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
            Assert.Equal(5, prodA.Stock);
        }
    }

    [Fact]
    public async Task Create_JumlahMelebihiSisaYangDapatDiretur_Ditolak()
    {
        var (_, productAId, _, poId) = await SeedFixtureAsync(stockA: 10, orderedA: 5);
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = new DateTime(2026, 5, 11),
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 6 }
            }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("melebihi sisa yang dapat diretur", result.Error!, StringComparison.OrdinalIgnoreCase);

        await using var context = _db.CreateContext();
        var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
        Assert.Equal(10, prodA.Stock);
        Assert.False(await context.PurchaseReturns.AnyAsync());
        Assert.False(await context.StockCards.AnyAsync());
    }

    [Fact]
    public async Task Create_StokGudangTidakCukup_Ditolak_StokTidakBerubah()
    {
        // PO menerima 5 unit, tetapi stok fisik di gudang tersisa hanya 1 (misal 4 sudah terjual)
        var (_, productAId, _, poId) = await SeedFixtureAsync(stockA: 1, orderedA: 5);
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = new DateTime(2026, 5, 11),
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 2 }
            }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("tidak cukup di gudang untuk diretur", result.Error!, StringComparison.OrdinalIgnoreCase);

        await using var context = _db.CreateContext();
        var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
        Assert.Equal(1, prodA.Stock);
        Assert.False(await context.PurchaseReturns.AnyAsync());
        Assert.False(await context.StockCards.AnyAsync());
    }

    [Fact]
    public async Task Create_ProdukTidakAdaDalamPODetail_Ditolak()
    {
        var (_, productAId, _, poId) = await SeedFixtureAsync();

        // Tambah produk ketiga yang tidak ada di PO
        int productCId;
        await using (var context = _db.CreateContext())
        {
            var prodC = new Product
            {
                Name = "Incoe NS60",
                SKU = "INC-NS60",
                PurchasePrice = 600_000m,
                SellingPrice = 750_000m,
                Stock = 8
            };
            context.Products.Add(prodC);
            await context.SaveChangesAsync();
            productCId = prodC.Id;
        }

        var staffService = CreateService(Roles.Staff);
        var result = await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = new DateTime(2026, 5, 11),
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productCId, Quantity = 1 }
            }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("tidak terdapat dalam Purchase Order asal", result.Error!, StringComparison.OrdinalIgnoreCase);

        await using var verifyContext = _db.CreateContext();
        Assert.False(await verifyContext.PurchaseReturns.AnyAsync());
    }

    [Fact]
    public async Task Create_ProdukDuplikatDalamSatuRetur_Ditolak()
    {
        var (_, productAId, _, poId) = await SeedFixtureAsync(stockA: 10, orderedA: 5);
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = new DateTime(2026, 5, 11),
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 },
                new() { ProductId = productAId, Quantity = 2 },
            }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("tidak boleh muncul lebih dari satu kali", result.Error!, StringComparison.OrdinalIgnoreCase);

        await using var context = _db.CreateContext();
        Assert.False(await context.PurchaseReturns.AnyAsync());
    }

    [Fact]
    public async Task Create_TanggalReturSebelumTanggalOrder_Ditolak()
    {
        var (_, productAId, _, poId) = await SeedFixtureAsync(orderDate: new DateTime(2026, 5, 10));
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = new DateTime(2026, 5, 9),
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("sebelum tanggal order", result.Error!, StringComparison.OrdinalIgnoreCase);

        await using var context = _db.CreateContext();
        Assert.False(await context.PurchaseReturns.AnyAsync());
    }

    [Fact]
    public async Task Create_TanggalReturSamaDenganTanggalOrder_Berhasil()
    {
        var sameDate = new DateTime(2026, 5, 10);
        var (_, productAId, _, poId) = await SeedFixtureAsync(orderDate: sameDate);
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = sameDate,
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("PRT-2026-0001", result.Value!.ReturnNumber);
    }

    [Fact]
    public async Task Create_PurchaseOrderTidakDitemukan_Ditolak()
    {
        var (_, productAId, _, _) = await SeedFixtureAsync();
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = 99999,
            ReturnDate = new DateTime(2026, 5, 12),
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });

        Assert.True(result.IsFailure);
        Assert.Contains("Purchase Order tidak ditemukan", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_TanpaDetail_Ditolak()
    {
        var (_, _, _, poId) = await SeedFixtureAsync();
        var staffService = CreateService(Roles.Staff);

        var result = await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = new DateTime(2026, 5, 12),
            Details = new List<CreatePurchaseReturnDetailDto>()
        });

        Assert.True(result.IsFailure);
        Assert.Contains("minimal memiliki 1 item", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAll_FilterTanggalDanSortingNewestFirst_BerfungsiUntukViewer()
    {
        var (_, productAId, _, poId) = await SeedFixtureAsync(stockA: 10, orderedA: 5);
        var staffService = CreateService(Roles.Staff);

        // Buat 2 retur pada tanggal berbeda
        var res1 = await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = new DateTime(2026, 5, 11),
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });
        Assert.True(res1.IsSuccess);

        var res2 = await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = new DateTime(2026, 5, 15),
            Details = new List<CreatePurchaseReturnDetailDto>
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
        Assert.Equal("PRT-2026-0002", allResult.Value[0].ReturnNumber);
        Assert.Equal("PRT-2026-0001", allResult.Value[1].ReturnNumber);
        Assert.Equal("PO-2026-0001", allResult.Value[0].PONumber);
        Assert.Equal("PT Distributor Aki", allResult.Value[0].SupplierName);
        Assert.Equal("Staf Pengadaan", allResult.Value[0].UserName);

        // 2. Filter rentang tanggal yang mencakup hanya retur kedua
        var filterResult = await viewerService.GetAllAsync(
            dateFrom: new DateTime(2026, 5, 12),
            dateTo: new DateTime(2026, 5, 16));
        Assert.True(filterResult.IsSuccess);
        var singleItem = Assert.Single(filterResult.Value!);
        Assert.Equal("PRT-2026-0002", singleItem.ReturnNumber);

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
        var (_, productAId, productBId, poId) = await SeedFixtureAsync();
        var staffService = CreateService(Roles.Staff);

        var createResult = await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = poId,
            ReturnDate = new DateTime(2026, 5, 12),
            Reason = "Retur sebagian barang",
            Details = new List<CreatePurchaseReturnDetailDto>
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
        Assert.Equal("PRT-2026-0001", ret.ReturnNumber);
        Assert.Equal("PO-2026-0001", ret.PONumber);
        Assert.Equal("PT Distributor Aki", ret.SupplierName);
        Assert.Equal("Staf Pengadaan", ret.UserName);
        Assert.Equal(2, ret.Details.Count);
        Assert.Equal("GS Astra NS40", ret.Details[0].ProductName);
        Assert.Equal("Yuasa YTZ5S", ret.Details[1].ProductName);

        // Id tidak ada
        var notFoundResult = await viewerService.GetByIdAsync(99999);
        Assert.True(notFoundResult.IsFailure);
        Assert.Contains("tidak ditemukan", notFoundResult.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetByPurchaseOrder_MengembalikanSemuaReturUntukPOTertentu()
    {
        var (_, productAId, _, po1Id) = await SeedFixtureAsync(stockA: 10, orderedA: 5);

        // Buat PO kedua tanpa retur
        int po2Id;
        await using (var context = _db.CreateContext())
        {
            var supplier = await context.Suppliers.FirstAsync();
            var po2 = new PurchaseOrder
            {
                PONumber = "PO-2026-0002",
                OrderDate = new DateTime(2026, 5, 15),
                SupplierId = supplier.Id,
                UserId = "user-test",
                TotalAmount = 500_000m,
                PaidAmount = 0m,
                RemainingAmount = 500_000m,
                PaymentStatus = PaymentStatus.Unpaid,
                Status = PurchaseOrderStatus.Received,
                ApprovedByUserId = "user-test",
                Details = new List<PurchaseOrderDetail>
                {
                    new() { ProductId = productAId, Quantity = 1, UnitPrice = 500_000m, Subtotal = 500_000m }
                }
            };
            context.PurchaseOrders.Add(po2);
            await context.SaveChangesAsync();
            po2Id = po2.Id;
        }

        var staffService = CreateService(Roles.Staff);
        await staffService.CreateAsync(new CreatePurchaseReturnDto
        {
            PurchaseOrderId = po1Id,
            ReturnDate = new DateTime(2026, 5, 12),
            Details = new List<CreatePurchaseReturnDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1 }
            }
        });

        var viewerService = CreateService(Roles.Viewer);

        // PO 1 memiliki 1 retur
        var byPo1 = await viewerService.GetByPurchaseOrderAsync(po1Id);
        Assert.True(byPo1.IsSuccess);
        Assert.Single(byPo1.Value!);
        Assert.Equal("PRT-2026-0001", byPo1.Value![0].ReturnNumber);

        // PO 2 belum memiliki retur
        var byPo2 = await viewerService.GetByPurchaseOrderAsync(po2Id);
        Assert.True(byPo2.IsSuccess);
        Assert.Empty(byPo2.Value!);
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

        var getByPoResult = await anonymousService.GetByPurchaseOrderAsync(1);
        Assert.True(getByPoResult.IsFailure);
        Assert.Equal(ReadDenied, getByPoResult.Error);
    }
}
