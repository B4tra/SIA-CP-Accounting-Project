using Microsoft.EntityFrameworkCore;
using Moq;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.DTOs.System;
using SIA.Domain.Entities.System;
using SIA.Infrastructure.Identity;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;
using Xunit;

namespace SIA.Tests.Services;

public class AuditServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private AuditService CreateService(ICurrentUserService currentUser) =>
        new(_db.CreateContext(), currentUser);

    private AuditService CreateService(params string[] roles) =>
        CreateService(TestUser.InRole(roles));

    private async Task SeedUsersAsync(params (string Id, string UserName, string FullName)[] users)
    {
        await using var context = _db.CreateContext();
        foreach (var u in users)
        {
            context.Users.Add(new ApplicationUser
            {
                Id = u.Id,
                UserName = u.UserName,
                Email = u.UserName,
                FullName = u.FullName,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
        }
        await context.SaveChangesAsync();
    }

    private async Task SeedLogsAsync(params AuditLog[] logs)
    {
        await using var context = _db.CreateContext();
        context.AuditLogs.AddRange(logs);
        await context.SaveChangesAsync();
    }

    private const string ReadDenied = "Anda tidak memiliki hak akses untuk melihat data ini.";

    #region 1. Authorization Tests

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Viewer)]
    public async Task GetLogsAsync_NonAdmin_ReturnsFailureReadDenied(string role)
    {
        var service = CreateService(role);

        var result = await service.GetLogsAsync(new AuditLogFilterDto());

        Assert.True(result.IsFailure);
        Assert.Equal(ReadDenied, result.Error);
    }

    [Fact]
    public async Task GetLogsAsync_Unauthenticated_ReturnsFailureReadDenied()
    {
        var unauthenticatedUser = TestUser.InRole(); // No roles -> IsAuthenticated = false
        var service = CreateService(unauthenticatedUser);

        var result = await service.GetLogsAsync(new AuditLogFilterDto());

        Assert.True(result.IsFailure);
        Assert.Equal(ReadDenied, result.Error);
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Viewer)]
    public async Task GetByEntityAsync_NonAdmin_ReturnsFailureReadDenied(string role)
    {
        var service = CreateService(role);

        var result = await service.GetByEntityAsync("User", "u1");

        Assert.True(result.IsFailure);
        Assert.Equal(ReadDenied, result.Error);
    }

    #endregion

    #region 2. LogAsync Persistence Tests

    [Fact]
    public async Task LogAsync_ValidParameters_PersistsAuditLogWithCurrentUserAndIp()
    {
        var userMock = new Mock<ICurrentUserService>();
        userMock.SetupGet(u => u.IsAuthenticated).Returns(true);
        userMock.SetupGet(u => u.UserId).Returns("admin-123");
        userMock.SetupGet(u => u.UserName).Returns("admin@sia.local");
        userMock.SetupGet(u => u.IpAddress).Returns("192.168.1.15");
        userMock.Setup(u => u.IsInRole(Roles.Admin)).Returns(true);

        var service = CreateService(userMock.Object);

        await service.LogAsync(
            action: "Create",
            entityName: "User",
            entityId: "u-99",
            oldValues: null,
            newValues: "{\"FullName\":\"Budi\",\"Role\":\"Staff\"}");

        await using var verifyContext = _db.CreateContext();
        var log = await verifyContext.AuditLogs.SingleOrDefaultAsync(l => l.EntityId == "u-99");

        Assert.NotNull(log);
        Assert.Equal("Create", log.Action);
        Assert.Equal("User", log.EntityName);
        Assert.Equal("u-99", log.EntityId);
        Assert.Null(log.OldValues);
        Assert.Equal("{\"FullName\":\"Budi\",\"Role\":\"Staff\"}", log.NewValues);
        Assert.Equal("admin-123", log.UserId);
        Assert.Equal("192.168.1.15", log.IpAddress);
        Assert.True((DateTime.UtcNow - log.Timestamp).TotalSeconds < 10);
    }

    [Fact]
    public async Task LogAsync_WithoutUser_PersistsAuditLogWithNullUserIdAndIp()
    {
        var anonymousUser = new Mock<ICurrentUserService>();
        anonymousUser.SetupGet(u => u.IsAuthenticated).Returns(false);
        anonymousUser.SetupGet(u => u.UserId).Returns((string?)null);
        anonymousUser.SetupGet(u => u.IpAddress).Returns((string?)null);

        var service = CreateService(anonymousUser.Object);

        await service.LogAsync("SystemEvent", "System", "0", null, "{\"Event\":\"Startup\"}");

        await using var verifyContext = _db.CreateContext();
        var log = await verifyContext.AuditLogs.SingleOrDefaultAsync(l => l.Action == "SystemEvent");

        Assert.NotNull(log);
        Assert.Null(log.UserId);
        Assert.Null(log.IpAddress);
    }

    [Fact]
    public async Task LogAsync_WithOldAndNewValues_PersistsBothJsonPayloads()
    {
        var service = CreateService(Roles.Admin);

        await service.LogAsync(
            action: "Update",
            entityName: "User",
            entityId: "u-1",
            oldValues: "{\"IsActive\":true}",
            newValues: "{\"IsActive\":false}");

        await using var verifyContext = _db.CreateContext();
        var log = await verifyContext.AuditLogs.SingleAsync(l => l.EntityId == "u-1");

        Assert.Equal("{\"IsActive\":true}", log.OldValues);
        Assert.Equal("{\"IsActive\":false}", log.NewValues);
    }

    #endregion

    #region 3. GetLogsAsync Filter Tests

    [Fact]
    public async Task GetLogsAsync_FilterDateFrom_ReturnsOnlyLogsOnOrAfterDate()
    {
        await SeedLogsAsync(
            new AuditLog { Action = "A1", EntityName = "E", EntityId = "1", Timestamp = new DateTime(2026, 1, 1, 10, 0, 0) },
            new AuditLog { Action = "A2", EntityName = "E", EntityId = "2", Timestamp = new DateTime(2026, 1, 15, 10, 0, 0) },
            new AuditLog { Action = "A3", EntityName = "E", EntityId = "3", Timestamp = new DateTime(2026, 1, 20, 10, 0, 0) });

        var service = CreateService(Roles.Admin);
        var result = await service.GetLogsAsync(new AuditLogFilterDto
        {
            DateFrom = new DateTime(2026, 1, 15),
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Contains(result.Value, l => l.Action == "A2");
        Assert.Contains(result.Value, l => l.Action == "A3");
        Assert.DoesNotContain(result.Value, l => l.Action == "A1");
    }

    [Fact]
    public async Task GetLogsAsync_FilterDateTo_ReturnsOnlyLogsOnOrBeforeDate()
    {
        await SeedLogsAsync(
            new AuditLog { Action = "A1", EntityName = "E", EntityId = "1", Timestamp = new DateTime(2026, 1, 5, 10, 0, 0) },
            new AuditLog { Action = "A2", EntityName = "E", EntityId = "2", Timestamp = new DateTime(2026, 1, 10, 23, 0, 0) },
            new AuditLog { Action = "A3", EntityName = "E", EntityId = "3", Timestamp = new DateTime(2026, 1, 12, 1, 0, 0) });

        var service = CreateService(Roles.Admin);
        var result = await service.GetLogsAsync(new AuditLogFilterDto
        {
            DateTo = new DateTime(2026, 1, 10),
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Contains(result.Value, l => l.Action == "A1");
        Assert.Contains(result.Value, l => l.Action == "A2");
        Assert.DoesNotContain(result.Value, l => l.Action == "A3");
    }

    [Fact]
    public async Task GetLogsAsync_FilterDateRange_ReturnsLogsWithinRange()
    {
        await SeedLogsAsync(
            new AuditLog { Action = "A1", EntityName = "E", EntityId = "1", Timestamp = new DateTime(2026, 1, 1) },
            new AuditLog { Action = "A2", EntityName = "E", EntityId = "2", Timestamp = new DateTime(2026, 1, 10) },
            new AuditLog { Action = "A3", EntityName = "E", EntityId = "3", Timestamp = new DateTime(2026, 1, 20) });

        var service = CreateService(Roles.Admin);
        var result = await service.GetLogsAsync(new AuditLogFilterDto
        {
            DateFrom = new DateTime(2026, 1, 5),
            DateTo = new DateTime(2026, 1, 15),
        });

        Assert.True(result.IsSuccess);
        var single = Assert.Single(result.Value!);
        Assert.Equal("A2", single.Action);
    }

    [Fact]
    public async Task GetLogsAsync_FilterAction_ReturnsExactActionMatches()
    {
        await SeedLogsAsync(
            new AuditLog { Action = "Create", EntityName = "User", EntityId = "1", Timestamp = DateTime.UtcNow },
            new AuditLog { Action = "Update", EntityName = "User", EntityId = "2", Timestamp = DateTime.UtcNow },
            new AuditLog { Action = "ResetPassword", EntityName = "User", EntityId = "3", Timestamp = DateTime.UtcNow });

        var service = CreateService(Roles.Admin);
        var result = await service.GetLogsAsync(new AuditLogFilterDto { Action = "Update" });

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal("2", item.EntityId);
    }

    [Fact]
    public async Task GetLogsAsync_FilterEntityName_ReturnsExactEntityMatches()
    {
        await SeedLogsAsync(
            new AuditLog { Action = "Create", EntityName = "User", EntityId = "1", Timestamp = DateTime.UtcNow },
            new AuditLog { Action = "Create", EntityName = "Sale", EntityId = "101", Timestamp = DateTime.UtcNow });

        var service = CreateService(Roles.Admin);
        var result = await service.GetLogsAsync(new AuditLogFilterDto { EntityName = "User" });

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal("User", item.EntityName);
    }

    [Fact]
    public async Task GetLogsAsync_FilterEntityId_ReturnsExactEntityIdMatches()
    {
        await SeedLogsAsync(
            new AuditLog { Action = "Update", EntityName = "User", EntityId = "user-1", Timestamp = DateTime.UtcNow },
            new AuditLog { Action = "Update", EntityName = "User", EntityId = "user-2", Timestamp = DateTime.UtcNow });

        var service = CreateService(Roles.Admin);
        var result = await service.GetLogsAsync(new AuditLogFilterDto { EntityId = "user-2" });

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal("user-2", item.EntityId);
    }

    [Fact]
    public async Task GetLogsAsync_FilterUserId_ReturnsOnlyLogsForTargetUser()
    {
        await SeedLogsAsync(
            new AuditLog { Action = "Create", EntityName = "User", EntityId = "1", UserId = "admin-1", Timestamp = DateTime.UtcNow },
            new AuditLog { Action = "Create", EntityName = "User", EntityId = "2", UserId = "admin-2", Timestamp = DateTime.UtcNow });

        var service = CreateService(Roles.Admin);
        var result = await service.GetLogsAsync(new AuditLogFilterDto { UserId = "admin-1" });

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal("admin-1", item.UserId);
    }

    [Fact]
    public async Task GetLogsAsync_MaxResults_LimitsReturnedLogsCount()
    {
        var logs = Enumerable.Range(1, 10).Select(i => new AuditLog
        {
            Action = $"Action-{i}",
            EntityName = "User",
            EntityId = $"{i}",
            Timestamp = DateTime.UtcNow.AddMinutes(i),
        }).ToArray();

        await SeedLogsAsync(logs);

        var service = CreateService(Roles.Admin);
        var result = await service.GetLogsAsync(new AuditLogFilterDto { MaxResults = 3 });

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        // Terurut menurun (Action-10, Action-9, Action-8)
        Assert.Equal("Action-10", result.Value[0].Action);
        Assert.Equal("Action-9", result.Value[1].Action);
        Assert.Equal("Action-8", result.Value[2].Action);
    }

    [Fact]
    public async Task GetLogsAsync_OrdersDescendingByTimestampAndId()
    {
        var t1 = new DateTime(2026, 1, 1, 10, 0, 0);
        var t2 = new DateTime(2026, 1, 2, 10, 0, 0);

        await SeedLogsAsync(
            new AuditLog { Action = "First", EntityName = "E", EntityId = "1", Timestamp = t1 },
            new AuditLog { Action = "Second", EntityName = "E", EntityId = "2", Timestamp = t2 });

        var service = CreateService(Roles.Admin);
        var result = await service.GetLogsAsync(new AuditLogFilterDto());

        Assert.True(result.IsSuccess);
        Assert.Equal("Second", result.Value![0].Action);
        Assert.Equal("First", result.Value[1].Action);
    }

    #endregion

    #region 4. UserName Resolution Tests

    [Fact]
    public async Task GetLogsAsync_ResolvesUserNameFromUserStore()
    {
        await SeedUsersAsync(
            ("u-admin", "admin@sia.local", "Administrator"),
            ("u-staff", "staff@sia.local", "Staf Toko"));

        await SeedLogsAsync(
            new AuditLog { Action = "Act1", EntityName = "E", EntityId = "1", UserId = "u-admin", Timestamp = DateTime.UtcNow },
            new AuditLog { Action = "Act2", EntityName = "E", EntityId = "2", UserId = "u-staff", Timestamp = DateTime.UtcNow },
            new AuditLog { Action = "Act3", EntityName = "E", EntityId = "3", UserId = "u-deleted", Timestamp = DateTime.UtcNow },
            new AuditLog { Action = "Act4", EntityName = "E", EntityId = "4", UserId = null, Timestamp = DateTime.UtcNow });

        var service = CreateService(Roles.Admin);
        var result = await service.GetLogsAsync(new AuditLogFilterDto());

        Assert.True(result.IsSuccess);
        var map = result.Value!.ToDictionary(l => l.Action, l => l.UserName);

        Assert.Equal("admin@sia.local", map["Act1"]);
        Assert.Equal("staff@sia.local", map["Act2"]);
        Assert.Null(map["Act3"]); // Pengguna tidak ada di tabel Users -> null
        Assert.Null(map["Act4"]); // UserId null -> null
    }

    #endregion

    #region 5. GetByEntityAsync Tests

    [Fact]
    public async Task GetByEntityAsync_Admin_ReturnsMatchingLogsDescendingWithUserName()
    {
        await SeedUsersAsync(("u-admin", "admin@sia.local", "Administrator"));

        await SeedLogsAsync(
            new AuditLog { Action = "Create", EntityName = "User", EntityId = "target-1", UserId = "u-admin", Timestamp = new DateTime(2026, 1, 1) },
            new AuditLog { Action = "Update", EntityName = "User", EntityId = "target-1", UserId = "u-admin", Timestamp = new DateTime(2026, 1, 5) },
            new AuditLog { Action = "Create", EntityName = "User", EntityId = "target-2", UserId = "u-admin", Timestamp = new DateTime(2026, 1, 2) },
            new AuditLog { Action = "Create", EntityName = "Sale", EntityId = "target-1", UserId = "u-admin", Timestamp = new DateTime(2026, 1, 3) });

        var service = CreateService(Roles.Admin);
        var result = await service.GetByEntityAsync("User", "target-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal("Update", result.Value[0].Action);
        Assert.Equal("Create", result.Value[1].Action);
        Assert.All(result.Value, l => Assert.Equal("admin@sia.local", l.UserName));
    }

    [Fact]
    public async Task GetByEntityAsync_NonExistentEntity_ReturnsEmptyList()
    {
        var service = CreateService(Roles.Admin);

        var result = await service.GetByEntityAsync("NonExistent", "999");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    #endregion
}
