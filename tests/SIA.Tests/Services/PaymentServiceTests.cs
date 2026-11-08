using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.Payments;
using SIA.Domain.Entities.Purchasing;
using SIA.Domain.Entities.Sales;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data.Seed;
using SIA.Infrastructure.Identity;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;

namespace SIA.Tests.Services;

public class PaymentServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private const string ReadDenied = "Anda tidak memiliki hak akses untuk melihat data ini.";
    private const string WriteDenied = "Anda tidak memiliki hak akses untuk mengubah data ini.";

    public void Dispose() => _db.Dispose();

    private PaymentService CreateService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);
        var numbers = new TransactionNumberService(context, user);
        return new PaymentService(context, user, numbers);
    }

    private async Task<int> SeedUnpaidSaleAsync(decimal total = 1_000_000m, decimal paid = 0m)
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

        var customer = new Customer { Name = "Toko Maju" };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var sale = new Sale
        {
            InvoiceNumber = "INV-2026-0001",
            SaleDate = new DateTime(2026, 4, 1),
            DueDate = new DateTime(2026, 4, 15),
            CustomerId = customer.Id,
            UserId = "user-test",
            TotalAmount = total,
            PaidAmount = paid,
            RemainingAmount = total - paid,
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = paid >= total ? PaymentStatus.Paid : paid <= 0 ? PaymentStatus.Unpaid : PaymentStatus.PartiallyPaid,
        };
        context.Sales.Add(sale);
        await context.SaveChangesAsync();
        return sale.Id;
    }

    private async Task<int> SeedPurchaseOrderAsync(PurchaseOrderStatus status, decimal total = 2_000_000m, decimal paid = 0m)
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

        var supplier = new Supplier { Name = "PT Distributor Aki" };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var po = new PurchaseOrder
        {
            PONumber = "PO-2026-0001",
            OrderDate = new DateTime(2026, 4, 1),
            DueDate = new DateTime(2026, 4, 30),
            SupplierId = supplier.Id,
            UserId = "user-test",
            TotalAmount = total,
            PaidAmount = paid,
            RemainingAmount = total - paid,
            Status = status,
            PaymentStatus = paid >= total ? PaymentStatus.Paid : paid <= 0 ? PaymentStatus.Unpaid : PaymentStatus.PartiallyPaid,
        };
        context.PurchaseOrders.Add(po);
        await context.SaveChangesAsync();
        return po.Id;
    }

    private async Task<(int saleId, int customerId)> SeedCustomSaleAsync(
        string invoiceNumber,
        string customerName,
        decimal total = 1_000_000m,
        decimal paid = 0m,
        DateTime? saleDate = null,
        DateTime? dueDate = null,
        PaymentMethod paymentMethod = PaymentMethod.Cash)
    {
        await using var context = _db.CreateContext();
        await MasterDataSeeder.SeedTransactionNumberConfigsAsync(context, 2026, NullLogger.Instance);

        if (!await context.Users.AnyAsync(u => u.Id == "user-test"))
        {
            context.Users.Add(new ApplicationUser
            {
                Id = "user-test",
                UserName = "tester@sia.local",
                FullName = "Kasir Uji",
                Email = "tester@sia.local",
            });
        }

        var customer = new Customer { Name = customerName };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var sale = new Sale
        {
            InvoiceNumber = invoiceNumber,
            SaleDate = saleDate ?? new DateTime(2026, 4, 1),
            DueDate = dueDate,
            CustomerId = customer.Id,
            UserId = "user-test",
            TotalAmount = total,
            PaidAmount = paid,
            RemainingAmount = total - paid,
            PaymentMethod = paymentMethod,
            PaymentStatus = paid >= total ? PaymentStatus.Paid : paid <= 0 ? PaymentStatus.Unpaid : PaymentStatus.PartiallyPaid,
        };
        context.Sales.Add(sale);
        await context.SaveChangesAsync();
        return (sale.Id, customer.Id);
    }

    private async Task<(int poId, int supplierId)> SeedCustomPurchaseOrderAsync(
        string poNumber,
        string supplierName,
        PurchaseOrderStatus status,
        decimal total = 2_000_000m,
        decimal paid = 0m,
        DateTime? orderDate = null,
        DateTime? dueDate = null)
    {
        await using var context = _db.CreateContext();
        await MasterDataSeeder.SeedTransactionNumberConfigsAsync(context, 2026, NullLogger.Instance);

        if (!await context.Users.AnyAsync(u => u.Id == "user-test"))
        {
            context.Users.Add(new ApplicationUser
            {
                Id = "user-test",
                UserName = "tester@sia.local",
                FullName = "Kasir Uji",
                Email = "tester@sia.local",
            });
        }

        var supplier = new Supplier { Name = supplierName };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var po = new PurchaseOrder
        {
            PONumber = poNumber,
            OrderDate = orderDate ?? new DateTime(2026, 4, 1),
            DueDate = dueDate,
            SupplierId = supplier.Id,
            UserId = "user-test",
            TotalAmount = total,
            PaidAmount = paid,
            RemainingAmount = total - paid,
            Status = status,
            PaymentStatus = paid >= total ? PaymentStatus.Paid : paid <= 0 ? PaymentStatus.Unpaid : PaymentStatus.PartiallyPaid,
        };
        context.PurchaseOrders.Add(po);
        await context.SaveChangesAsync();
        return (po.Id, supplier.Id);
    }

    [Fact]
    public async Task Create_PembayaranBertahapPadaSale_UpdatePaidRemainingDanPaymentStatus()
    {
        var saleId = await SeedUnpaidSaleAsync(total: 1_000_000m);
        var service = CreateService(Roles.Staff);

        // Pembayaran pertama: 400.000 -> PartiallyPaid
        var first = await service.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            PaymentDate = new DateTime(2026, 4, 5),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 400_000m,
            Notes = "Cicilan 1",
        });

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal("PAY-2026-0001", first.Value!.PaymentNumber);
        Assert.Equal("INV-2026-0001", first.Value.ReferenceNumber);

        await using (var context = _db.CreateContext())
        {
            var sale = await context.Sales.SingleAsync(s => s.Id == saleId);
            Assert.Equal(400_000m, sale.PaidAmount);
            Assert.Equal(600_000m, sale.RemainingAmount);
            Assert.Equal(PaymentStatus.PartiallyPaid, sale.PaymentStatus);
        }

        // Pembayaran kedua: 600.000 -> Paid
        var second = await service.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            PaymentDate = new DateTime(2026, 4, 10),
            PaymentMethod = PaymentMethod.Transfer,
            Amount = 600_000m,
            Notes = "Pelunasan",
        });

        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal("PAY-2026-0002", second.Value!.PaymentNumber);

        await using (var context = _db.CreateContext())
        {
            var sale = await context.Sales.SingleAsync(s => s.Id == saleId);
            Assert.Equal(1_000_000m, sale.PaidAmount);
            Assert.Equal(0m, sale.RemainingAmount);
            Assert.Equal(PaymentStatus.Paid, sale.PaymentStatus);
            Assert.False(await context.JournalEntries.AnyAsync());
        }

        var history = await CreateService(Roles.Viewer).GetByReferenceAsync(ReferenceType.Sale, saleId);
        Assert.True(history.IsSuccess);
        Assert.Equal(2, history.Value!.Count);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Approved)]
    [InlineData(PurchaseOrderStatus.Received)]
    public async Task Create_PembayaranPadaPurchaseOrderValid_UpdatePaidRemainingDanPaymentStatus(PurchaseOrderStatus status)
    {
        var poId = await SeedPurchaseOrderAsync(status, total: 2_000_000m);

        var result = await CreateService(Roles.Admin).CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.PurchaseOrder,
            ReferenceId = poId,
            PaymentDate = new DateTime(2026, 4, 10),
            PaymentMethod = PaymentMethod.Transfer,
            Amount = 2_000_000m,
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("PO-2026-0001", result.Value!.ReferenceNumber);

        await using var context = _db.CreateContext();
        var po = await context.PurchaseOrders.SingleAsync(p => p.Id == poId);
        Assert.Equal(2_000_000m, po.PaidAmount);
        Assert.Equal(0m, po.RemainingAmount);
        Assert.Equal(PaymentStatus.Paid, po.PaymentStatus);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public async Task Create_PurchaseOrderDraftAtauCancelled_Ditolak(PurchaseOrderStatus status)
    {
        var poId = await SeedPurchaseOrderAsync(status, total: 2_000_000m);

        var result = await CreateService(Roles.Admin).CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.PurchaseOrder,
            ReferenceId = poId,
            PaymentDate = new DateTime(2026, 4, 10),
            PaymentMethod = PaymentMethod.Transfer,
            Amount = 500_000m,
        });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Create_JumlahMelebihiSisaTagihan_Ditolak()
    {
        var saleId = await SeedUnpaidSaleAsync(total: 1_000_000m, paid: 700_000m);

        var result = await CreateService(Roles.Staff).CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            PaymentDate = new DateTime(2026, 4, 5),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 400_000m,
        });

        Assert.True(result.IsFailure);
        Assert.Contains("melebihi sisa tagihan", result.Error!);
    }

    [Fact]
    public async Task Create_TagihanSudahLunas_Ditolak()
    {
        var saleId = await SeedUnpaidSaleAsync(total: 1_000_000m, paid: 1_000_000m);

        var result = await CreateService(Roles.Staff).CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            PaymentDate = new DateTime(2026, 4, 5),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 100_000m,
        });

        Assert.True(result.IsFailure);
        Assert.Contains("sudah lunas", result.Error!);
    }

    [Fact]
    public async Task Create_ReferensiTidakAda_Ditolak()
    {
        await SeedUnpaidSaleAsync();

        var saleResult = await CreateService(Roles.Staff).CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = 9999,
            PaymentDate = new DateTime(2026, 4, 5),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 100_000m,
        });
        Assert.True(saleResult.IsFailure);

        var poResult = await CreateService(Roles.Staff).CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.PurchaseOrder,
            ReferenceId = 9999,
            PaymentDate = new DateTime(2026, 4, 5),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 100_000m,
        });
        Assert.True(poResult.IsFailure);
    }

    [Fact]
    public async Task Create_TanggalPembayaranSebelumTanggalTransaksi_Ditolak()
    {
        var saleId = await SeedUnpaidSaleAsync();

        var result = await CreateService(Roles.Staff).CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            PaymentDate = new DateTime(2026, 3, 25),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 100_000m,
        });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Create_Viewer_Ditolak()
    {
        var saleId = await SeedUnpaidSaleAsync();

        var result = await CreateService(Roles.Viewer).CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            PaymentDate = new DateTime(2026, 4, 5),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 100_000m,
        });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Create_DenganCultureIndonesia_ValidasiRangeDecimalBerhasil()
    {
        var poId = await SeedPurchaseOrderAsync(PurchaseOrderStatus.Approved, total: 250_000m);
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        var previousUiCulture = System.Globalization.CultureInfo.CurrentUICulture;

        try
        {
            var idCulture = System.Globalization.CultureInfo.GetCultureInfo("id-ID");
            System.Globalization.CultureInfo.CurrentCulture = idCulture;
            System.Globalization.CultureInfo.CurrentUICulture = idCulture;

            var invalidResult = await CreateService(Roles.Admin).CreateAsync(new CreatePaymentDto
            {
                ReferenceType = ReferenceType.PurchaseOrder,
                ReferenceId = poId,
                PaymentDate = new DateTime(2026, 4, 10),
                PaymentMethod = PaymentMethod.Transfer,
                Amount = 0m,
            });
            Assert.True(invalidResult.IsFailure);

            var validResult = await CreateService(Roles.Admin).CreateAsync(new CreatePaymentDto
            {
                ReferenceType = ReferenceType.PurchaseOrder,
                ReferenceId = poId,
                PaymentDate = new DateTime(2026, 4, 10),
                PaymentMethod = PaymentMethod.Transfer,
                Amount = 250_000m,
            });
            Assert.True(validResult.IsSuccess, validResult.Error);
            Assert.Equal(250_000m, validResult.Value!.Amount);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
            System.Globalization.CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    // -------------------------------------------------------------------------
    // 1. GetAllAsync dengan filter ReferenceType, PaymentMethod, Date range
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetAllAsync_FilterReferenceTypeMetodeDanTanggal_MengembalikanDataSesuaiFilter()
    {
        var (sale1Id, _) = await SeedCustomSaleAsync("INV-2026-0001", "Pelanggan 1", total: 1_000_000m, saleDate: new DateTime(2026, 4, 1));
        var (sale2Id, _) = await SeedCustomSaleAsync("INV-2026-0002", "Pelanggan 2", total: 1_000_000m, saleDate: new DateTime(2026, 4, 1));
        var (po1Id, _) = await SeedCustomPurchaseOrderAsync("PO-2026-0001", "Pemasok 1", PurchaseOrderStatus.Approved, total: 2_000_000m, orderDate: new DateTime(2026, 4, 1));
        var (po2Id, _) = await SeedCustomPurchaseOrderAsync("PO-2026-0002", "Pemasok 2", PurchaseOrderStatus.Approved, total: 2_000_000m, orderDate: new DateTime(2026, 4, 1));

        var createService = CreateService(Roles.Admin);
        // Payment 1: Sale, Cash, 2026-04-01
        await createService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = sale1Id,
            PaymentDate = new DateTime(2026, 4, 1),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 100_000m,
        });

        // Payment 2: Sale, Transfer, 2026-04-05
        await createService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = sale2Id,
            PaymentDate = new DateTime(2026, 4, 5),
            PaymentMethod = PaymentMethod.Transfer,
            Amount = 200_000m,
        });

        // Payment 3: PurchaseOrder, Transfer, 2026-04-05
        await createService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.PurchaseOrder,
            ReferenceId = po1Id,
            PaymentDate = new DateTime(2026, 4, 5),
            PaymentMethod = PaymentMethod.Transfer,
            Amount = 300_000m,
        });

        // Payment 4: PurchaseOrder, Cash, 2026-04-10
        await createService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.PurchaseOrder,
            ReferenceId = po2Id,
            PaymentDate = new DateTime(2026, 4, 10),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 400_000m,
        });

        var queryService = CreateService(Roles.Viewer);

        // Filter ReferenceType.Sale
        var saleOnly = await queryService.GetAllAsync(new PaymentFilterDto { ReferenceType = ReferenceType.Sale });
        Assert.True(saleOnly.IsSuccess, saleOnly.Error);
        Assert.Equal(2, saleOnly.Value!.Count);
        Assert.All(saleOnly.Value, p => Assert.Equal(ReferenceType.Sale, p.ReferenceType));

        // Filter PaymentMethod.Transfer
        var transferOnly = await queryService.GetAllAsync(new PaymentFilterDto { PaymentMethod = PaymentMethod.Transfer });
        Assert.True(transferOnly.IsSuccess, transferOnly.Error);
        Assert.Equal(2, transferOnly.Value!.Count);
        Assert.All(transferOnly.Value, p => Assert.Equal(PaymentMethod.Transfer, p.PaymentMethod));

        // Filter Date range (2026-04-03 to 2026-04-07)
        var dateFiltered = await queryService.GetAllAsync(new PaymentFilterDto
        {
            DateFrom = new DateTime(2026, 4, 3),
            DateTo = new DateTime(2026, 4, 7),
        });
        Assert.True(dateFiltered.IsSuccess, dateFiltered.Error);
        Assert.Equal(2, dateFiltered.Value!.Count);

        // Multi-filter kombinasi (Sale + Transfer + Date Range)
        var combined = await queryService.GetAllAsync(new PaymentFilterDto
        {
            ReferenceType = ReferenceType.Sale,
            PaymentMethod = PaymentMethod.Transfer,
            DateFrom = new DateTime(2026, 4, 3),
            DateTo = new DateTime(2026, 4, 7),
        });
        Assert.True(combined.IsSuccess, combined.Error);
        Assert.Single(combined.Value!);
        Assert.Equal(200_000m, combined.Value[0].Amount);
        Assert.Equal("INV-2026-0002", combined.Value[0].ReferenceNumber);
    }

    // -------------------------------------------------------------------------
    // 2. GetAllAsync PartyName population termasuk saat party soft-deleted
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetAllAsync_PopulatePartyNameCustomerDanSupplier_TermasukSaatPartySoftDeleted()
    {
        var (saleId, customerId) = await SeedCustomSaleAsync("INV-2026-0001", "Pelanggan Abadi", total: 1_000_000m);
        var (poId, supplierId) = await SeedCustomPurchaseOrderAsync("PO-2026-0001", "Pemasok Utama", PurchaseOrderStatus.Approved, total: 2_000_000m);

        var adminService = CreateService(Roles.Admin);
        await adminService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            PaymentDate = new DateTime(2026, 4, 5),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 150_000m,
        });

        await adminService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.PurchaseOrder,
            ReferenceId = poId,
            PaymentDate = new DateTime(2026, 4, 6),
            PaymentMethod = PaymentMethod.Transfer,
            Amount = 250_000m,
        });

        // Soft delete Customer dan Supplier
        await using (var context = _db.CreateContext())
        {
            var customer = await context.Customers.SingleAsync(c => c.Id == customerId);
            context.Customers.Remove(customer);

            var supplier = await context.Suppliers.SingleAsync(s => s.Id == supplierId);
            context.Suppliers.Remove(supplier);

            await context.SaveChangesAsync();
        }

        // Pastikan customer dan supplier ter-soft-delete
        await using (var context = _db.CreateContext())
        {
            Assert.False(await context.Customers.AnyAsync(c => c.Id == customerId));
            Assert.False(await context.Suppliers.AnyAsync(s => s.Id == supplierId));
            Assert.True(await context.Customers.IgnoreQueryFilters().AnyAsync(c => c.Id == customerId && c.IsDeleted));
            Assert.True(await context.Suppliers.IgnoreQueryFilters().AnyAsync(s => s.Id == supplierId && s.IsDeleted));
        }

        var result = await CreateService(Roles.Viewer).GetAllAsync(new PaymentFilterDto());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value!.Count);

        var salePayment = result.Value.Single(p => p.ReferenceType == ReferenceType.Sale);
        Assert.Equal("INV-2026-0001", salePayment.ReferenceNumber);
        Assert.Equal("Pelanggan Abadi", salePayment.PartyName);

        var poPayment = result.Value.Single(p => p.ReferenceType == ReferenceType.PurchaseOrder);
        Assert.Equal("PO-2026-0001", poPayment.ReferenceNumber);
        Assert.Equal("Pemasok Utama", poPayment.PartyName);
    }

    // -------------------------------------------------------------------------
    // 3. GetAllAsync Search matching PaymentNumber, ReferenceNumber, PartyName, Notes
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetAllAsync_SearchNomorPembayaranNomorReferensiNamaPartyDanCatatan_Cocok()
    {
        var (sale1Id, _) = await SeedCustomSaleAsync("INV-2026-ALPHA", "Bengkel Barokah", total: 1_000_000m);
        var (sale2Id, _) = await SeedCustomSaleAsync("INV-2026-BETA", "Toko Sumber Rejeki", total: 1_000_000m);
        var (po1Id, _) = await SeedCustomPurchaseOrderAsync("PO-2026-GAMMA", "PT Inti Mandiri", PurchaseOrderStatus.Approved, total: 1_000_000m);

        var adminService = CreateService(Roles.Admin);
        var p1 = await adminService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = sale1Id,
            PaymentDate = new DateTime(2026, 4, 5),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 100_000m,
            Notes = "Pembayaran DP rutin",
        });

        var p2 = await adminService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = sale2Id,
            PaymentDate = new DateTime(2026, 4, 6),
            PaymentMethod = PaymentMethod.Transfer,
            Amount = 200_000m,
            Notes = "Pelunasan invoice tempo",
        });

        var p3 = await adminService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.PurchaseOrder,
            ReferenceId = po1Id,
            PaymentDate = new DateTime(2026, 4, 7),
            PaymentMethod = PaymentMethod.Transfer,
            Amount = 300_000m,
            Notes = "Cicilan terminal aki",
        });

        var queryService = CreateService(Roles.Viewer);

        // Search PaymentNumber
        var searchByPaymentNumber = await queryService.GetAllAsync(new PaymentFilterDto { Search = p1.Value!.PaymentNumber });
        Assert.True(searchByPaymentNumber.IsSuccess);
        Assert.Single(searchByPaymentNumber.Value!);
        Assert.Equal(p1.Value.PaymentNumber, searchByPaymentNumber.Value[0].PaymentNumber);

        // Search ReferenceNumber ("BETA")
        var searchByRef = await queryService.GetAllAsync(new PaymentFilterDto { Search = "BETA" });
        Assert.True(searchByRef.IsSuccess);
        Assert.Single(searchByRef.Value!);
        Assert.Equal("INV-2026-BETA", searchByRef.Value[0].ReferenceNumber);

        // Search PartyName ("Mandiri")
        var searchByParty = await queryService.GetAllAsync(new PaymentFilterDto { Search = "Mandiri" });
        Assert.True(searchByParty.IsSuccess);
        Assert.Single(searchByParty.Value!);
        Assert.Equal("PT Inti Mandiri", searchByParty.Value[0].PartyName);

        // Search Notes ("terminal" - case insensitive)
        var searchByNotes = await queryService.GetAllAsync(new PaymentFilterDto { Search = "terminal" });
        Assert.True(searchByNotes.IsSuccess);
        Assert.Single(searchByNotes.Value!);
        Assert.Equal(p3.Value!.PaymentNumber, searchByNotes.Value[0].PaymentNumber);
    }

    // -------------------------------------------------------------------------
    // 4. GetAllAsync unauthorized user returns ViewAllData denial
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetAllAsync_UserTanpaRoleAtauTidakLogin_DitolakViewAllData()
    {
        await SeedUnpaidSaleAsync();
        var unauthenticatedService = CreateService(); // Tanpa role = unauthenticated

        var result = await unauthenticatedService.GetAllAsync(new PaymentFilterDto());

        Assert.True(result.IsFailure);
        Assert.Equal(ReadDenied, result.Error);
    }

    // -------------------------------------------------------------------------
    // 5. GetOutstandingAsync returns unpaid Sales and Approved/Received POs
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetOutstandingAsync_MengembalikanSaleBelumLunasDanPOApprovedAtauReceived()
    {
        var (saleId, _) = await SeedCustomSaleAsync("INV-2026-0010", "Pelanggan A", total: 1_000_000m, paid: 200_000m);
        var (poApprovedId, _) = await SeedCustomPurchaseOrderAsync("PO-2026-0010", "Pemasok B", PurchaseOrderStatus.Approved, total: 2_000_000m, paid: 500_000m);
        var (poReceivedId, _) = await SeedCustomPurchaseOrderAsync("PO-2026-0011", "Pemasok C", PurchaseOrderStatus.Received, total: 3_000_000m, paid: 0m);

        var service = CreateService(Roles.Viewer);
        var result = await service.GetOutstandingAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(3, result.Value!.Count);

        var saleOut = result.Value.Single(o => o.ReferenceType == ReferenceType.Sale && o.ReferenceId == saleId);
        Assert.Equal("INV-2026-0010", saleOut.ReferenceNumber);
        Assert.Equal("Pelanggan A", saleOut.PartyName);
        Assert.Equal(800_000m, saleOut.RemainingAmount);
        Assert.Equal(PaymentStatus.PartiallyPaid, saleOut.PaymentStatus);
        Assert.Null(saleOut.PurchaseOrderStatus);

        var poApprOut = result.Value.Single(o => o.ReferenceType == ReferenceType.PurchaseOrder && o.ReferenceId == poApprovedId);
        Assert.Equal("PO-2026-0010", poApprOut.ReferenceNumber);
        Assert.Equal("Pemasok B", poApprOut.PartyName);
        Assert.Equal(1_500_000m, poApprOut.RemainingAmount);
        Assert.Equal(PurchaseOrderStatus.Approved, poApprOut.PurchaseOrderStatus);

        var poRecvOut = result.Value.Single(o => o.ReferenceType == ReferenceType.PurchaseOrder && o.ReferenceId == poReceivedId);
        Assert.Equal("PO-2026-0011", poRecvOut.ReferenceNumber);
        Assert.Equal("Pemasok C", poRecvOut.PartyName);
        Assert.Equal(3_000_000m, poRecvOut.RemainingAmount);
        Assert.Equal(PurchaseOrderStatus.Received, poRecvOut.PurchaseOrderStatus);
    }

    // -------------------------------------------------------------------------
    // 6. GetOutstandingAsync excludes Draft and Cancelled POs
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetOutstandingAsync_MengecualikanPurchaseOrderDraftDanCancelled()
    {
        await SeedCustomPurchaseOrderAsync("PO-2026-DRAFT", "Pemasok Draft", PurchaseOrderStatus.Draft, total: 1_000_000m, paid: 0m);
        await SeedCustomPurchaseOrderAsync("PO-2026-CANCEL", "Pemasok Cancel", PurchaseOrderStatus.Cancelled, total: 1_000_000m, paid: 0m);
        await SeedCustomPurchaseOrderAsync("PO-2026-APPR", "Pemasok Sah", PurchaseOrderStatus.Approved, total: 1_000_000m, paid: 0m);
        await SeedCustomSaleAsync("INV-2026-SALE", "Pelanggan Sah", total: 500_000m, paid: 0m);

        var service = CreateService(Roles.Staff);
        var result = await service.GetOutstandingAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value!.Count);
        Assert.DoesNotContain(result.Value, o => o.ReferenceNumber == "PO-2026-DRAFT");
        Assert.DoesNotContain(result.Value, o => o.ReferenceNumber == "PO-2026-CANCEL");
        Assert.Contains(result.Value, o => o.ReferenceNumber == "PO-2026-APPR");
        Assert.Contains(result.Value, o => o.ReferenceNumber == "INV-2026-SALE");
    }

    // -------------------------------------------------------------------------
    // 7. GetOutstandingAsync excludes fully paid documents (RemainingAmount == 0)
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetOutstandingAsync_MengecualikanDokumenYangSudahLunas()
    {
        // Sale lunas
        await SeedCustomSaleAsync("INV-2026-LUNAS", "Pelanggan Lunas", total: 1_000_000m, paid: 1_000_000m);
        // PO Approved lunas
        await SeedCustomPurchaseOrderAsync("PO-2026-LUNAS", "Pemasok Lunas", PurchaseOrderStatus.Approved, total: 2_000_000m, paid: 2_000_000m);
        // Sale belum lunas
        await SeedCustomSaleAsync("INV-2026-SISA", "Pelanggan Berutang", total: 1_000_000m, paid: 250_000m);

        var service = CreateService(Roles.Viewer);
        var result = await service.GetOutstandingAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(result.Value!);
        Assert.Equal("INV-2026-SISA", result.Value[0].ReferenceNumber);
        Assert.Equal(750_000m, result.Value[0].RemainingAmount);
    }

    // -------------------------------------------------------------------------
    // 8. GetOutstandingAsync OnlyOverdue filter
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetOutstandingAsync_FilterOnlyOverdue_HanyaMengembalikanTagihanJatuhTempo()
    {
        var today = DateTime.Today;
        // Overdue (jatuh tempo 5 hari lalu)
        await SeedCustomSaleAsync("INV-OVERDUE", "Pelanggan Telat", total: 1_000_000m, dueDate: today.AddDays(-5));
        // Belum jatuh tempo (5 hari ke depan)
        await SeedCustomPurchaseOrderAsync("PO-FUTURE", "Pemasok Aman", PurchaseOrderStatus.Approved, total: 2_000_000m, dueDate: today.AddDays(5));
        // Tanpa tanggal jatuh tempo
        await SeedCustomSaleAsync("INV-NODUE", "Pelanggan Kontan", total: 500_000m, dueDate: null);

        var service = CreateService(Roles.Viewer);
        var result = await service.GetOutstandingAsync(new OutstandingFilterDto { OnlyOverdue = true });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(result.Value!);
        Assert.Equal("INV-OVERDUE", result.Value[0].ReferenceNumber);
    }

    // -------------------------------------------------------------------------
    // 9. GetOutstandingAsync search by reference number and party name
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetOutstandingAsync_SearchNomorReferensiDanNamaParty_Cocok()
    {
        await SeedCustomSaleAsync("INV-TARGET-001", "Bengkel Sentosa Abadi", total: 1_000_000m);
        await SeedCustomPurchaseOrderAsync("PO-TARGET-002", "PT Distributor Mega Jaya", PurchaseOrderStatus.Approved, total: 2_000_000m);

        var service = CreateService(Roles.Viewer);

        // Search partial ReferenceNumber ("TARGET-001")
        var resRef = await service.GetOutstandingAsync(new OutstandingFilterDto { Search = "TARGET-001" });
        Assert.True(resRef.IsSuccess);
        Assert.Single(resRef.Value!);
        Assert.Equal("INV-TARGET-001", resRef.Value[0].ReferenceNumber);

        // Search partial PartyName ("mega jaya" - case insensitive)
        var resParty = await service.GetOutstandingAsync(new OutstandingFilterDto { Search = "mega jaya" });
        Assert.True(resParty.IsSuccess);
        Assert.Single(resParty.Value!);
        Assert.Equal("PO-TARGET-002", resParty.Value[0].ReferenceNumber);
        Assert.Equal("PT Distributor Mega Jaya", resParty.Value[0].PartyName);
    }

    // -------------------------------------------------------------------------
    // 10. GetOutstandingAsync sorting: overdue first, then DueDate, then TransactionDate
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetOutstandingAsync_Sorting_OverdueDahuluKemudianDueDateLaluTransactionDate()
    {
        var today = DateTime.Today;

        // 1. Overdue lebih awal: DueDate today - 10
        await SeedCustomSaleAsync("DOC-1-OVERDUE-EARLY", "Cust 1", total: 100_000m, saleDate: new DateTime(2026, 3, 1), dueDate: today.AddDays(-10));
        // 2. Overdue lebih dekat: DueDate today - 2
        await SeedCustomPurchaseOrderAsync("DOC-2-OVERDUE-LATE", "Supp 2", PurchaseOrderStatus.Approved, total: 100_000m, orderDate: new DateTime(2026, 3, 1), dueDate: today.AddDays(-2));
        // 3. Belum jatuh tempo lebih awal: DueDate today + 3
        await SeedCustomSaleAsync("DOC-3-FUTURE-EARLY", "Cust 3", total: 100_000m, saleDate: new DateTime(2026, 3, 1), dueDate: today.AddDays(3));
        // 4. Belum jatuh tempo lebih lama: DueDate today + 10
        await SeedCustomPurchaseOrderAsync("DOC-4-FUTURE-LATE", "Supp 4", PurchaseOrderStatus.Approved, total: 100_000m, orderDate: new DateTime(2026, 3, 1), dueDate: today.AddDays(10));
        // 5. Tanpa DueDate, transaksi lebih lama (2026-01-01)
        await SeedCustomSaleAsync("DOC-5-NODUE-TRANS-OLD", "Cust 5", total: 100_000m, saleDate: new DateTime(2026, 1, 1), dueDate: null);
        // 6. Tanpa DueDate, transaksi lebih baru (2026-02-01)
        await SeedCustomPurchaseOrderAsync("DOC-6-NODUE-TRANS-NEW", "Supp 6", PurchaseOrderStatus.Approved, total: 100_000m, orderDate: new DateTime(2026, 2, 1), dueDate: null);

        var service = CreateService(Roles.Viewer);
        var result = await service.GetOutstandingAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(6, result.Value!.Count);

        var expectedOrder = new[]
        {
            "DOC-1-OVERDUE-EARLY",
            "DOC-2-OVERDUE-LATE",
            "DOC-3-FUTURE-EARLY",
            "DOC-4-FUTURE-LATE",
            "DOC-5-NODUE-TRANS-OLD",
            "DOC-6-NODUE-TRANS-NEW",
        };

        var actualOrder = result.Value.Select(x => x.ReferenceNumber).ToArray();
        Assert.Equal(expectedOrder, actualOrder);
    }

    // -------------------------------------------------------------------------
    // 11. DeleteAsync by Admin reverses Sale PaidAmount, RemainingAmount, PaymentStatus, soft-deletes payment
    // -------------------------------------------------------------------------
    [Fact]
    public async Task DeleteAsync_AdminPadaSale_ReversiPaidRemainingStatusDanSoftDeletePayment()
    {
        var saleId = await SeedUnpaidSaleAsync(total: 1_000_000m);
        var adminService = CreateService(Roles.Admin);

        // Cicilan 1: 400.000 -> PartiallyPaid
        var pay1 = await adminService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            PaymentDate = new DateTime(2026, 4, 5),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 400_000m,
        });
        Assert.True(pay1.IsSuccess);

        // Cicilan 2: 600.000 -> Paid
        var pay2 = await adminService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            PaymentDate = new DateTime(2026, 4, 10),
            PaymentMethod = PaymentMethod.Transfer,
            Amount = 600_000m,
        });
        Assert.True(pay2.IsSuccess);

        // Hapus cicilan kedua (600.000) oleh Admin
        var deleteResult2 = await adminService.DeleteAsync(pay2.Value!.Id);
        Assert.True(deleteResult2.IsSuccess, deleteResult2.Error);

        await using (var context = _db.CreateContext())
        {
            var sale = await context.Sales.SingleAsync(s => s.Id == saleId);
            Assert.Equal(400_000m, sale.PaidAmount);
            Assert.Equal(600_000m, sale.RemainingAmount);
            Assert.Equal(PaymentStatus.PartiallyPaid, sale.PaymentStatus);

            Assert.Null(await context.Payments.SingleOrDefaultAsync(p => p.Id == pay2.Value.Id));
            var softDeletedPay2 = await context.Payments.IgnoreQueryFilters().SingleAsync(p => p.Id == pay2.Value.Id);
            Assert.True(softDeletedPay2.IsDeleted);
            Assert.NotNull(softDeletedPay2.DeletedAt);
        }

        // Hapus cicilan pertama (400.000) oleh Admin
        var deleteResult1 = await adminService.DeleteAsync(pay1.Value!.Id);
        Assert.True(deleteResult1.IsSuccess, deleteResult1.Error);

        await using (var context = _db.CreateContext())
        {
            var sale = await context.Sales.SingleAsync(s => s.Id == saleId);
            Assert.Equal(0m, sale.PaidAmount);
            Assert.Equal(1_000_000m, sale.RemainingAmount);
            Assert.Equal(PaymentStatus.Unpaid, sale.PaymentStatus);

            var softDeletedPay1 = await context.Payments.IgnoreQueryFilters().SingleAsync(p => p.Id == pay1.Value.Id);
            Assert.True(softDeletedPay1.IsDeleted);
        }
    }

    // -------------------------------------------------------------------------
    // 12. DeleteAsync by Admin reverses PO PaidAmount, RemainingAmount, PaymentStatus, soft-deletes payment
    // -------------------------------------------------------------------------
    [Fact]
    public async Task DeleteAsync_AdminPadaPurchaseOrder_ReversiPaidRemainingStatusDanSoftDeletePayment()
    {
        var poId = await SeedPurchaseOrderAsync(PurchaseOrderStatus.Approved, total: 2_000_000m);
        var adminService = CreateService(Roles.Admin);

        var payment = await adminService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.PurchaseOrder,
            ReferenceId = poId,
            PaymentDate = new DateTime(2026, 4, 10),
            PaymentMethod = PaymentMethod.Transfer,
            Amount = 2_000_000m,
        });
        Assert.True(payment.IsSuccess);

        // Verifikasi PO lunas
        await using (var context = _db.CreateContext())
        {
            var poBefore = await context.PurchaseOrders.SingleAsync(p => p.Id == poId);
            Assert.Equal(2_000_000m, poBefore.PaidAmount);
            Assert.Equal(0m, poBefore.RemainingAmount);
            Assert.Equal(PaymentStatus.Paid, poBefore.PaymentStatus);
        }

        // Hapus pembayaran oleh Admin
        var deleteResult = await adminService.DeleteAsync(payment.Value!.Id);
        Assert.True(deleteResult.IsSuccess, deleteResult.Error);

        await using (var context = _db.CreateContext())
        {
            var po = await context.PurchaseOrders.SingleAsync(p => p.Id == poId);
            Assert.Equal(0m, po.PaidAmount);
            Assert.Equal(2_000_000m, po.RemainingAmount);
            Assert.Equal(PaymentStatus.Unpaid, po.PaymentStatus);

            Assert.Null(await context.Payments.SingleOrDefaultAsync(p => p.Id == payment.Value.Id));
            var softDeleted = await context.Payments.IgnoreQueryFilters().SingleAsync(p => p.Id == payment.Value.Id);
            Assert.True(softDeleted.IsDeleted);
        }
    }

    // -------------------------------------------------------------------------
    // 13. DeleteAsync rejects Staff, Viewer, unauthenticated with WriteDenied
    // -------------------------------------------------------------------------
    [Fact]
    public async Task DeleteAsync_StaffViewerAtauUnauthenticated_DitolakWriteDenied()
    {
        var saleId = await SeedUnpaidSaleAsync(total: 1_000_000m);
        var adminService = CreateService(Roles.Admin);

        var payment = await adminService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            PaymentDate = new DateTime(2026, 4, 5),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 500_000m,
        });
        Assert.True(payment.IsSuccess);

        // Staff ditolak
        var staffResult = await CreateService(Roles.Staff).DeleteAsync(payment.Value!.Id);
        Assert.True(staffResult.IsFailure);
        Assert.Equal(WriteDenied, staffResult.Error);

        // Viewer ditolak
        var viewerResult = await CreateService(Roles.Viewer).DeleteAsync(payment.Value.Id);
        Assert.True(viewerResult.IsFailure);
        Assert.Equal(WriteDenied, viewerResult.Error);

        // Unauthenticated ditolak
        var unauthResult = await CreateService().DeleteAsync(payment.Value.Id);
        Assert.True(unauthResult.IsFailure);
        Assert.Equal(WriteDenied, unauthResult.Error);

        // Verifikasi tidak terhapus di DB dan saldo tidak berubah
        await using var context = _db.CreateContext();
        var paymentInDb = await context.Payments.SingleOrDefaultAsync(p => p.Id == payment.Value.Id);
        Assert.NotNull(paymentInDb);
        Assert.False(paymentInDb.IsDeleted);

        var sale = await context.Sales.SingleAsync(s => s.Id == saleId);
        Assert.Equal(500_000m, sale.PaidAmount);
    }

    // -------------------------------------------------------------------------
    // 14. DeleteAsync non-existent payment returns failure
    // -------------------------------------------------------------------------
    [Fact]
    public async Task DeleteAsync_PembayaranTidakDitemukan_MengembalikanGagal()
    {
        var adminService = CreateService(Roles.Admin);
        var result = await adminService.DeleteAsync(99999);

        Assert.True(result.IsFailure);
        Assert.Contains("tidak ditemukan", result.Error!);
    }

    // -------------------------------------------------------------------------
    // 15. DeleteAsync does not create or modify journal entries
    // -------------------------------------------------------------------------
    [Fact]
    public async Task DeleteAsync_TidakMembuatAtauMengubahJurnalUmum()
    {
        var saleId = await SeedUnpaidSaleAsync(total: 1_000_000m);
        var adminService = CreateService(Roles.Admin);

        var payment = await adminService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            PaymentDate = new DateTime(2026, 4, 5),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 500_000m,
        });
        Assert.True(payment.IsSuccess);

        await using (var context = _db.CreateContext())
        {
            Assert.False(await context.JournalEntries.AnyAsync());
        }

        var result = await adminService.DeleteAsync(payment.Value!.Id);
        Assert.True(result.IsSuccess, result.Error);

        // Verifikasi sesudah delete tetap tidak ada jurnal umum
        await using (var context = _db.CreateContext())
        {
            Assert.False(await context.JournalEntries.AnyAsync());
            Assert.False(await context.JournalEntryLines.AnyAsync());
        }
    }

    // -------------------------------------------------------------------------
    // 16. Backward-compatibility of GetAllAsync(DateTime?, DateTime?)
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetAllAsync_OverloadTanggalLama_KompatibelDanPopulatePartyName()
    {
        var (saleId, _) = await SeedCustomSaleAsync("INV-2026-LEGACY", "Pelanggan Tradisional", total: 1_000_000m);
        var adminService = CreateService(Roles.Admin);

        await adminService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            PaymentDate = new DateTime(2026, 4, 5),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 300_000m,
        });

        var viewerService = CreateService(Roles.Viewer);

        // Memanggil signature lama GetAllAsync(DateTime?, DateTime?, CancellationToken)
        var result = await viewerService.GetAllAsync(new DateTime(2026, 4, 1), new DateTime(2026, 4, 10));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(result.Value!);
        Assert.Equal("INV-2026-LEGACY", result.Value[0].ReferenceNumber);
        Assert.Equal("Pelanggan Tradisional", result.Value[0].PartyName);
        Assert.Equal(300_000m, result.Value[0].Amount);

        // Tanpa parameter tanggal
        var allResult = await viewerService.GetAllAsync();
        Assert.True(allResult.IsSuccess);
        Assert.Single(allResult.Value!);

        // User unauthenticated tetap ditolak
        var unauthResult = await CreateService().GetAllAsync();
        Assert.True(unauthResult.IsFailure);
        Assert.Equal(ReadDenied, unauthResult.Error);
    }
}
