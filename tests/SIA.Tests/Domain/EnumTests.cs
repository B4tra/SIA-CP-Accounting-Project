using SIA.Domain.Enums;

namespace SIA.Tests.Domain;

/// <summary>
/// Mengunci nilai int setiap enum karena enum disimpan sebagai int di database.
/// Mengubah urutan enum akan merusak data yang sudah tersimpan.
/// </summary>
public class EnumTests
{
    [Theory]
    [InlineData(AccountCategory.AsetLancar, 0)]
    [InlineData(AccountCategory.AsetTetap, 1)]
    [InlineData(AccountCategory.LiabilitasJangkaPendek, 2)]
    [InlineData(AccountCategory.LiabilitasJangkaPanjang, 3)]
    [InlineData(AccountCategory.Ekuitas, 4)]
    [InlineData(AccountCategory.Pendapatan, 5)]
    [InlineData(AccountCategory.PendapatanLainLain, 6)]
    [InlineData(AccountCategory.BebanPokokPenjualan, 7)]
    [InlineData(AccountCategory.BebanOperasional, 8)]
    [InlineData(AccountCategory.BebanLainLain, 9)]
    public void AccountCategory_HasExpectedValues(AccountCategory value, int expected)
        => Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(NormalBalance.Debit, 0)]
    [InlineData(NormalBalance.Credit, 1)]
    public void NormalBalance_HasExpectedValues(NormalBalance value, int expected)
        => Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(PaymentMethod.Cash, 0)]
    [InlineData(PaymentMethod.Transfer, 1)]
    public void PaymentMethod_HasExpectedValues(PaymentMethod value, int expected)
        => Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(PaymentStatus.Unpaid, 0)]
    [InlineData(PaymentStatus.PartiallyPaid, 1)]
    [InlineData(PaymentStatus.Paid, 2)]
    public void PaymentStatus_HasExpectedValues(PaymentStatus value, int expected)
        => Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft, 0)]
    [InlineData(PurchaseOrderStatus.Approved, 1)]
    [InlineData(PurchaseOrderStatus.Received, 2)]
    [InlineData(PurchaseOrderStatus.Cancelled, 3)]
    public void PurchaseOrderStatus_HasExpectedValues(PurchaseOrderStatus value, int expected)
        => Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(StockMovementType.In, 0)]
    [InlineData(StockMovementType.Out, 1)]
    [InlineData(StockMovementType.ReturnIn, 2)]
    [InlineData(StockMovementType.ReturnOut, 3)]
    public void StockMovementType_HasExpectedValues(StockMovementType value, int expected)
        => Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(BatteryType.Basah, 0)]
    [InlineData(BatteryType.Kering, 1)]
    [InlineData(BatteryType.AGM, 2)]
    [InlineData(BatteryType.GEL, 3)]
    public void BatteryType_HasExpectedValues(BatteryType value, int expected)
        => Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(ReferenceType.Sale, 0)]
    [InlineData(ReferenceType.PurchaseOrder, 1)]
    public void ReferenceType_HasExpectedValues(ReferenceType value, int expected)
        => Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(typeof(AccountCategory), 10)]
    [InlineData(typeof(NormalBalance), 2)]
    [InlineData(typeof(PaymentMethod), 2)]
    [InlineData(typeof(PaymentStatus), 3)]
    [InlineData(typeof(PurchaseOrderStatus), 4)]
    [InlineData(typeof(StockMovementType), 4)]
    [InlineData(typeof(BatteryType), 4)]
    [InlineData(typeof(ReferenceType), 2)]
    public void Enum_HasExpectedMemberCount(Type enumType, int expectedCount)
        => Assert.Equal(expectedCount, Enum.GetValues(enumType).Length);
}
