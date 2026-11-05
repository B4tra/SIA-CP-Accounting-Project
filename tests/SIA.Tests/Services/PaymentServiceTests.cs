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
}
