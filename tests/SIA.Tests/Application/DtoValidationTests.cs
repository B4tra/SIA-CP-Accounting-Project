using System.ComponentModel.DataAnnotations;
using SIA.Application.DTOs.Accounting;
using SIA.Application.DTOs.Inventory;
using SIA.Application.DTOs.Payments;
using SIA.Application.DTOs.Sales;
using SIA.Application.DTOs.System;
using SIA.Domain.Enums;

namespace SIA.Tests.Application;

public class DtoValidationTests
{
    private static IList<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(model, serviceProvider: null, items: null);
        Validator.TryValidateObject(model, context, validationResults, validateAllProperties: true);
        return validationResults;
    }

    [Fact]
    public void SaveCustomerDto_ValidModel_HasNoErrors()
    {
        var dto = new SaveCustomerDto
        {
            Name = "Budi Santoso",
            Phone = "081234567890",
            Address = "Jl. Merdeka No. 10"
        };

        var errors = ValidateModel(dto);

        Assert.Empty(errors);
    }

    [Fact]
    public void SaveCustomerDto_MissingName_HasValidationError()
    {
        var dto = new SaveCustomerDto
        {
            Name = string.Empty
        };

        var errors = ValidateModel(dto);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(SaveCustomerDto.Name)));
    }

    [Fact]
    public void CreateAccountDto_MissingCodeAndName_HasValidationErrors()
    {
        var dto = new CreateAccountDto
        {
            Code = string.Empty,
            Name = string.Empty
        };

        var errors = ValidateModel(dto);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateAccountDto.Code)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateAccountDto.Name)));
    }

    [Fact]
    public void SaveProductDto_MissingName_HasValidationError()
    {
        var dto = new SaveProductDto
        {
            Name = string.Empty
        };

        var errors = ValidateModel(dto);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(SaveProductDto.Name)));
    }

    [Fact]
    public void CreateSaleItemDto_QuantityZero_HasValidationError()
    {
        var item = new CreateSaleItemDto
        {
            ProductId = 1,
            Quantity = 0,
            UnitPrice = 100000
        };

        var errors = ValidateModel(item);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreateSaleItemDto.Quantity)));
    }

    [Fact]
    public void CreatePaymentDto_AmountZero_HasValidationError()
    {
        var payment = new CreatePaymentDto
        {
            PaymentDate = DateTime.Today,
            PaymentMethod = PaymentMethod.Cash,
            ReferenceId = 1,
            ReferenceType = ReferenceType.Sale,
            Amount = 0
        };

        var errors = ValidateModel(payment);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CreatePaymentDto.Amount)));
    }

    [Fact]
    public void UpdateTransactionNumberConfigDto_MissingPrefixAndFormat_HasValidationErrors()
    {
        var dto = new UpdateTransactionNumberConfigDto
        {
            Prefix = string.Empty,
            Format = string.Empty
        };

        var errors = ValidateModel(dto);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(UpdateTransactionNumberConfigDto.Prefix)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(UpdateTransactionNumberConfigDto.Format)));
    }
}
