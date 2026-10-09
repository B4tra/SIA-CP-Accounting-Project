using SIA.Application.Common.Models;

namespace SIA.Tests.Application;

public class ResultTests
{
    [Fact]
    public void Success_ReturnsIsSuccessTrue_AndNullError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_ReturnsIsSuccessFalse_AndCorrectErrorMessage()
    {
        var errorMessage = "Terjadi kesalahan operasi.";
        var result = Result.Failure(errorMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal(errorMessage, result.Error);
    }

    [Fact]
    public void GenericResult_Success_ReturnsValue()
    {
        var expectedValue = 42;
        var result = Result<int>.Success(expectedValue);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Equal(expectedValue, result.Value);
    }

    [Fact]
    public void GenericResult_Failure_ThrowsWhenAccessingValue()
    {
        var errorMessage = "Data tidak ditemukan.";
        var result = Result<string>.Failure(errorMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal(errorMessage, result.Error);

        var ex = Assert.Throws<InvalidOperationException>(() => _ = result.Value);
        Assert.Contains(errorMessage, ex.Message);
    }
}
