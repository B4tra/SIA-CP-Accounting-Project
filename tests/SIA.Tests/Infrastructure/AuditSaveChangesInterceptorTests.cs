using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using SIA.Application.Common.Interfaces;
using SIA.Domain.Entities.Sales;
using SIA.Domain.Entities.System;
using SIA.Infrastructure.Data.Interceptors;

namespace SIA.Tests.Infrastructure;

public class AuditSaveChangesInterceptorTests : IDisposable
{
    private readonly SqliteTestDatabase _db;

    public AuditSaveChangesInterceptorTests()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(u => u.UserId).Returns("user-123");
        currentUser.SetupGet(u => u.IpAddress).Returns("127.0.0.1");
        _db = new SqliteTestDatabase(currentUser.Object);
    }

    public void Dispose() => _db.Dispose();

    private async Task<List<AuditLog>> GetLogsAsync()
    {
        await using var context = _db.CreateContext();
        return await context.AuditLogs.OrderBy(a => a.Id).ToListAsync();
    }

    [Fact]
    public async Task Create_MencatatAuditDenganIdHasilGenerate_DanUser()
    {
        await using (var context = _db.CreateContext())
        {
            context.Customers.Add(new Customer { Name = "Budi" });
            await context.SaveChangesAsync();
        }

        var log = Assert.Single(await GetLogsAsync());
        Assert.Equal(AuditSaveChangesInterceptor.ActionCreate, log.Action);
        Assert.Equal(nameof(Customer), log.EntityName);
        Assert.NotEqual("0", log.EntityId);
        Assert.Equal("user-123", log.UserId);
        Assert.Equal("127.0.0.1", log.IpAddress);
        Assert.Null(log.OldValues);

        var newValues = JsonDocument.Parse(log.NewValues!).RootElement;
        Assert.Equal("Budi", newValues.GetProperty("Name").GetString());
        Assert.Equal(int.Parse(log.EntityId), newValues.GetProperty("Id").GetInt32());
    }

    [Fact]
    public async Task Update_HanyaMencatatKolomYangBerubah()
    {
        await using (var context = _db.CreateContext())
        {
            var customer = new Customer { Name = "Budi", Phone = "0811" };
            context.Customers.Add(customer);
            await context.SaveChangesAsync();

            customer.Name = "Budi Santoso";
            await context.SaveChangesAsync();
        }

        var log = (await GetLogsAsync()).Last();
        Assert.Equal(AuditSaveChangesInterceptor.ActionUpdate, log.Action);

        var oldValues = JsonDocument.Parse(log.OldValues!).RootElement;
        var newValues = JsonDocument.Parse(log.NewValues!).RootElement;
        Assert.Equal("Budi", oldValues.GetProperty("Name").GetString());
        Assert.Equal("Budi Santoso", newValues.GetProperty("Name").GetString());
        Assert.False(oldValues.TryGetProperty("Phone", out _));
    }

    [Fact]
    public async Task SoftDelete_DicatatSebagaiDelete()
    {
        await using (var context = _db.CreateContext())
        {
            var customer = new Customer { Name = "Budi" };
            context.Customers.Add(customer);
            await context.SaveChangesAsync();

            context.Customers.Remove(customer);
            await context.SaveChangesAsync();
        }

        var logs = await GetLogsAsync();
        Assert.Equal(2, logs.Count);
        var log = logs[1];
        Assert.Equal(AuditSaveChangesInterceptor.ActionDelete, log.Action);
        Assert.Equal(logs[0].EntityId, log.EntityId);
        Assert.Equal("Budi", JsonDocument.Parse(log.OldValues!).RootElement.GetProperty("Name").GetString());
    }

    [Fact]
    public async Task AuditLog_TidakMengauditDirinyaSendiri()
    {
        await using (var context = _db.CreateContext())
        {
            context.Customers.AddRange(new Customer { Name = "A" }, new Customer { Name = "B" });
            await context.SaveChangesAsync();
        }

        var logs = await GetLogsAsync();
        Assert.Equal(2, logs.Count);
        Assert.DoesNotContain(logs, l => l.EntityName == nameof(AuditLog));
    }

    [Fact]
    public async Task TransactionNumberConfig_KenaikanSequenceTidakDicatat_PerubahanPrefixDicatat()
    {
        await using (var context = _db.CreateContext())
        {
            var config = new TransactionNumberConfig
            {
                TransactionType = "Sale", Prefix = "INV", Format = "{Prefix}-{Year}-{Seq:0000}", Year = 2026
            };
            context.TransactionNumberConfigs.Add(config);
            await context.SaveChangesAsync();
            Assert.Empty(await context.AuditLogs.ToListAsync()); // pembuatan tidak dicatat

            config.CurrentSequence = 1;
            await context.SaveChangesAsync();
            Assert.Empty(await context.AuditLogs.ToListAsync()); // sequence tidak dicatat

            config.Prefix = "NOTA";
            config.CurrentSequence = 2;
            await context.SaveChangesAsync();
        }

        var log = Assert.Single(await GetLogsAsync());
        Assert.Equal(AuditSaveChangesInterceptor.ActionUpdate, log.Action);
        Assert.Equal(nameof(TransactionNumberConfig), log.EntityName);
        Assert.Equal("NOTA", JsonDocument.Parse(log.NewValues!).RootElement.GetProperty("Prefix").GetString());
    }

    [Fact]
    public async Task TanpaCurrentUser_UserIdNull()
    {
        using var db = new SqliteTestDatabase();
        await using (var context = db.CreateContext())
        {
            context.Customers.Add(new Customer { Name = "Budi" });
            await context.SaveChangesAsync();
        }

        await using (var context = db.CreateContext())
        {
            var log = await context.AuditLogs.SingleAsync();
            Assert.Null(log.UserId);
            Assert.Null(log.IpAddress);
        }
    }

    [Fact]
    public void SaveChangesSinkron_JugaMencatatAudit()
    {
        using (var context = _db.CreateContext())
        {
            context.Customers.Add(new Customer { Name = "Sinkron" });
            context.SaveChanges();
        }

        using (var context = _db.CreateContext())
        {
            Assert.Single(context.AuditLogs.ToList());
        }
    }
}
