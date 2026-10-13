using SIA.Application.Common.Models;

namespace SIA.Tests.Application;

public class ResultTests
{
    [Fact]
    public void Success_IsSuccessTrue_TanpaError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Empty(result.Errors);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_SatuPesan_MenyimpanPesan()
    {
        var result = Result.Failure("Stok tidak mencukupi.");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("Stok tidak mencukupi.", result.Error);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Failure_BanyakPesan_MenyimpanSemuaPesan()
    {
        var result = Result.Failure(new[] { "Error 1", "Error 2" });

        Assert.True(result.IsFailure);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal("Error 1", result.Error);
    }

    [Fact]
    public void Failure_DaftarKosong_MelemparArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Result.Failure(Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => Result<int>.Failure(Array.Empty<string>()));
    }

    [Fact]
    public void GenericSuccess_MenyimpanNilai()
    {
        var result = Result<decimal>.Success(1_250_000m);

        Assert.True(result.IsSuccess);
        Assert.Equal(1_250_000m, result.Value);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void GenericFailure_NilaiDefault_DanPesanTersimpan()
    {
        var result = Result<string>.Failure("Data tidak ditemukan.");

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal("Data tidak ditemukan.", result.Error);
    }

    [Fact]
    public void GenericFailure_BanyakPesan_MenyimpanSemuaPesan()
    {
        var result = Result<int>.Failure(new List<string> { "A", "B", "C" });

        Assert.True(result.IsFailure);
        Assert.Equal(new[] { "A", "B", "C" }, result.Errors);
    }
}
