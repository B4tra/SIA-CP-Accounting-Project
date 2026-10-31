using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.System;
using SIA.Domain.Entities.System;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;

namespace SIA.Tests.Services;

public class TransactionNumberServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedConfigAsync(string type = TransactionTypes.Sale, string prefix = "INV",
        string format = "{Prefix}-{Year}-{Seq:0000}", int sequence = 0, int year = 2026)
    {
        await using var context = _db.CreateContext();
        var config = new TransactionNumberConfig
        {
            TransactionType = type,
            Prefix = prefix,
            Format = format,
            CurrentSequence = sequence,
            Year = year,
        };
        context.TransactionNumberConfigs.Add(config);
        await context.SaveChangesAsync();
        return config.Id;
    }

    private TransactionNumberService CreateService(params string[] roles) =>
        new(_db.CreateContext(), TestUser.InRole(roles));

    [Fact]
    public async Task Generate_NomorPertama_Format_INV_2026_0001()
    {
        await SeedConfigAsync();
        var service = CreateService();

        var number = await service.GenerateAsync(TransactionTypes.Sale, new DateTime(2026, 3, 15));

        Assert.Equal("INV-2026-0001", number);
    }

    [Fact]
    public async Task Generate_AutoIncrement_DanTersimpan()
    {
        await SeedConfigAsync();

        Assert.Equal("INV-2026-0001", await CreateService().GenerateAsync(TransactionTypes.Sale, new DateTime(2026, 1, 1)));
        Assert.Equal("INV-2026-0002", await CreateService().GenerateAsync(TransactionTypes.Sale, new DateTime(2026, 1, 2)));

        var service = CreateService();
        Assert.Equal("INV-2026-0003", await service.GenerateAsync(TransactionTypes.Sale, new DateTime(2026, 1, 3)));
        Assert.Equal("INV-2026-0004", await service.GenerateAsync(TransactionTypes.Sale, new DateTime(2026, 1, 3)));

        await using var context = _db.CreateContext();
        Assert.Equal(4, (await context.TransactionNumberConfigs.SingleAsync()).CurrentSequence);
    }

    [Fact]
    public async Task Generate_GantiTahun_SequenceDireset()
    {
        await SeedConfigAsync(sequence: 57, year: 2026);

        var number = await CreateService().GenerateAsync(TransactionTypes.Sale, new DateTime(2027, 1, 2));

        Assert.Equal("INV-2027-0001", number);
        await using var context = _db.CreateContext();
        var config = await context.TransactionNumberConfigs.SingleAsync();
        Assert.Equal(2027, config.Year);
        Assert.Equal(1, config.CurrentSequence);
    }

    [Fact]
    public async Task Generate_TahunLebihLamaDariKonfigurasi_DitolakAgarTidakGanda()
    {
        await SeedConfigAsync(sequence: 3, year: 2026);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().GenerateAsync(TransactionTypes.Sale, new DateTime(2025, 12, 31)));
    }

    [Fact]
    public async Task Generate_FormatKustom()
    {
        await SeedConfigAsync(type: TransactionTypes.PurchaseOrder, prefix: "PO", format: "{Year}/{Prefix}/{Seq:000000}", sequence: 41);

        var number = await CreateService().GenerateAsync(TransactionTypes.PurchaseOrder, new DateTime(2026, 6, 1));

        Assert.Equal("2026/PO/000042", number);
    }

    [Fact]
    public async Task Generate_KonfigurasiTidakAda_MelemparInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().GenerateAsync(TransactionTypes.Payment, new DateTime(2026, 1, 1)));
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Viewer)]
    public async Task GetConfigs_NonAdmin_Ditolak(string role)
    {
        await SeedConfigAsync();

        var result = await CreateService(role).GetConfigsAsync();

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetConfigs_Admin_MengembalikanDaftar()
    {
        await SeedConfigAsync();

        var result = await CreateService(Roles.Admin).GetConfigsAsync();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("INV", result.Value![0].Prefix);
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Viewer)]
    public async Task UpdateConfig_NonAdmin_Ditolak(string role)
    {
        var id = await SeedConfigAsync();

        var result = await CreateService(role).UpdateConfigAsync(new UpdateTransactionNumberConfigDto
        {
            Id = id, Prefix = "FKT", Format = "{Prefix}-{Year}-{Seq:0000}",
        });

        Assert.True(result.IsFailure);
        await using var context = _db.CreateContext();
        Assert.Equal("INV", (await context.TransactionNumberConfigs.SingleAsync()).Prefix);
    }

    [Fact]
    public async Task UpdateConfig_FormatTanpaSeq_Ditolak()
    {
        var id = await SeedConfigAsync();

        var result = await CreateService(Roles.Admin).UpdateConfigAsync(new UpdateTransactionNumberConfigDto
        {
            Id = id, Prefix = "INV", Format = "{Prefix}-{Year}",
        });

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Contains("{Seq}"));
    }

    [Fact]
    public async Task UpdateConfig_Admin_BerhasilDanDipakaiGenerate()
    {
        var id = await SeedConfigAsync();

        var result = await CreateService(Roles.Admin).UpdateConfigAsync(new UpdateTransactionNumberConfigDto
        {
            Id = id, Prefix = "FKT", Format = "{Prefix}.{Year}.{Seq:000}",
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("FKT.2026.001", await CreateService().GenerateAsync(TransactionTypes.Sale, new DateTime(2026, 2, 1)));
    }

    [Theory]
    [InlineData("{Prefix}-{Year}-{Seq:0000}", "INV", 2026, 7, "INV-2026-0007")]
    [InlineData("{Prefix}{Seq}", "PAY", 2026, 12, "PAY12")]
    [InlineData("{Prefix}-{Year}-{Seq:0000}", "JRN", 2026, 12345, "JRN-2026-12345")]
    public void Render_MenggantiPlaceholder(string format, string prefix, int year, int seq, string expected)
    {
        Assert.Equal(expected, TransactionNumberService.Render(format, prefix, year, seq));
    }
}
