using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.DTOs.System;
using SIA.Domain.Entities.Accounting;
using SIA.Domain.Entities.System;
using SIA.Domain.Enums;
using SIA.Infrastructure.Data;
using SIA.Infrastructure.Data.Seed;
using SIA.Infrastructure.Identity;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;
using SIA.Tests.Services;
using Xunit;

namespace SIA.Tests.Challenger;

/// <summary>
/// Empirical Challenger Verification Suite for Admin Phase UI & Backend Logic:
/// - AuditLogViewer JSON parsing, malformed payloads, non-object JSON, edge cases.
/// - TransactionNumberService.Render & live preview exception handling.
/// - RecalculateBalances accounting integrity and soft-delete exclusion.
/// - UserManagement self-protection, SecurityStamp invalidation, and DTO validation.
/// </summary>
public sealed class AdminChallengerTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly ServiceProvider _sp;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly Mock<IAuditService> _auditMock = new();

    public AdminChallengerTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<AppDbContext>(_ => _db.CreateContext());

        services.AddIdentityCore<ApplicationUser>(opts =>
        {
            opts.User.RequireUniqueEmail = true;
            opts.SignIn.RequireConfirmedAccount = false;
            opts.Password.RequiredLength = 8;
            opts.Password.RequireDigit = true;
            opts.Password.RequireLowercase = true;
            opts.Password.RequireUppercase = true;
            opts.Password.RequireNonAlphanumeric = false;
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>();

        _sp = services.BuildServiceProvider();
        _userManager = _sp.GetRequiredService<UserManager<ApplicationUser>>();
        _roleManager = _sp.GetRequiredService<RoleManager<IdentityRole>>();

        IdentitySeeder.SeedRolesAsync(_roleManager, NullLogger.Instance).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _db.Dispose();
    }

    private static ICurrentUserService MockUser(string role, string userId, string userName = "admin@sia.local")
    {
        var mock = new Mock<ICurrentUserService>();
        mock.SetupGet(u => u.IsAuthenticated).Returns(true);
        mock.SetupGet(u => u.UserId).Returns(userId);
        mock.SetupGet(u => u.UserName).Returns(userName);
        mock.Setup(u => u.IsInRole(It.IsAny<string>())).Returns<string>(r => r == role);
        return mock.Object;
    }

    #region 1. AuditLogViewer JSON Diff Parsing Stress Tests

    private class PropertyDiff
    {
        public string Property { get; set; } = string.Empty;
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public bool IsChanged => !string.Equals(OldValue, NewValue, StringComparison.Ordinal);
    }

    /// <summary>
    /// Exact implementation from AuditLogViewer.razor (lines 225-293)
    /// </summary>
    private static List<PropertyDiff> ParseDiff(string? oldJson, string? newJson)
    {
        var diffs = new Dictionary<string, PropertyDiff>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(oldJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(oldJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        diffs[prop.Name] = new PropertyDiff
                        {
                            Property = prop.Name,
                            OldValue = FormatJsonElement(prop.Value),
                        };
                    }
                }
            }
            catch
            {
                // Format bukan JSON valid atau skalar
            }
        }

        if (!string.IsNullOrWhiteSpace(newJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(newJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (diffs.TryGetValue(prop.Name, out var existing))
                        {
                            existing.NewValue = FormatJsonElement(prop.Value);
                        }
                        else
                        {
                            diffs[prop.Name] = new PropertyDiff
                            {
                                Property = prop.Name,
                                NewValue = FormatJsonElement(prop.Value),
                            };
                        }
                    }
                }
            }
            catch
            {
                // Format bukan JSON valid atau skalar
            }
        }

        return diffs.Values.OrderBy(d => d.Property).ToList();
    }

    private static string FormatJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => "-",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.String => element.GetString() ?? "-",
        _ => element.GetRawText(),
    };

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   \t\n")]
    [InlineData(null, "")]
    [InlineData("", null)]
    public void ParseDiff_NullOrEmptyStrings_ReturnsEmptyListWithoutException(string? oldJson, string? newJson)
    {
        var diffs = ParseDiff(oldJson, newJson);
        Assert.NotNull(diffs);
        Assert.Empty(diffs);
    }

    [Theory]
    [InlineData("{", "{")]
    [InlineData("invalid json", "not a json string")]
    [InlineData("{foo: 123}", "{bar: 456}")] // unquoted keys
    [InlineData("{'name': 'value'}", "{'name': 'val2'}")] // single quotes
    [InlineData("{\"key\": undefined}", "{\"key\": NaN}")]
    [InlineData("{\"unclosed\": \"value", "}")]
    [InlineData("<<<xml>data</xml>", "---yaml: true")]
    [InlineData("{\"trailingComma\": 1,}", "{\"trailingComma\": 2,}")]
    public void ParseDiff_MalformedJson_HandlesGracefullyWithoutThrowing(string oldJson, string newJson)
    {
        var diffs = ParseDiff(oldJson, newJson);
        Assert.NotNull(diffs);
        Assert.Empty(diffs);
    }

    [Theory]
    [InlineData("12345", "67890")] // numbers
    [InlineData("\"just a string\"", "\"another string\"")] // strings
    [InlineData("true", "false")] // booleans
    [InlineData("null", "null")] // json nulls
    [InlineData("[1, 2, 3]", "[4, 5, 6]")] // array of primitives
    [InlineData("[{\"id\": 1}]", "[{\"id\": 2}]")] // array of objects
    public void ParseDiff_NonObjectJson_SkipsEnumerateAndReturnsEmptyWithoutThrowing(string oldJson, string newJson)
    {
        var diffs = ParseDiff(oldJson, newJson);
        Assert.NotNull(diffs);
        Assert.Empty(diffs);
    }

    [Fact]
    public void ParseDiff_ValidObjectDiff_CalculatesSideBySideAndChangedFlags()
    {
        var oldJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["FullName"] = "Budi Santoso",
            ["Email"] = "budi@sia.local",
            ["Role"] = "Staff",
            ["IsActive"] = true,
            ["PhoneNumber"] = null,
            ["Metadata"] = new { Department = "Gudang" },
            ["Tags"] = new[] { "tag1", "tag2" }
        });

        var newJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["FullName"] = "Budi Santoso, S.Kom",
            ["Email"] = "budi@sia.local",
            ["Role"] = "Admin",
            ["IsActive"] = false,
            ["PhoneNumber"] = "08123456789",
            ["Metadata"] = new { Department = "Manajemen" },
            ["Tags"] = new[] { "tag1", "tag3" }
        });

        var diffs = ParseDiff(oldJson, newJson);

        Assert.Equal(7, diffs.Count);

        // FullName changed
        var fullNameDiff = diffs.Single(d => d.Property == "FullName");
        Assert.Equal("Budi Santoso", fullNameDiff.OldValue);
        Assert.Equal("Budi Santoso, S.Kom", fullNameDiff.NewValue);
        Assert.True(fullNameDiff.IsChanged);

        // Email unchanged
        var emailDiff = diffs.Single(d => d.Property == "Email");
        Assert.Equal("budi@sia.local", emailDiff.OldValue);
        Assert.Equal("budi@sia.local", emailDiff.NewValue);
        Assert.False(emailDiff.IsChanged);

        // Role changed
        var roleDiff = diffs.Single(d => d.Property == "Role");
        Assert.Equal("Staff", roleDiff.OldValue);
        Assert.Equal("Admin", roleDiff.NewValue);
        Assert.True(roleDiff.IsChanged);

        // IsActive changed
        var activeDiff = diffs.Single(d => d.Property == "IsActive");
        Assert.Equal("true", activeDiff.OldValue);
        Assert.Equal("false", activeDiff.NewValue);
        Assert.True(activeDiff.IsChanged);

        // PhoneNumber from null to string
        var phoneDiff = diffs.Single(d => d.Property == "PhoneNumber");
        Assert.Equal("-", phoneDiff.OldValue);
        Assert.Equal("08123456789", phoneDiff.NewValue);
        Assert.True(phoneDiff.IsChanged);

        // Complex types serialized as raw text
        var metaDiff = diffs.Single(d => d.Property == "Metadata");
        Assert.Contains("Gudang", metaDiff.OldValue);
        Assert.Contains("Manajemen", metaDiff.NewValue);
        Assert.True(metaDiff.IsChanged);
    }

    [Fact]
    public void ParseDiff_CaseInsensitiveKeyMatching_MergesKeysCorrectly()
    {
        var oldJson = "{\"UserName\": \"admin\", \"Role\": \"Staff\"}";
        var newJson = "{\"username\": \"admin\", \"role\": \"Admin\"}";

        var diffs = ParseDiff(oldJson, newJson);

        Assert.Equal(2, diffs.Count);
        var roleDiff = diffs.Single(d => string.Equals(d.Property, "Role", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Staff", roleDiff.OldValue);
        Assert.Equal("Admin", roleDiff.NewValue);
        Assert.True(roleDiff.IsChanged);
    }

    [Fact]
    public void ParseDiff_FuzzTesting_RandomGarbageStrings_NeverThrows()
    {
        var rnd = new Random(42);
        for (int i = 0; i < 200; i++)
        {
            var len = rnd.Next(1, 100);
            var chars = new char[len];
            for (int j = 0; j < len; j++)
            {
                chars[j] = (char)rnd.Next(0, 255);
            }
            var garbage = new string(chars);

            var ex = Record.Exception(() => ParseDiff(garbage, garbage));
            Assert.Null(ex);
        }
    }

    #endregion

    #region 2. Transaction Number Preview & Render Stress Tests

    private static string RenderPreview(string? format, string? prefix, int year, int sequence)
    {
        try
        {
            return TransactionNumberService.Render(
                string.IsNullOrWhiteSpace(format) ? "{Prefix}-{Year}-{Seq:0000}" : format,
                prefix ?? string.Empty,
                year,
                sequence);
        }
        catch
        {
            return "(Format belum valid)";
        }
    }

    [Theory]
    [InlineData("{Prefix}-{Year}-{Seq:0000}", "TX", 2026, 1, "TX-2026-0001")]
    [InlineData("{Prefix}/{Year}/{Seq:000000}", "INV", 2026, 42, "INV/2026/000042")]
    [InlineData("{Year}-{Prefix}-{Seq}", "SO", 2026, 999, "2026-SO-999")]
    [InlineData("CONSTANT-PREFIX-{Seq:000}", "", 2026, 5, "CONSTANT-PREFIX-005")]
    [InlineData(null, "PO", 2026, 1, "PO-2026-0001")]
    [InlineData("", "PO", 2026, 1, "PO-2026-0001")]
    [InlineData("   ", "PO", 2026, 1, "PO-2026-0001")]
    public void RenderPreview_ValidCustomFormats_ProducesExpectedOutput(string? format, string? prefix, int year, int sequence, string expected)
    {
        var result = RenderPreview(format, prefix, year, sequence);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("{Prefix}-{Year}-{Seq:Z}")]
    [InlineData("{Prefix}-{Year}-{Seq:A}")]
    [InlineData("{Seq:Q}")]
    [InlineData("{Seq:Y}")]
    public void RenderPreview_InvalidStandardFormatSpecifiers_CaughtAndReturnsFallbackNotice(string invalidFormat)
    {
        // Direct call throws FormatException due to invalid standard specifier
        Assert.Throws<FormatException>(() => TransactionNumberService.Render(invalidFormat, "TX", 2026, 1));

        // RenderPreview safely catches it and displays fallback
        var preview = RenderPreview(invalidFormat, "TX", 2026, 1);
        Assert.Equal("(Format belum valid)", preview);
    }

    [Theory]
    [InlineData("{Prefix}-{Year}")] // No Seq
    [InlineData("JUST-RAW-TEXT")]
    [InlineData("{0}-{1}-{2}")]
    [InlineData("{UnknownPlaceholder}-{Year}")]
    [InlineData("{Prefix-{Year-{Seq:0000}")] // Unmatched braces
    [InlineData("{{{Seq:0000}}}")]
    [InlineData("{Prefix}-{Year}-{Seq:custom_literal}")]
    public void RenderPreview_WeirdOrLiteralCustomFormats_DoesNotThrow(string weirdFormat)
    {
        var preview = RenderPreview(weirdFormat, "TX", 2026, 1);
        Assert.NotNull(preview);
        Assert.NotEqual("(Format belum valid)", preview);
    }

    [Fact]
    public void RenderPreview_ExtremeBoundaryValues_SurvivesWithoutException()
    {
        var format = "{Prefix}-{Year}-{Seq:0000}";
        var p1 = RenderPreview(format, "LONG_PREFIX_1234567890", 9999, int.MaxValue);
        Assert.Contains("LONG_PREFIX_1234567890", p1);
        Assert.Contains("9999", p1);
        Assert.Contains(int.MaxValue.ToString(CultureInfo.InvariantCulture), p1);

        var p2 = RenderPreview(format, "", 1, 0);
        Assert.Equal("-1-0000", p2);
    }

    #endregion

    #region 3. Account Recalculation Maintenance Stress Tests

    [Fact]
    public async Task RecalculateBalances_MatchesAccountingInvariantsAcrossMultipleTransactions()
    {
        using var ctx = _db.CreateContext();
        var adminUser = TestUser.InRole(Roles.Admin);
        var accountService = new AccountService(ctx, adminUser);

        // Create seed user for FK
        var user = new ApplicationUser
        {
            Id = "admin-test-recalc",
            UserName = "admin.recalc@sia.local",
            Email = "admin.recalc@sia.local",
            FullName = "Admin Recalc",
            IsActive = true
        };
        ctx.Users.Add(user);

        // Create asset and revenue accounts
        var assetAcc = new Account
        {
            Code = "1110-STRESS",
            Name = "Kas Stress Test",
            Category = AccountCategory.AsetLancar,
            NormalBalance = NormalBalance.Debit,
            Balance = 9999999m // intentionally corrupted balance
        };
        var revAcc = new Account
        {
            Code = "4110-STRESS",
            Name = "Pendapatan Stress Test",
            Category = AccountCategory.Pendapatan,
            NormalBalance = NormalBalance.Credit,
            Balance = -500000m // intentionally corrupted balance
        };
        ctx.Accounts.AddRange(assetAcc, revAcc);
        await ctx.SaveChangesAsync();

        // Add 2 valid journal entries and 1 journal entry that will be soft-deleted
        var j1 = new JournalEntry
        {
            JournalNumber = "JRN-2026-9001",
            TransactionDate = DateTime.Today,
            Description = "Transaksi Valid 1",
            UserId = user.Id,
            Lines = new List<JournalEntryLine>
            {
                new() { AccountId = assetAcc.Id, Debit = 500_000m, Credit = 0m },
                new() { AccountId = revAcc.Id, Debit = 0m, Credit = 500_000m }
            }
        };

        var j2 = new JournalEntry
        {
            JournalNumber = "JRN-2026-9002",
            TransactionDate = DateTime.Today,
            Description = "Transaksi Valid 2",
            UserId = user.Id,
            Lines = new List<JournalEntryLine>
            {
                new() { AccountId = assetAcc.Id, Debit = 300_000m, Credit = 0m },
                new() { AccountId = revAcc.Id, Debit = 0m, Credit = 300_000m }
            }
        };

        var jDeleted = new JournalEntry
        {
            JournalNumber = "JRN-2026-9003",
            TransactionDate = DateTime.Today,
            Description = "Transaksi Soft-Deleted",
            UserId = user.Id,
            Lines = new List<JournalEntryLine>
            {
                new() { AccountId = assetAcc.Id, Debit = 1_000_000m, Credit = 0m },
                new() { AccountId = revAcc.Id, Debit = 0m, Credit = 1_000_000m }
            }
        };

        ctx.JournalEntries.AddRange(j1, j2, jDeleted);
        await ctx.SaveChangesAsync();

        // Soft delete jDeleted and its lines (exactly as JournalService.DeleteAsync does)
        var now = DateTime.UtcNow;
        jDeleted.IsDeleted = true;
        jDeleted.DeletedAt = now;
        foreach (var line in jDeleted.Lines)
        {
            line.IsDeleted = true;
            line.DeletedAt = now;
        }
        await ctx.SaveChangesAsync();

        // Perform recalculation
        var result = await accountService.RecalculateBalancesAsync();
        Assert.True(result.IsSuccess);

        // Verify balances match exactly active journals (excluding soft-deleted jDeleted):
        // Asset: Debit normal balance = 500_000 + 300_000 = 800_000m
        // Revenue: Credit normal balance = 500_000 + 300_000 = 800_000m
        var updatedAsset = await ctx.Accounts.FindAsync(assetAcc.Id);
        var updatedRev = await ctx.Accounts.FindAsync(revAcc.Id);

        Assert.Equal(800_000m, updatedAsset!.Balance);
        Assert.Equal(800_000m, updatedRev!.Balance);
    }

    #endregion

    #region 4. User Self-Protection & Invariant Stress Tests

    [Fact]
    public async Task UserService_SelfProtection_AdminCannotDeactivateSelf()
    {
        var adminUser = new ApplicationUser
        {
            Id = "admin-self-1",
            UserName = "admin.self@sia.local",
            Email = "admin.self@sia.local",
            FullName = "Admin Utama",
            IsActive = true
        };
        await _userManager.CreateAsync(adminUser, "P@ssword123");
        await _userManager.AddToRoleAsync(adminUser, Roles.Admin);

        var currentAdmin = MockUser(Roles.Admin, adminUser.Id);
        var userService = new UserService(_userManager, currentAdmin, _auditMock.Object);

        // Adversarial attempt: SetActiveAsync(adminId, false)
        var result = await userService.SetActiveAsync(adminUser.Id, false);

        Assert.False(result.IsSuccess);
        Assert.Equal("Anda tidak dapat menonaktifkan akun Anda sendiri.", result.Error);

        var refreshed = await _userManager.FindByIdAsync(adminUser.Id);
        Assert.True(refreshed!.IsActive);
    }

    [Fact]
    public async Task UserService_SelfProtection_AdminCannotDemoteSelf()
    {
        var adminUser = new ApplicationUser
        {
            Id = "admin-self-2",
            UserName = "admin.self2@sia.local",
            Email = "admin.self2@sia.local",
            FullName = "Admin Utama 2",
            IsActive = true
        };
        await _userManager.CreateAsync(adminUser, "P@ssword123");
        await _userManager.AddToRoleAsync(adminUser, Roles.Admin);

        var currentAdmin = MockUser(Roles.Admin, adminUser.Id);
        var userService = new UserService(_userManager, currentAdmin, _auditMock.Object);

        // Adversarial attempt: demote to Staff
        var updateDto = new UpdateUserDto
        {
            Id = adminUser.Id,
            FullName = "Admin Berubah",
            Email = adminUser.Email,
            Role = Roles.Staff, // Demotion!
            IsActive = true
        };

        var result = await userService.UpdateAsync(updateDto);

        Assert.False(result.IsSuccess);
        Assert.Equal("Anda tidak dapat mengubah role akun Anda sendiri.", result.Error);

        var roles = await _userManager.GetRolesAsync(adminUser);
        Assert.Contains(Roles.Admin, roles);
        Assert.DoesNotContain(Roles.Staff, roles);
    }

    [Fact]
    public async Task UserService_SelfUpdate_AdminCanUpdateOwnProfileInfoWhilePreservingAdminRole()
    {
        var adminUser = new ApplicationUser
        {
            Id = "admin-self-3",
            UserName = "admin.self3@sia.local",
            Email = "admin.self3@sia.local",
            FullName = "Admin Asli",
            PhoneNumber = "081111111",
            IsActive = true
        };
        await _userManager.CreateAsync(adminUser, "P@ssword123");
        await _userManager.AddToRoleAsync(adminUser, Roles.Admin);

        var currentAdmin = MockUser(Roles.Admin, adminUser.Id);
        var userService = new UserService(_userManager, currentAdmin, _auditMock.Object);

        var updateDto = new UpdateUserDto
        {
            Id = adminUser.Id,
            FullName = "Admin Telah Diperbarui",
            Email = adminUser.Email,
            PhoneNumber = "082222222",
            Role = Roles.Admin, // Preserves Admin
            IsActive = true     // Preserves Active
        };

        var result = await userService.UpdateAsync(updateDto);

        Assert.True(result.IsSuccess);
        Assert.Equal("Admin Telah Diperbarui", result.Value!.FullName);
        Assert.Equal("082222222", result.Value.PhoneNumber);
    }

    [Fact]
    public async Task UserService_DeactivationAndResetPassword_UpdatesSecurityStamp()
    {
        var targetUser = new ApplicationUser
        {
            Id = "target-user-stamp",
            UserName = "target.stamp@sia.local",
            Email = "target.stamp@sia.local",
            FullName = "Target Pengguna",
            IsActive = true
        };
        await _userManager.CreateAsync(targetUser, "InitialP@ss1");
        await _userManager.AddToRoleAsync(targetUser, Roles.Staff);

        var stampInitial = targetUser.SecurityStamp;
        Assert.NotNull(stampInitial);

        var currentAdmin = MockUser(Roles.Admin, "admin-caller-stamp");
        var userService = new UserService(_userManager, currentAdmin, _auditMock.Object);

        // 1. Deactivate updates stamp
        var deactResult = await userService.SetActiveAsync(targetUser.Id, false);
        Assert.True(deactResult.IsSuccess);

        var userAfterDeact = await _userManager.FindByIdAsync(targetUser.Id);
        var stampAfterDeact = userAfterDeact!.SecurityStamp;
        Assert.NotEqual(stampInitial, stampAfterDeact);

        // 2. Reset password updates stamp again
        var resetDto = new ResetUserPasswordDto
        {
            Id = targetUser.Id,
            NewPassword = "NewP@ssword123",
            ConfirmPassword = "NewP@ssword123"
        };
        var resetResult = await userService.ResetPasswordAsync(resetDto);
        Assert.True(resetResult.IsSuccess);

        var userAfterReset = await _userManager.FindByIdAsync(targetUser.Id);
        var stampAfterReset = userAfterReset!.SecurityStamp;
        Assert.NotEqual(stampAfterDeact, stampAfterReset);
    }

    #endregion

    #region 5. DataAnnotations Validation Tests

    [Fact]
    public void CreateUserDto_Validation_RejectsInvalidPayloads()
    {
        var dto = new CreateUserDto
        {
            FullName = "",
            Email = "invalid-email-format",
            Password = "short",
            ConfirmPassword = "mismatched",
            Role = ""
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains("FullName"));
        Assert.Contains(results, r => r.MemberNames.Contains("Email"));
        Assert.Contains(results, r => r.MemberNames.Contains("Password"));
        Assert.Contains(results, r => r.MemberNames.Contains("ConfirmPassword"));
        Assert.Contains(results, r => r.MemberNames.Contains("Role"));
    }

    [Fact]
    public void ResetUserPasswordDto_Validation_RejectsShortOrMismatchedPassword()
    {
        var dto = new ResetUserPasswordDto
        {
            Id = "user-1",
            NewPassword = "P@1", // < 8 chars
            ConfirmPassword = "P@2"
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains("NewPassword"));
        Assert.Contains(results, r => r.MemberNames.Contains("ConfirmPassword"));
    }

    #endregion
}
