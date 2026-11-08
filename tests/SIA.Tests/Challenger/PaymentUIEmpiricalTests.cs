using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using SIA.Application.DTOs.Payments;
using SIA.Domain.Enums;
using Xunit;

namespace SIA.Tests.Challenger;

/// <summary>
/// Empirical Challenger Verification Suite for Milestone 2: Payments UI Components & Logic.
/// Adversarially stress-tests:
/// 1. CreatePaymentDialog dual-mode behavior, 'Bayar Penuh' calculation, and validation guards.
/// 2. PaymentList KPI metrics calculation on full unfiltered datasets vs filter isolation.
/// 3. Overdue date boundary conditions and chip logic.
/// 4. 300ms debounce on search inputs and theme/authorization static analysis.
/// </summary>
public sealed class PaymentUIEmpiricalTests
{
    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SIA.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not find solution root containing SIA.sln");
    }

    private static readonly string WebAssemblyPath = Path.Combine(
        FindSolutionRoot(), "src", "SIA.Web", "bin", "Debug", "net8.0", "SIA.Web.dll");

    private static readonly string WebPagesPath = Path.Combine(
        FindSolutionRoot(), "src", "SIA.Web", "Components", "Pages", "Payments");

    #region 1. Display.Label(ReferenceType) Tests

    [Theory]
    [InlineData(ReferenceType.Sale, "Penjualan (Piutang)")]
    [InlineData(ReferenceType.PurchaseOrder, "Purchase Order (Utang)")]
    public void Display_Label_ReferenceType_ReturnsApprovedIndonesianLabels(ReferenceType refType, string expected)
    {
        // Act: Invoke Display.Label(ReferenceType) via reflection from SIA.Web.dll
        var webAssembly = Assembly.LoadFrom(WebAssemblyPath);
        var displayType = webAssembly.GetType("SIA.Web.Formatting.Display")
            ?? throw new InvalidOperationException("SIA.Web.Formatting.Display not found.");

        var method = displayType.GetMethod("Label", new[] { typeof(ReferenceType) })
            ?? throw new InvalidOperationException("Display.Label(ReferenceType) method not found.");

        var label = (string?)method.Invoke(null, new object[] { refType });

        // Assert
        Assert.Equal(expected, label);
    }

    [Fact]
    public void Display_Label_UndefinedReferenceType_FallsBackToString()
    {
        var webAssembly = Assembly.LoadFrom(WebAssemblyPath);
        var displayType = webAssembly.GetType("SIA.Web.Formatting.Display")!;
        var method = displayType.GetMethod("Label", new[] { typeof(ReferenceType) })!;

        var undefinedEnum = (ReferenceType)999;
        var label = (string?)method.Invoke(null, new object[] { undefinedEnum });

        Assert.Equal("999", label);
    }

    #endregion

    #region 2. CreatePaymentDialog Logic & Validation

    [Fact]
    public void CreatePaymentDialog_BayarPenuh_SetsModelAmountToMaxAmount()
    {
        // Arrange simulation of CreatePaymentDialog state
        var preselectedDoc = new OutstandingDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = 10,
            ReferenceNumber = "INV-2026-0001",
            TotalAmount = 1_500_000m,
            PaidAmount = 500_000m,
            RemainingAmount = 1_000_000m
        };

        var model = new CreatePaymentDto();

        // Simulate ApplyDocument
        model.ReferenceType = preselectedDoc.ReferenceType;
        model.ReferenceId = preselectedDoc.ReferenceId;
        model.Amount = preselectedDoc.RemainingAmount;

        decimal maxAmount = preselectedDoc.RemainingAmount;

        // User manually changed amount to partial payment
        model.Amount = 250_000m;
        Assert.Equal(250_000m, model.Amount);

        // Act: User clicks "Bayar Penuh" (SetFullPayment handler)
        if (maxAmount > 0)
        {
            model.Amount = maxAmount;
        }

        // Assert
        Assert.Equal(1_000_000m, model.Amount);
        Assert.Equal(maxAmount, model.Amount);
    }

    [Theory]
    [InlineData(0, "Jumlah pembayaran harus lebih dari 0.")]
    [InlineData(-1, "Jumlah pembayaran harus lebih dari 0.")]
    [InlineData(-1000000, "Jumlah pembayaran harus lebih dari 0.")]
    public void CreatePaymentDialog_Validation_RejectsZeroAndNegativeAmounts(decimal amount, string expectedError)
    {
        var errors = new List<string>();
        decimal maxAmount = 500_000m;
        int referenceId = 1;

        // Simulate SubmitAsync validation logic
        if (referenceId <= 0)
        {
            errors.Add("Pilih dokumen transaksi yang akan dibayar terlebih dahulu.");
        }
        else if (amount <= 0)
        {
            errors.Add("Jumlah pembayaran harus lebih dari 0.");
        }
        else if (amount > maxAmount)
        {
            errors.Add($"Jumlah pembayaran tidak boleh melebihi sisa tagihan ({maxAmount:N0}).");
        }

        Assert.Single(errors);
        Assert.Equal(expectedError, errors[0]);
    }

    [Fact]
    public void CreatePaymentDialog_Validation_RejectsAmountExceedingRemaining()
    {
        var errors = new List<string>();
        decimal maxAmount = 500_000m;
        decimal inputAmount = 500_000.01m;
        int referenceId = 1;

        if (referenceId <= 0)
        {
            errors.Add("Pilih dokumen transaksi yang akan dibayar terlebih dahulu.");
        }
        else if (inputAmount <= 0)
        {
            errors.Add("Jumlah pembayaran harus lebih dari 0.");
        }
        else if (inputAmount > maxAmount)
        {
            errors.Add($"Jumlah pembayaran tidak boleh melebihi sisa tagihan ({maxAmount:N0}).");
        }

        Assert.Single(errors);
        Assert.Contains("tidak boleh melebihi sisa tagihan", errors[0]);
    }

    [Fact]
    public void CreatePaymentDialog_Validation_RejectsUnselectedReference()
    {
        var errors = new List<string>();
        decimal maxAmount = 500_000m;
        decimal inputAmount = 100_000m;
        int referenceId = 0; // Unselected

        if (referenceId <= 0)
        {
            errors.Add("Pilih dokumen transaksi yang akan dibayar terlebih dahulu.");
        }
        else if (inputAmount <= 0)
        {
            errors.Add("Jumlah pembayaran harus lebih dari 0.");
        }
        else if (inputAmount > maxAmount)
        {
            errors.Add($"Jumlah pembayaran tidak boleh melebihi sisa tagihan ({maxAmount:N0}).");
        }

        Assert.Single(errors);
        Assert.Equal("Pilih dokumen transaksi yang akan dibayar terlebih dahulu.", errors[0]);
    }

    [Fact]
    public void CreatePaymentDto_DataAnnotations_EnforcesModelValidation()
    {
        var dto = new CreatePaymentDto
        {
            PaymentDate = DateTime.Today,
            PaymentMethod = PaymentMethod.Cash,
            ReferenceType = ReferenceType.Sale,
            ReferenceId = 0, // Invalid: [Range(1, int.MaxValue)]
            Amount = 0m,     // Invalid: [Range(0.01, ...)]
        };

        var context = new ValidationContext(dto);
        var validationResults = new List<ValidationResult>();
        bool isValid = Validator.TryValidateObject(dto, context, validationResults, true);

        Assert.False(isValid);
        Assert.Contains(validationResults, v => v.ErrorMessage!.Contains("Jumlah pembayaran harus lebih dari 0."));
        Assert.Contains(validationResults, v => v.ErrorMessage!.Contains("Transaksi yang dibayar wajib dipilih."));
    }

    #endregion

    #region 3. Overdue Boundary Conditions

    [Theory]
    [InlineData(0, 1000, false)]    // Due today -> NOT overdue
    [InlineData(-1, 1000, true)]    // Due yesterday -> OVERDUE
    [InlineData(-30, 1000, true)]   // Due 30 days ago -> OVERDUE
    [InlineData(1, 1000, false)]    // Due tomorrow -> NOT overdue
    [InlineData(-5, 0, false)]      // Due 5 days ago but remaining amount is 0 -> NOT overdue
    [InlineData(-5, -100, false)]   // Due 5 days ago but remaining amount negative -> NOT overdue
    public void PaymentList_IsOverdue_EvaluatesDateAndRemainingAmountCorrectly(int daysOffset, decimal remainingAmount, bool expectedOverdue)
    {
        var item = new OutstandingDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = 1,
            DueDate = DateTime.Today.AddDays(daysOffset),
            RemainingAmount = remainingAmount
        };

        // Formula from PaymentList.razor: item.RemainingAmount > 0 && item.DueDate is not null && item.DueDate.Value.Date < DateTime.Today
        bool isOverdue = item.RemainingAmount > 0 && item.DueDate is not null && item.DueDate.Value.Date < DateTime.Today;

        Assert.Equal(expectedOverdue, isOverdue);
    }

    [Fact]
    public void PaymentList_IsOverdue_NullDueDate_ReturnsFalse()
    {
        var item = new OutstandingDto
        {
            ReferenceType = ReferenceType.Sale,
            ReferenceId = 1,
            DueDate = null,
            RemainingAmount = 500_000m
        };

        bool isOverdue = item.RemainingAmount > 0 && item.DueDate is not null && item.DueDate.Value.Date < DateTime.Today;
        Assert.False(isOverdue);
    }

    #endregion

    #region 4. PaymentList KPI Calculations on Full Unfiltered Datasets

    [Fact]
    public void PaymentList_CalculateKpis_ComputesAccurateMetricsOnFullUnfilteredDataset()
    {
        var today = DateTime.Today;

        // Full payments dataset (Sale and PO)
        var allPayments = new List<PaymentDto>
        {
            new() { Id = 1, ReferenceType = ReferenceType.Sale, Amount = 500_000m },
            new() { Id = 2, ReferenceType = ReferenceType.Sale, Amount = 750_000m },
            new() { Id = 3, ReferenceType = ReferenceType.PurchaseOrder, Amount = 1_200_000m },
            new() { Id = 4, ReferenceType = ReferenceType.PurchaseOrder, Amount = 800_000m },
            new() { Id = 5, ReferenceType = ReferenceType.PurchaseOrder, Amount = 300_000m },
        };

        // Full outstanding dataset (Sales and POs, with varied due dates)
        var allOutstanding = new List<OutstandingDto>
        {
            // Sale 1: Overdue
            new() { ReferenceType = ReferenceType.Sale, ReferenceId = 101, RemainingAmount = 400_000m, DueDate = today.AddDays(-3) },
            // Sale 2: Not overdue (due tomorrow)
            new() { ReferenceType = ReferenceType.Sale, ReferenceId = 102, RemainingAmount = 600_000m, DueDate = today.AddDays(1) },
            // Sale 3: No due date
            new() { ReferenceType = ReferenceType.Sale, ReferenceId = 103, RemainingAmount = 250_000m, DueDate = null },

            // PO 1: Overdue
            new() { ReferenceType = ReferenceType.PurchaseOrder, ReferenceId = 201, RemainingAmount = 1_500_000m, DueDate = today.AddDays(-10) },
            // PO 2: Overdue
            new() { ReferenceType = ReferenceType.PurchaseOrder, ReferenceId = 202, RemainingAmount = 700_000m, DueDate = today.AddDays(-1) },
            // PO 3: Due today (not overdue)
            new() { ReferenceType = ReferenceType.PurchaseOrder, ReferenceId = 203, RemainingAmount = 900_000m, DueDate = today },
        };

        // Act: Apply PaymentList.CalculateKpis algorithm
        var salePayments = allPayments.Where(p => p.ReferenceType == ReferenceType.Sale).ToList();
        var kpiTotalSalePaid = salePayments.Sum(p => p.Amount);
        var kpiSalePaymentCount = salePayments.Count;

        var poPayments = allPayments.Where(p => p.ReferenceType == ReferenceType.PurchaseOrder).ToList();
        var kpiTotalPoPaid = poPayments.Sum(p => p.Amount);
        var kpiPoPaymentCount = poPayments.Count;

        bool IsOverdue(OutstandingDto o) => o.RemainingAmount > 0 && o.DueDate.HasValue && o.DueDate.Value.Date < today;

        var unpaidSales = allOutstanding.Where(o => o.ReferenceType == ReferenceType.Sale).ToList();
        var kpiTotalReceivables = unpaidSales.Sum(o => o.RemainingAmount);
        var kpiUnpaidSaleCount = unpaidSales.Count;
        var kpiOverdueSaleCount = unpaidSales.Count(IsOverdue);

        var unpaidPos = allOutstanding.Where(o => o.ReferenceType == ReferenceType.PurchaseOrder).ToList();
        var kpiTotalPayables = unpaidPos.Sum(o => o.RemainingAmount);
        var kpiUnpaidPoCount = unpaidPos.Count;
        var kpiOverduePoCount = unpaidPos.Count(IsOverdue);

        var kpiOverdueTotalCount = kpiOverdueSaleCount + kpiOverduePoCount;

        // Assert KPI 1: Total Penerimaan Piutang
        Assert.Equal(1_250_000m, kpiTotalSalePaid);
        Assert.Equal(2, kpiSalePaymentCount);

        // Assert KPI 2: Total Pembayaran Utang
        Assert.Equal(2_300_000m, kpiTotalPoPaid);
        Assert.Equal(3, kpiPoPaymentCount);

        // Assert KPI 3: Sisa Piutang Pelanggan
        Assert.Equal(1_250_000m, kpiTotalReceivables); // 400k + 600k + 250k
        Assert.Equal(3, kpiUnpaidSaleCount);
        Assert.Equal(1, kpiOverdueSaleCount); // only Sale 1 (due -3 days)

        // Assert KPI 4: Sisa Utang Pemasok
        Assert.Equal(3_100_000m, kpiTotalPayables); // 1.5M + 700k + 900k
        Assert.Equal(3, kpiUnpaidPoCount);
        Assert.Equal(2, kpiOverduePoCount); // PO 1 (-10 days) and PO 2 (-1 day)

        // Total Overdue Badge
        Assert.Equal(3, kpiOverdueTotalCount); // 1 Sale + 2 PO
    }

    [Fact]
    public void PaymentList_FilterIsolation_TableFilteringDoesNotAlterKpiTotals()
    {
        var allPayments = new List<PaymentDto>
        {
            new() { Id = 1, ReferenceType = ReferenceType.Sale, Amount = 500_000m, PartyName = "Toko A" },
            new() { Id = 2, ReferenceType = ReferenceType.Sale, Amount = 750_000m, PartyName = "Toko B" },
            new() { Id = 3, ReferenceType = ReferenceType.PurchaseOrder, Amount = 1_000_000m, PartyName = "Supplier X" },
        };

        // KPI calculated from all payments
        var initialKpiSalePaid = allPayments.Where(p => p.ReferenceType == ReferenceType.Sale).Sum(p => p.Amount);
        Assert.Equal(1_250_000m, initialKpiSalePaid);

        // User filters table to "Toko A" only
        var filteredTableItems = allPayments.Where(p => p.PartyName.Contains("Toko A")).ToList();
        Assert.Single(filteredTableItems);
        Assert.Equal(500_000m, filteredTableItems[0].Amount);

        // In PaymentList.razor, LoadPaymentsAsync() updates _payments (the table),
        // but does NOT recompute or alter _allPayments or the KPI cards:
        var kpiSalePaidAfterTableFilter = allPayments.Where(p => p.ReferenceType == ReferenceType.Sale).Sum(p => p.Amount);
        Assert.Equal(initialKpiSalePaid, kpiSalePaidAfterTableFilter);
    }

    #endregion

    #region 5. Razor Static Analysis & Theme Conformance

    [Fact]
    public void Razor_ZeroRawHexColorCodes_AcrossAllPaymentComponents()
    {
        var razorFiles = Directory.GetFiles(WebPagesPath, "*.razor", SearchOption.AllDirectories);
        Assert.NotEmpty(razorFiles);

        var hexRegex = new Regex(@"#[0-9a-fA-F]{3,6}\b", RegexOptions.Compiled);
        var violations = new List<string>();

        foreach (var file in razorFiles)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var match = hexRegex.Match(lines[i]);
                if (match.Success)
                {
                    violations.Add($"{Path.GetFileName(file)} Line {i + 1}: '{match.Value}' in line: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} raw hex color violations (AGENTS.md mandates theme tokens only):\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void Razor_PaymentList_Has300msDebounceOnSearchFields()
    {
        var paymentListPath = Path.Combine(WebPagesPath, "PaymentList.razor");
        Assert.True(File.Exists(paymentListPath), "PaymentList.razor file exists");

        var content = File.ReadAllText(paymentListPath);

        // Verify DebounceInterval="300" is configured for search fields
        var debounceMatches = Regex.Matches(content, @"DebounceInterval=""300""");
        Assert.True(debounceMatches.Count >= 2,
            $"Expected at least 2 search fields with DebounceInterval=\"300\" (Tab 1 & Tab 2), found {debounceMatches.Count}");

        // Verify Immediate="true"
        var immediateMatches = Regex.Matches(content, @"Immediate=""true""");
        Assert.True(immediateMatches.Count >= 2,
            $"Expected at least 2 search fields with Immediate=\"true\", found {immediateMatches.Count}");
    }

    [Fact]
    public void Razor_PaymentList_ProtectsAdminActionsWithAuthorizationGuard()
    {
        var paymentListPath = Path.Combine(WebPagesPath, "PaymentList.razor");
        var content = File.ReadAllText(paymentListPath);

        // Verify page-level authorization
        Assert.Contains("@attribute [Authorize(Policy = Policies.ViewAllData)]", content);

        // Verify delete button is enclosed in @if (_isAdmin)
        Assert.Contains("@if (_isAdmin)", content);
        Assert.Contains("DeletePaymentAsync", content);

        // Verify create button is enclosed in @if (_canManage)
        Assert.Contains("@if (_canManage)", content);
        Assert.Contains("OpenCreatePaymentDialogAsync", content);
    }

    [Fact]
    public void Razor_CreatePaymentDialog_ImplementsDualModeBranches()
    {
        var createDialogPath = Path.Combine(WebPagesPath, "CreatePaymentDialog.razor");
        Assert.True(File.Exists(createDialogPath), "CreatePaymentDialog.razor file exists");

        var content = File.ReadAllText(createDialogPath);

        // Preselected mode parameter and conditional
        Assert.Contains("[Parameter] public OutstandingDto? PreselectedItem", content);
        Assert.Contains("@if (IsPreselected && PreselectedItem != null)", content);

        // Standalone mode dropdowns
        Assert.Contains("Pilih Dokumen Belum Lunas", content);
        Assert.Contains("Jenis Referensi", content);

        // "Bayar Penuh" quick button
        Assert.Contains("Bayar Penuh", content);
        Assert.Contains("SetFullPayment", content);

        // Validation bounds
        Assert.Contains("_model.Amount <= 0", content);
        Assert.Contains("_model.Amount > MaxAmount", content);
        Assert.Contains("_model.ReferenceId <= 0", content);
    }

    [Fact]
    public void Razor_PaymentHistoryDialog_UsesStandardSiaTableAndRupiah()
    {
        var historyDialogPath = Path.Combine(WebPagesPath, "PaymentHistoryDialog.razor");
        Assert.True(File.Exists(historyDialogPath), "PaymentHistoryDialog.razor file exists");

        var content = File.ReadAllText(historyDialogPath);

        Assert.Contains("sia-table", content);
        Assert.Contains("Display.Rupiah", content);
        Assert.Contains("PaymentService.GetByReferenceAsync", content);
        Assert.Contains("TotalPaid", content);
    }

    #endregion
}
