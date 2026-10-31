using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.Sales;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;

namespace SIA.Tests.Services;

public class CustomerServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private CustomerService CreateService(params string[] roles) => new(_db.CreateContext(), TestUser.InRole(roles));

    private async Task<int> CreateAsStaffAsync(string name, string? phone = null, string? address = null)
    {
        var result = await CreateService(Roles.Staff).CreateAsync(new CreateCustomerDto { Name = name, Phone = phone, Address = address });
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!.Id;
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    public async Task Create_AdminAtauStaff_Berhasil(string role)
    {
        var result = await CreateService(role).CreateAsync(new CreateCustomerDto { Name = " Budi ", Phone = "0812", Address = " " });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Budi", result.Value!.Name);
        Assert.Equal("0812", result.Value.Phone);
        Assert.Null(result.Value.Address);
    }

    [Fact]
    public async Task Create_NamaKosong_Ditolak()
    {
        var result = await CreateService(Roles.Staff).CreateAsync(new CreateCustomerDto { Name = "" });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Create_Viewer_Ditolak()
    {
        var result = await CreateService(Roles.Viewer).CreateAsync(new CreateCustomerDto { Name = "Budi" });

        Assert.True(result.IsFailure);
        await using var context = _db.CreateContext();
        Assert.False(await context.Customers.AnyAsync());
    }

    [Fact]
    public async Task Update_Berhasil_DanViewerDitolak()
    {
        var id = await CreateAsStaffAsync("Budi");

        var denied = await CreateService(Roles.Viewer).UpdateAsync(new UpdateCustomerDto { Id = id, Name = "Diubah" });
        var ok = await CreateService(Roles.Staff).UpdateAsync(new UpdateCustomerDto { Id = id, Name = "Budi Santoso", Phone = "0813" });

        Assert.True(denied.IsFailure);
        Assert.True(ok.IsSuccess, ok.Error);
        Assert.Equal("Budi Santoso", (await CreateService(Roles.Viewer).GetByIdAsync(id)).Value!.Name);
    }

    [Fact]
    public async Task Update_IdTidakAda_Gagal()
    {
        var result = await CreateService(Roles.Staff).UpdateAsync(new UpdateCustomerDto { Id = 999, Name = "X" });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Delete_SoftDelete_DanViewerDitolak()
    {
        var id = await CreateAsStaffAsync("Budi");

        Assert.True((await CreateService(Roles.Viewer).DeleteAsync(id)).IsFailure);
        Assert.True((await CreateService(Roles.Staff).DeleteAsync(id)).IsSuccess);

        Assert.Empty((await CreateService(Roles.Viewer).GetAllAsync()).Value!);
        Assert.True((await CreateService(Roles.Viewer).GetByIdAsync(id)).IsFailure);
        await using var context = _db.CreateContext();
        Assert.True((await context.Customers.IgnoreQueryFilters().SingleAsync()).IsDeleted);
    }

    [Theory]
    [InlineData("budi", 1)]
    [InlineData("0857", 1)]
    [InlineData(null, 2)]
    [InlineData("zzz", 0)]
    public async Task GetAll_Pencarian_NamaAtauTelepon(string? search, int expected)
    {
        await CreateAsStaffAsync("Budi", "0812111");
        await CreateAsStaffAsync("Ani", "0857222");

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
