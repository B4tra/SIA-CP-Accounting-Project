using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.DTOs.Inventory;
using SIA.Domain.Enums;
using SIA.Infrastructure.Services;
using SIA.Tests.Infrastructure;

namespace SIA.Tests.Services;

public class ProductServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private ProductService CreateService(params string[] roles) => new(_db.CreateContext(), TestUser.InRole(roles));

    private static CreateProductDto NewProduct(string name = "GS Astra NS40", string? sku = "GS-NS40", string? brand = "GS Astra") => new()
    {
        Name = name,
        SKU = sku,
        Brand = brand,
        Category = "Aki Mobil",
        BatteryType = BatteryType.Kering,
        Voltage = "12V",
        Capacity = "35Ah",
        PurchasePrice = 650_000m,
        SellingPrice = 750_000m,
        ReorderPoint = 3,
    };

    private async Task<int> CreateAsStaffAsync(CreateProductDto dto)
    {
        var result = await CreateService(Roles.Staff).CreateAsync(dto);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!.Id;
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    public async Task Create_AdminAtauStaff_BerhasilDenganStokNol(string role)
    {
        var result = await CreateService(role).CreateAsync(NewProduct());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.Value!.Stock);
        Assert.Equal(750_000m, result.Value.SellingPrice);
        Assert.Equal("GS-NS40", result.Value.SKU);
    }

    [Fact]
    public async Task Create_Viewer_Ditolak()
    {
        var result = await CreateService(Roles.Viewer).CreateAsync(NewProduct());

        Assert.True(result.IsFailure);
        await using var context = _db.CreateContext();
        Assert.False(await context.Products.AnyAsync());
    }

    [Fact]
    public async Task Create_SkuDuplikat_Ditolak()
    {
        await CreateAsStaffAsync(NewProduct());

        var result = await CreateService(Roles.Staff).CreateAsync(NewProduct(name: "Produk Lain"));

        Assert.True(result.IsFailure);
        Assert.Contains("SKU", result.Error);
    }

    [Fact]
    public async Task Create_SkuKosong_BolehBanyak()
    {
        await CreateAsStaffAsync(NewProduct(name: "A", sku: null));
        var result = await CreateService(Roles.Staff).CreateAsync(NewProduct(name: "B", sku: "  "));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value!.SKU);
    }

    [Fact]
    public async Task Create_HargaNegatif_Ditolak()
    {
        var dto = NewProduct();
        dto.SellingPrice = -1m;

        var result = await CreateService(Roles.Staff).CreateAsync(dto);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Update_MengubahData_StokTidakBerubah()
    {
        var id = await CreateAsStaffAsync(NewProduct());
        await using (var context = _db.CreateContext())
        {
            // Simulasi stok hasil transaksi
            (await context.Products.SingleAsync(p => p.Id == id)).Stock = 12;
            await context.SaveChangesAsync();
        }

        var result = await CreateService(Roles.Staff).UpdateAsync(new UpdateProductDto
        {
            Id = id, Name = "GS Astra NS40 Hybrid", SKU = "GS-NS40", PurchasePrice = 700_000m, SellingPrice = 800_000m,
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("GS Astra NS40 Hybrid", result.Value!.Name);
        Assert.Equal(800_000m, result.Value.SellingPrice);
        Assert.Equal(12, result.Value.Stock);
    }

    [Fact]
    public async Task Update_SkuMilikProdukLain_Ditolak()
    {
        await CreateAsStaffAsync(NewProduct(name: "A", sku: "SKU-A"));
        var idB = await CreateAsStaffAsync(NewProduct(name: "B", sku: "SKU-B"));

        var result = await CreateService(Roles.Staff).UpdateAsync(new UpdateProductDto { Id = idB, Name = "B", SKU = "SKU-A" });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Update_Viewer_Ditolak()
    {
        var id = await CreateAsStaffAsync(NewProduct());

        var result = await CreateService(Roles.Viewer).UpdateAsync(new UpdateProductDto { Id = id, Name = "Diubah" });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Delete_SoftDelete_DanViewerDitolak()
    {
        var id = await CreateAsStaffAsync(NewProduct());

        Assert.True((await CreateService(Roles.Viewer).DeleteAsync(id)).IsFailure);
        Assert.True((await CreateService(Roles.Staff).DeleteAsync(id)).IsSuccess);

        await using var context = _db.CreateContext();
        Assert.False(await context.Products.AnyAsync());
        Assert.True((await context.Products.IgnoreQueryFilters().SingleAsync()).IsDeleted);
    }

    [Theory]
    [InlineData("ns40", 1)]      // nama (tidak peka huruf besar/kecil)
    [InlineData("YUASA", 1)]     // merek
    [InlineData("GS-", 1)]       // SKU
    [InlineData("", 3)]          // tanpa filter
    [InlineData("tidakada", 0)]
    public async Task GetAll_Pencarian_NamaSkuMerek(string search, int expected)
    {
        await CreateAsStaffAsync(NewProduct());
        await CreateAsStaffAsync(NewProduct(name: "Yuasa YTZ5S", sku: "YU-5S", brand: "Yuasa"));
        await CreateAsStaffAsync(NewProduct(name: "Incoe NS60", sku: "IN-60", brand: "Incoe"));

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
