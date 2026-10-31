using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.Purchasing;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;

namespace SIA.Tests.Services;

public class SupplierServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private SupplierService CreateService(params string[] roles) => new(_db.CreateContext(), TestUser.InRole(roles));

    private async Task<int> CreateAsStaffAsync(string name, string? phone = null, string? address = null)
    {
        var result = await CreateService(Roles.Staff).CreateAsync(new CreateSupplierDto { Name = name, Phone = phone, Address = address });
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!.Id;
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    public async Task Create_AdminAtauStaff_Berhasil(string role)
    {
        var result = await CreateService(role).CreateAsync(new CreateSupplierDto { Name = " CV Sumber Aki ", Phone = "0812", Address = " " });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("CV Sumber Aki", result.Value!.Name);
        Assert.Equal("0812", result.Value.Phone);
        Assert.Null(result.Value.Address);
    }

    [Fact]
    public async Task Create_NamaKosong_Ditolak()
    {
        var result = await CreateService(Roles.Staff).CreateAsync(new CreateSupplierDto { Name = "" });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Create_Viewer_Ditolak()
    {
        var result = await CreateService(Roles.Viewer).CreateAsync(new CreateSupplierDto { Name = "CV Sumber Aki" });

        Assert.True(result.IsFailure);
        await using var context = _db.CreateContext();
        Assert.False(await context.Suppliers.AnyAsync());
    }

    [Fact]
    public async Task Update_Berhasil_DanViewerDitolak()
    {
        var id = await CreateAsStaffAsync("CV Sumber Aki");

        var denied = await CreateService(Roles.Viewer).UpdateAsync(new UpdateSupplierDto { Id = id, Name = "Diubah" });
        var ok = await CreateService(Roles.Staff).UpdateAsync(new UpdateSupplierDto { Id = id, Name = "CV Sumber Aki Jaya", Phone = "0813" });

        Assert.True(denied.IsFailure);
        Assert.True(ok.IsSuccess, ok.Error);
        Assert.Equal("CV Sumber Aki Jaya", (await CreateService(Roles.Viewer).GetByIdAsync(id)).Value!.Name);
    }

    [Fact]
    public async Task Update_IdTidakAda_Gagal()
    {
        var result = await CreateService(Roles.Staff).UpdateAsync(new UpdateSupplierDto { Id = 999, Name = "X" });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Delete_SoftDelete_DanViewerDitolak()
    {
        var id = await CreateAsStaffAsync("CV Sumber Aki");

        Assert.True((await CreateService(Roles.Viewer).DeleteAsync(id)).IsFailure);
        Assert.True((await CreateService(Roles.Staff).DeleteAsync(id)).IsSuccess);

        Assert.Empty((await CreateService(Roles.Viewer).GetAllAsync()).Value!);
        Assert.True((await CreateService(Roles.Viewer).GetByIdAsync(id)).IsFailure);
        await using var context = _db.CreateContext();
        Assert.True((await context.Suppliers.IgnoreQueryFilters().SingleAsync()).IsDeleted);
    }

    [Theory]
    [InlineData("sumber", 1)]
    [InlineData("0857", 1)]
    [InlineData(null, 2)]
    [InlineData("zzz", 0)]
    public async Task GetAll_Pencarian_NamaAtauTelepon(string? search, int expected)
    {
        await CreateAsStaffAsync("CV Sumber Aki", "0812111");
        await CreateAsStaffAsync("PT Yuasa Distribusi", "0857222");

        var result = await CreateService(Roles.Viewer).GetAllAsync(search);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value!.Count);
    }

    [Fact]
    public async Task GetAll_TanpaRole_Ditolak()
    {
        Assert.True((await CreateService().GetAllAsync()).IsFailure);
    }
}
