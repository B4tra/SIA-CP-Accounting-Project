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

public class PurchaseOrderServiceTests : IDisposable
{
    private const string WriteDenied = "Anda tidak memiliki hak akses untuk mengubah data ini.";
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private PurchaseOrderService CreateService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);
        var numbers = new TransactionNumberService(context, user);
        return new PurchaseOrderService(context, user, numbers);
    }

    private async Task<(int supplierId, int productAId, int productBId)> SeedFixtureAsync(int stockA = 10, int stockB = 5)
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
            PurchasePrice = 500_000m,
            SellingPrice = 650_000m,
            Stock = stockA,
        };
        var prodB = new Product
        {
            Name = "Yuasa YTZ5S",
            SKU = "YUA-YTZ5S",
            PurchasePrice = 180_000m,
            SellingPrice = 240_000m,
            Stock = stockB,
        };

        context.Suppliers.Add(supplier);
        context.Products.AddRange(prodA, prodB);
        await context.SaveChangesAsync();

        return (supplier.Id, prodA.Id, prodB.Id);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    public async Task CreateDraft_AdminDanStaff_NomorPOGenerated_StokBelumBerubah_ViewerDitolak(string role)
    {
        var (supplierId, productAId, productBId) = await SeedFixtureAsync(stockA: 10, stockB: 5);
        var orderDate = new DateTime(2026, 5, 10);

        var service = CreateService(role);
        var result = await service.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplierId,
            OrderDate = orderDate,
            Notes = "Pesanan aki berkala",
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productAId, Quantity = 4, UnitPrice = 500_000m },
                new() { ProductId = productBId, Quantity = 2, UnitPrice = 180_000m },
            },
        });

        Assert.True(result.IsSuccess, result.Error);
        var po = result.Value!;
        Assert.Equal("PO-2026-0001", po.PONumber);
        Assert.Equal(PurchaseOrderStatus.Draft, po.Status);
        Assert.Equal(2_360_000m, po.TotalAmount);
        Assert.Equal(0m, po.PaidAmount);
        Assert.Equal(2_360_000m, po.RemainingAmount);
        Assert.Equal(PaymentStatus.Unpaid, po.PaymentStatus);
        Assert.Null(po.ApprovedByUserId);
        Assert.Equal(2, po.Details.Count);

        // Verifikasi stok fisik sama sekali belum bertambah
        await using var context = _db.CreateContext();
        var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
        var prodB = await context.Products.SingleAsync(p => p.Id == productBId);
        Assert.Equal(10, prodA.Stock);
        Assert.Equal(5, prodB.Stock);

        // Verifikasi tidak ada penulisan kartu stok dan tidak ada jurnal umum
        Assert.False(await context.StockCards.AnyAsync());
        Assert.False(await context.JournalEntries.AnyAsync());

        // Verifikasi role Viewer ditolak saat mencoba membuat draft
        var viewerService = CreateService(Roles.Viewer);
        var viewerResult = await viewerService.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplierId,
            OrderDate = orderDate,
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 500_000m },
            },
        });
        Assert.True(viewerResult.IsFailure);
        Assert.Equal(WriteDenied, viewerResult.Error);
    }

    [Fact]
    public async Task CreateDraft_ValidasiDueDate_DanDuplikasiProduk_Ditolak()
    {
        var (supplierId, productAId, _) = await SeedFixtureAsync();
        var service = CreateService(Roles.Staff);

        // 1. DueDate lebih awal dari OrderDate
        var invalidDueDateResult = await service.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 6, 10),
            DueDate = new DateTime(2026, 6, 9),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 500_000m },
            },
        });
        Assert.True(invalidDueDateResult.IsFailure);
        Assert.Contains("jatuh tempo", invalidDueDateResult.Error!, StringComparison.OrdinalIgnoreCase);

        // 2. Duplikasi produk dalam satu PO
        var duplicateProductResult = await service.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 6, 10),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 500_000m },
                new() { ProductId = productAId, Quantity = 2, UnitPrice = 500_000m },
            },
        });
        Assert.True(duplicateProductResult.IsFailure);
        Assert.Contains("tidak boleh muncul lebih dari satu kali", duplicateProductResult.Error!, StringComparison.OrdinalIgnoreCase);

        // 3. Pemasok tidak ada
        var invalidSupplierResult = await service.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = 99999,
            OrderDate = new DateTime(2026, 6, 10),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 500_000m },
            },
        });
        Assert.True(invalidSupplierResult.IsFailure);
        Assert.Contains("Pemasok", invalidSupplierResult.Error!);

        // 4. Produk tidak ada
        var invalidProductResult = await service.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 6, 10),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = 88888, Quantity = 1, UnitPrice = 500_000m },
            },
        });
        Assert.True(invalidProductResult.IsFailure);
        Assert.Contains("Produk", invalidProductResult.Error!);
    }

    [Fact]
    public async Task UpdateDraft_HanyaSaatDraft_DitolakBilaBukanDraftAtauViewer()
    {
        var (supplierId, productAId, productBId) = await SeedFixtureAsync();
        var staffService = CreateService(Roles.Staff);

        // Buat Draft awal
        var createResult = await staffService.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 6, 1),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productAId, Quantity = 2, UnitPrice = 500_000m },
            },
        });
        Assert.True(createResult.IsSuccess);
        var poId = createResult.Value!.Id;

        // Update saat masih Draft berhasil
        var updateResult = await staffService.UpdateDraftAsync(new UpdatePurchaseOrderDto
        {
            Id = poId,
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 6, 2),
            DueDate = new DateTime(2026, 6, 20),
            Notes = "Revisi item draft",
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productBId, Quantity = 5, UnitPrice = 180_000m },
            },
        });
        Assert.True(updateResult.IsSuccess, updateResult.Error);
        Assert.Equal(900_000m, updateResult.Value!.TotalAmount);
        Assert.Equal(900_000m, updateResult.Value.RemainingAmount);
        Assert.Single(updateResult.Value.Details);
        Assert.Equal(productBId, updateResult.Value.Details[0].ProductId);

        // Viewer ditolak saat update
        var viewerResult = await CreateService(Roles.Viewer).UpdateDraftAsync(new UpdatePurchaseOrderDto
        {
            Id = poId,
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 6, 2),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productBId, Quantity = 1, UnitPrice = 180_000m },
            },
        });
        Assert.True(viewerResult.IsFailure);

        // Approve PO oleh Admin
        var adminService = CreateService(Roles.Admin);
        var approveResult = await adminService.ApproveAsync(poId);
        Assert.True(approveResult.IsSuccess);

        // Update saat status sudah Approved ditolak
        var updateApprovedResult = await staffService.UpdateDraftAsync(new UpdatePurchaseOrderDto
        {
            Id = poId,
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 6, 2),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productBId, Quantity = 1, UnitPrice = 180_000m },
            },
        });
        Assert.True(updateApprovedResult.IsFailure);
        Assert.Contains("Draft", updateApprovedResult.Error!);
    }

    [Fact]
    public async Task Approve_HanyaAdmin_ApprovedByUserIdTerisi_StatusApproved_StokBelumBerubah_StaffDanViewerDitolak()
    {
        var (supplierId, productAId, _) = await SeedFixtureAsync(stockA: 10);
        var staffService = CreateService(Roles.Staff);

        var draftResult = await staffService.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 7, 1),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productAId, Quantity = 3, UnitPrice = 500_000m },
            },
        });
        Assert.True(draftResult.IsSuccess);
        var poId = draftResult.Value!.Id;

        // Staff mencoba approve -> ditolak
        var staffApprove = await staffService.ApproveAsync(poId);
        Assert.True(staffApprove.IsFailure);
        Assert.Equal(WriteDenied, staffApprove.Error);

        // Viewer mencoba approve -> ditolak
        var viewerApprove = await CreateService(Roles.Viewer).ApproveAsync(poId);
        Assert.True(viewerApprove.IsFailure);
        Assert.Equal(WriteDenied, viewerApprove.Error);

        // Admin approve -> berhasil
        var adminService = CreateService(Roles.Admin);
        var adminApprove = await adminService.ApproveAsync(poId);
        Assert.True(adminApprove.IsSuccess, adminApprove.Error);
        Assert.Equal(PurchaseOrderStatus.Approved, adminApprove.Value!.Status);
        Assert.Equal("user-test", adminApprove.Value.ApprovedByUserId);

        // Stok produk tetap tidak berubah
        await using var context = _db.CreateContext();
        var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
        Assert.Equal(10, prodA.Stock);
        Assert.False(await context.StockCards.AnyAsync());

        // Approve ulang pada PO yang sudah Approved ditolak
        var reApprove = await adminService.ApproveAsync(poId);
        Assert.True(reApprove.IsFailure);
        Assert.Contains("Draft", reApprove.Error!);
    }

    [Fact]
    public async Task Receive_HanyaDariApproved_StokBertambah_StockCardInTertulis_TanpaJurnalOtomatis_DanDariDraftDitolak()
    {
        var (supplierId, productAId, productBId) = await SeedFixtureAsync(stockA: 10, stockB: 5);
        var staffService = CreateService(Roles.Staff);
        var adminService = CreateService(Roles.Admin);

        var draft = await staffService.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 7, 10),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productAId, Quantity = 4, UnitPrice = 500_000m },
                new() { ProductId = productBId, Quantity = 3, UnitPrice = 180_000m },
            },
        });
        Assert.True(draft.IsSuccess);
        var poId = draft.Value!.Id;

        // 1. Terima barang langsung dari status Draft -> DITOLAK
        var receiveDraftResult = await staffService.ReceiveAsync(poId);
        Assert.True(receiveDraftResult.IsFailure);
        Assert.Contains("disetujui terlebih dahulu", receiveDraftResult.Error!, StringComparison.OrdinalIgnoreCase);

        // 2. Admin menyetujui PO
        var approveResult = await adminService.ApproveAsync(poId);
        Assert.True(approveResult.IsSuccess);

        // 3. Staff menerima barang yang telah disetujui -> BERHASIL
        var receiveResult = await staffService.ReceiveAsync(poId);
        Assert.True(receiveResult.IsSuccess, receiveResult.Error);
        Assert.Equal(PurchaseOrderStatus.Received, receiveResult.Value!.Status);

        // Verifikasi stok fisik bertambah secara akurat
        await using var context = _db.CreateContext();
        var prodA = await context.Products.SingleAsync(p => p.Id == productAId);
        var prodB = await context.Products.SingleAsync(p => p.Id == productBId);
        Assert.Equal(14, prodA.Stock); // 10 + 4
        Assert.Equal(8, prodB.Stock);  // 5 + 3

        // Verifikasi mutasi kartu stok In tercatat lengkap
        var stockCards = await context.StockCards.OrderBy(c => c.ProductId).ToListAsync();
        Assert.Equal(2, stockCards.Count);

        Assert.Equal(productAId, stockCards[0].ProductId);
        Assert.Equal(StockMovementType.In, stockCards[0].MovementType);
        Assert.Equal(4, stockCards[0].Quantity);
        Assert.Equal(10, stockCards[0].StockBefore);
        Assert.Equal(14, stockCards[0].StockAfter);
        Assert.Equal("PO-2026-0001", stockCards[0].Reference);

        Assert.Equal(productBId, stockCards[1].ProductId);
        Assert.Equal(StockMovementType.In, stockCards[1].MovementType);
        Assert.Equal(3, stockCards[1].Quantity);
        Assert.Equal(5, stockCards[1].StockBefore);
        Assert.Equal(8, stockCards[1].StockAfter);
        Assert.Equal("PO-2026-0001", stockCards[1].Reference);

        // ATURAN INTEGRITAS UTAMA: TIDAK BOLEH ADA JURNAL UMUM OTOMATIS
        Assert.False(await context.JournalEntries.AnyAsync());

        // Menerima ulang PO yang sudah Received ditolak
        var reReceiveResult = await staffService.ReceiveAsync(poId);
        Assert.True(reReceiveResult.IsFailure);
        Assert.Contains("sudah diterima", reReceiveResult.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancel_DraftDanApprovedBerhasil_DitolakBilaAdaPembayaranAtauReceivedAtauCancelled()
    {
        var (supplierId, productAId, _) = await SeedFixtureAsync();
        var staffService = CreateService(Roles.Staff);
        var adminService = CreateService(Roles.Admin);

        // 1. Batalkan saat Draft -> Berhasil
        var poDraft = await staffService.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 8, 1),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 500_000m },
            },
        });
        var cancelDraftResult = await staffService.CancelAsync(poDraft.Value!.Id);
        Assert.True(cancelDraftResult.IsSuccess);
        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelDraftResult.Value!.Status);

        // Batalkan ulang PO yang sudah Cancelled -> Ditolak
        var reCancelResult = await staffService.CancelAsync(poDraft.Value.Id);
        Assert.True(reCancelResult.IsFailure);
        Assert.Contains("sudah dibatalkan", reCancelResult.Error!, StringComparison.OrdinalIgnoreCase);

        // 2. Batalkan saat Approved (tanpa pembayaran) -> Berhasil
        var poApproved = await staffService.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 8, 2),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productAId, Quantity = 2, UnitPrice = 500_000m },
            },
        });
        await adminService.ApproveAsync(poApproved.Value!.Id);
        var cancelApprovedResult = await staffService.CancelAsync(poApproved.Value.Id);
        Assert.True(cancelApprovedResult.IsSuccess);
        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelApprovedResult.Value!.Status);

        // 3. Batalkan saat sudah memiliki pembayaran (PaidAmount > 0) -> Ditolak
        var poWithPayment = await staffService.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 8, 3),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productAId, Quantity = 2, UnitPrice = 500_000m },
            },
        });
        await adminService.ApproveAsync(poWithPayment.Value!.Id);

        // Simulasikan pembayaran sebagian via DB context
        await using (var context = _db.CreateContext())
        {
            var dbPo = await context.PurchaseOrders.SingleAsync(p => p.Id == poWithPayment.Value.Id);
            dbPo.PaidAmount = 200_000m;
            dbPo.RemainingAmount = 800_000m;
            dbPo.PaymentStatus = PaymentStatus.PartiallyPaid;
            await context.SaveChangesAsync();
        }

        var cancelPaidResult = await staffService.CancelAsync(poWithPayment.Value.Id);
        Assert.True(cancelPaidResult.IsFailure);
        Assert.Contains("pembayaran", cancelPaidResult.Error!, StringComparison.OrdinalIgnoreCase);

        // 4. Batalkan saat sudah Received -> Ditolak
        var poReceived = await staffService.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 8, 4),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 500_000m },
            },
        });
        await adminService.ApproveAsync(poReceived.Value!.Id);
        await staffService.ReceiveAsync(poReceived.Value.Id);

        var cancelReceivedResult = await staffService.CancelAsync(poReceived.Value.Id);
        Assert.True(cancelReceivedResult.IsFailure);
        Assert.Contains("sudah diterima", cancelReceivedResult.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAll_DanGetOutstanding_BerfungsiSesuaiFilter()
    {
        var (supplierId, productAId, _) = await SeedFixtureAsync();
        var staffService = CreateService(Roles.Staff);
        var adminService = CreateService(Roles.Admin);
        var viewerService = CreateService(Roles.Viewer);

        // PO 1: Received & Belum Lunas (Sisa 500.000)
        var po1 = await staffService.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 9, 1),
            DueDate = new DateTime(2026, 9, 15),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 500_000m },
            },
        });
        await adminService.ApproveAsync(po1.Value!.Id);
        await staffService.ReceiveAsync(po1.Value.Id);

        // PO 2: Received & Sudah Lunas (RemainingAmount = 0)
        var po2 = await staffService.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 9, 5),
            DueDate = new DateTime(2026, 9, 20),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productAId, Quantity = 2, UnitPrice = 500_000m },
            },
        });
        await adminService.ApproveAsync(po2.Value!.Id);
        await staffService.ReceiveAsync(po2.Value.Id);
        await using (var context = _db.CreateContext())
        {
            var dbPo2 = await context.PurchaseOrders.SingleAsync(p => p.Id == po2.Value.Id);
            dbPo2.PaidAmount = 1_000_000m;
            dbPo2.RemainingAmount = 0m;
            dbPo2.PaymentStatus = PaymentStatus.Paid;
            await context.SaveChangesAsync();
        }

        // PO 3: Masih Approved (Belum Received)
        var po3 = await staffService.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplierId,
            OrderDate = new DateTime(2026, 9, 10),
            DueDate = new DateTime(2026, 9, 25),
            Details = new List<CreatePurchaseOrderDetailDto>
            {
                new() { ProductId = productAId, Quantity = 1, UnitPrice = 500_000m },
            },
        });
        await adminService.ApproveAsync(po3.Value!.Id);

        // 1. Uji GetAllAsync oleh Viewer
        var allResult = await viewerService.GetAllAsync(new PurchaseOrderFilterDto());
        Assert.True(allResult.IsSuccess);
        Assert.Equal(3, allResult.Value!.Count);

        // Filter status Received
        var receivedResult = await viewerService.GetAllAsync(new PurchaseOrderFilterDto
        {
            Status = PurchaseOrderStatus.Received,
        });
        Assert.True(receivedResult.IsSuccess);
        Assert.Equal(2, receivedResult.Value!.Count);

        // Filter pencarian supplier
        var searchResult = await viewerService.GetAllAsync(new PurchaseOrderFilterDto
        {
            Search = "Distributor",
        });
        Assert.True(searchResult.IsSuccess);
        Assert.Equal(3, searchResult.Value!.Count);

        // 2. Uji GetOutstandingAsync: HANYA PO yang status Received DAN RemainingAmount > 0
        var outstandingResult = await viewerService.GetOutstandingAsync();
        Assert.True(outstandingResult.IsSuccess);
        var item = Assert.Single(outstandingResult.Value!);
        Assert.Equal(po1.Value.Id, item.ReferenceId);
        Assert.Equal("PO-2026-0001", item.ReferenceNumber);
        Assert.Equal(500_000m, item.RemainingAmount);
        Assert.Equal(ReferenceType.PurchaseOrder, item.ReferenceType);
        Assert.Equal("PT Distributor Aki", item.PartyName);
    }
}
