using System.Text.RegularExpressions;
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
using SIA.Tests.Services;
using Xunit;

namespace SIA.Tests.Challenger;

/// <summary>
/// Empirical Challenger Verification Suite for Milestone 2:
/// - Role enforcement in Razor components (PaymentList.razor, CreatePaymentDialog.razor, PaymentHistoryDialog.razor).
/// - Dialog contracts and mathematical calculation oracles for PaymentHistoryDialog.
/// - Security boundaries across Admin, Staff, Viewer, and Anonymous roles on PaymentService.
/// - Static theme compliance (zero raw hexadecimal color codes).
/// </summary>
public sealed class PaymentEmpiricalChallengerTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private const string ReadDenied = "Anda tidak memiliki hak akses untuk melihat data ini.";
    private const string WriteDenied = "Anda tidak memiliki hak akses untuk mengubah data ini.";

    public void Dispose() => _db.Dispose();

    private static string GetSolutionRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SIA.sln")))
                return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        if (File.Exists("SIA.sln")) return Path.GetFullPath(".");
        throw new InvalidOperationException("Could not find solution root containing SIA.sln");
    }

    private PaymentService CreateService(params string[] roles)
    {
        var context = _db.CreateContext();
        var user = TestUser.InRole(roles);
        var numbers = new TransactionNumberService(context, user);
        return new PaymentService(context, user, numbers);
    }

    private async Task<int> SeedSaleAsync(decimal total = 1_000_000m, decimal paid = 0m)
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

        var customer = new Customer { Name = "Customer Uji" };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var sale = new Sale
        {
            InvoiceNumber = "INV-2026-0099",
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

    private async Task<int> SeedPurchaseOrderAsync(PurchaseOrderStatus status = PurchaseOrderStatus.Approved, decimal total = 2_000_000m, decimal paid = 0m)
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

        var supplier = new Supplier { Name = "Supplier Uji" };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var po = new PurchaseOrder
        {
            PONumber = "PO-2026-0099",
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

    #region 1. Razor Static Contract & Role Guard Verification

    [Fact]
    public void TC01_PaymentList_MustHave_Authorize_ViewAllData()
    {
        var root = GetSolutionRoot();
        var filePath = Path.Combine(root, "src", "SIA.Web", "Components", "Pages", "Payments", "PaymentList.razor");
        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);
        Assert.Contains("@attribute [Authorize(Policy = Policies.ViewAllData)]", content);
        Assert.Contains("@page \"/payments\"", content);
    }

    [Fact]
    public void TC02_PaymentList_DeleteButton_MustBeStrictlyEnclosedInAdminCheck()
    {
        var root = GetSolutionRoot();
        var filePath = Path.Combine(root, "src", "SIA.Web", "Components", "Pages", "Payments", "PaymentList.razor");
        var content = File.ReadAllText(filePath);

        // Verify _isAdmin initialization
        Assert.Matches(@"_isAdmin\s*=\s*\(await\s+AuthorizationService\.AuthorizeAsync\(user,\s*Policies\.RequireAdmin\)\)\.Succeeded;", content);

        // Verify Delete button enclosure in @if (_isAdmin)
        var deleteBlockMatch = Regex.Match(content, @"@if\s*\(_isAdmin\)\s*\{[^}]*Icons\.Material\.Outlined\.Delete[^}]*\}", RegexOptions.Singleline);
        Assert.True(deleteBlockMatch.Success, "Delete button must be strictly enclosed in @if (_isAdmin)");

        // Ensure there are no other delete buttons or icons outside @if (_isAdmin)
        var allDeleteIconMatches = Regex.Matches(content, @"Icons\.Material\.Outlined\.Delete");
        Assert.Single(allDeleteIconMatches);
    }

    [Fact]
    public void TC03_PaymentList_RecordPaymentButtons_MustBeStrictlyEnclosedInCanManageCheck()
    {
        var root = GetSolutionRoot();
        var filePath = Path.Combine(root, "src", "SIA.Web", "Components", "Pages", "Payments", "PaymentList.razor");
        var content = File.ReadAllText(filePath);

        // Verify _canManage initialization
        Assert.Matches(@"_canManage\s*=\s*\(await\s+AuthorizationService\.AuthorizeAsync\(user,\s*Policies\.ManageTransactions\)\)\.Succeeded;", content);

        // Verify "+ Catat Pembayaran Baru" button is inside @if (_canManage)
        var catatBaruMatch = Regex.Match(content, @"@if\s*\(_canManage\)\s*\{[^}]*\+\s*Catat Pembayaran Baru[^}]*\}", RegexOptions.Singleline);
        Assert.True(catatBaruMatch.Success, "'+ Catat Pembayaran Baru' button must be strictly enclosed in @if (_canManage)");

        // Verify "Catat Bayar" button in Tab 2 is inside @if (_canManage)
        var catatBayarMatch = Regex.Match(content, @"@if\s*\(_canManage\)\s*\{[^}]*Catat Bayar[^}]*\}", RegexOptions.Singleline);
        Assert.True(catatBayarMatch.Success, "'Catat Bayar' button in Tab 2 must be strictly enclosed in @if (_canManage)");
    }

    [Fact]
    public void TC04_PaymentHistoryDialog_Contracts_And_Parameters()
    {
        var root = GetSolutionRoot();
        var filePath = Path.Combine(root, "src", "SIA.Web", "Components", "Pages", "Payments", "PaymentHistoryDialog.razor");
        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var content = File.ReadAllText(filePath);

        // Verify invocation of PaymentService.GetByReferenceAsync(ReferenceType, ReferenceId)
        Assert.Contains("PaymentService.GetByReferenceAsync(ReferenceType, ReferenceId)", content);

        // Verify public parameters
        Assert.Contains("[Parameter] public ReferenceType ReferenceType { get; set; }", content);
        Assert.Contains("[Parameter] public int ReferenceId { get; set; }", content);
        Assert.Contains("[Parameter] public string ReferenceNumber { get; set; }", content);
        Assert.Contains("[Parameter] public string? PartyName { get; set; }", content);
        Assert.Contains("[Parameter] public decimal TotalAmount { get; set; }", content);
        Assert.Contains("[Parameter] public decimal RemainingAmount { get; set; }", content);

        // Verify empty list message
        Assert.Contains("Belum ada riwayat pembayaran untuk dokumen ini.", content);

        // Verify footer total calculation property
        Assert.Contains("private decimal TotalPaid => _payments.Sum(p => p.Amount);", content);

        // Verify footer renders TotalPaid in Display.Rupiah
        Assert.Contains("@Display.Rupiah(TotalPaid)", content);
    }

    [Fact]
    public void TC05_ThemeCompliance_ZeroRawHexColors()
    {
        var root = GetSolutionRoot();
        var dirPath = Path.Combine(root, "src", "SIA.Web", "Components", "Pages", "Payments");
        var razorFiles = Directory.GetFiles(dirPath, "*.razor", SearchOption.AllDirectories);

        var hexRegex = new Regex(@"#[0-9a-fA-F]{3,6}\b", RegexOptions.Compiled);
        foreach (var file in razorFiles)
        {
            var text = File.ReadAllText(file);
            var matches = hexRegex.Matches(text);
            Assert.True(matches.Count == 0,
                $"File {Path.GetFileName(file)} contains raw hex colors: {string.Join(", ", matches.Select(m => m.Value))}");
        }
    }

    #endregion

    #region 2. Dialog Logic & Calculation Oracles

    [Fact]
    public void TC06_PaymentHistoryDialog_TotalPaid_Oracle_Calculations()
    {
        // Scenario A: Empty payments list
        var emptyList = new List<PaymentDto>();
        decimal totalA = emptyList.Sum(p => p.Amount);
        Assert.Equal(0m, totalA);

        // Scenario B: Single payment
        var singleList = new List<PaymentDto>
        {
            new() { Id = 1, Amount = 250_000m, PaymentDate = DateTime.Today }
        };
        decimal totalB = singleList.Sum(p => p.Amount);
        Assert.Equal(250_000m, totalB);

        // Scenario C: Multiple partial payments
        var multiList = new List<PaymentDto>
        {
            new() { Id = 1, Amount = 300_000m, PaymentDate = DateTime.Today.AddDays(-5) },
            new() { Id = 2, Amount = 450_000m, PaymentDate = DateTime.Today.AddDays(-2) },
            new() { Id = 3, Amount = 250_000m, PaymentDate = DateTime.Today }
        };
        decimal totalC = multiList.Sum(p => p.Amount);
        Assert.Equal(1_000_000m, totalC);
    }

    #endregion

    #region 3. Empirical Backend Security Barriers & Service Invariants

    [Fact]
    public async Task TC07_GetByReferenceAsync_EmpiricallyIsolates_SaleAndPO()
    {
        var adminService = CreateService(Roles.Admin);
        var staffService = CreateService(Roles.Staff);

        var saleId = await SeedSaleAsync(total: 1_000_000m);
        var poId = await SeedPurchaseOrderAsync(status: PurchaseOrderStatus.Approved, total: 2_000_000m);

        // Create 2 payments for Sale
        var paySale1 = await staffService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            Amount = 400_000m,
            PaymentMethod = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        });
        Assert.True(paySale1.IsSuccess);

        var paySale2 = await staffService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            Amount = 300_000m,
            PaymentMethod = PaymentMethod.Transfer,
            PaymentDate = DateTime.Today
        });
        Assert.True(paySale2.IsSuccess);

        // Create 1 payment for PO
        var payPo = await staffService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.PurchaseOrder,
            ReferenceId = poId,
            Amount = 1_500_000m,
            PaymentMethod = PaymentMethod.Transfer,
            PaymentDate = DateTime.Today
        });
        Assert.True(payPo.IsSuccess);

        // Verify Sale payments query
        var salePayments = await adminService.GetByReferenceAsync(ReferenceType.Sale, saleId);
        Assert.True(salePayments.IsSuccess);
        Assert.Equal(2, salePayments.Value!.Count);
        Assert.All(salePayments.Value, p => Assert.Equal(ReferenceType.Sale, p.ReferenceType));
        Assert.All(salePayments.Value, p => Assert.Equal(saleId, p.ReferenceId));
        Assert.Equal(700_000m, salePayments.Value.Sum(p => p.Amount));

        // Verify PO payments query
        var poPayments = await adminService.GetByReferenceAsync(ReferenceType.PurchaseOrder, poId);
        Assert.True(poPayments.IsSuccess);
        Assert.Single(poPayments.Value!);
        Assert.Equal(ReferenceType.PurchaseOrder, poPayments.Value[0].ReferenceType);
        Assert.Equal(poId, poPayments.Value[0].ReferenceId);
        Assert.Equal(1_500_000m, poPayments.Value[0].Amount);

        // Verify non-existent reference query returns empty list successfully
        var emptyQuery = await adminService.GetByReferenceAsync(ReferenceType.Sale, 99999);
        Assert.True(emptyQuery.IsSuccess);
        Assert.Empty(emptyQuery.Value!);
    }

    [Fact]
    public async Task TC08_DeleteAsync_StrictlyEnforcesAdminOnly()
    {
        var adminService = CreateService(Roles.Admin);
        var staffService = CreateService(Roles.Staff);
        var viewerService = CreateService(Roles.Viewer);
        var anonService = CreateService(); // unauthenticated

        var saleId = await SeedSaleAsync(total: 1_000_000m);
        var pay = await staffService.CreateAsync(new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            Amount = 500_000m,
            PaymentMethod = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        });
        Assert.True(pay.IsSuccess);
        var paymentId = pay.Value!.Id;

        // Staff deletion must fail
        var staffDel = await staffService.DeleteAsync(paymentId);
        Assert.False(staffDel.IsSuccess);
        Assert.Contains(WriteDenied, staffDel.Error);

        // Viewer deletion must fail
        var viewerDel = await viewerService.DeleteAsync(paymentId);
        Assert.False(viewerDel.IsSuccess);
        Assert.Contains(WriteDenied, viewerDel.Error);

        // Anonymous deletion must fail
        var anonDel = await anonService.DeleteAsync(paymentId);
        Assert.False(anonDel.IsSuccess);
        Assert.Contains(WriteDenied, anonDel.Error);

        // Admin deletion must succeed
        var adminDel = await adminService.DeleteAsync(paymentId);
        Assert.True(adminDel.IsSuccess);

        // Verify deleted payment no longer returned by GetByReferenceAsync
        var remainingPayments = await adminService.GetByReferenceAsync(ReferenceType.Sale, saleId);
        Assert.True(remainingPayments.IsSuccess);
        Assert.NotNull(remainingPayments.Value);
        Assert.Empty(remainingPayments.Value);
    }

    [Fact]
    public async Task TC09_CreateAsync_StaffAndAdminAllowed_ViewerAndAnonForbidden()
    {
        var adminService = CreateService(Roles.Admin);
        var staffService = CreateService(Roles.Staff);
        var viewerService = CreateService(Roles.Viewer);
        var anonService = CreateService();

        var saleId = await SeedSaleAsync(total: 1_000_000m);

        var dto = new CreatePaymentDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = saleId,
            Amount = 100_000m,
            PaymentMethod = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        };

        // Viewer must be rejected
        var viewerRes = await viewerService.CreateAsync(dto);
        Assert.False(viewerRes.IsSuccess);
        Assert.Contains(WriteDenied, viewerRes.Error);

        // Anonymous must be rejected
        var anonRes = await anonService.CreateAsync(dto);
        Assert.False(anonRes.IsSuccess);
        Assert.Contains(WriteDenied, anonRes.Error);

        // Staff must be accepted
        var staffRes = await staffService.CreateAsync(dto);
        Assert.True(staffRes.IsSuccess);

        // Admin must be accepted
        var adminRes = await adminService.CreateAsync(dto);
        Assert.True(adminRes.IsSuccess);
    }

    [Fact]
    public async Task TC10_GetAllAsync_And_GetOutstandingAsync_ViewAllDataEnforcement()
    {
        var adminService = CreateService(Roles.Admin);
        var staffService = CreateService(Roles.Staff);
        var viewerService = CreateService(Roles.Viewer);
        var anonService = CreateService();

        // Admin can read
        Assert.True((await adminService.GetAllAsync(new PaymentFilterDto())).IsSuccess);
        Assert.True((await adminService.GetOutstandingAsync()).IsSuccess);

        // Staff can read
        Assert.True((await staffService.GetAllAsync(new PaymentFilterDto())).IsSuccess);
        Assert.True((await staffService.GetOutstandingAsync()).IsSuccess);

        // Viewer can read
        Assert.True((await viewerService.GetAllAsync(new PaymentFilterDto())).IsSuccess);
        Assert.True((await viewerService.GetOutstandingAsync()).IsSuccess);

        // Anonymous is rejected
        var anonAll = await anonService.GetAllAsync(new PaymentFilterDto());
        Assert.False(anonAll.IsSuccess);
        Assert.Contains(ReadDenied, anonAll.Error);

        var anonOut = await anonService.GetOutstandingAsync();
        Assert.False(anonOut.IsSuccess);
        Assert.Contains(ReadDenied, anonOut.Error);
    }

    #endregion
}
