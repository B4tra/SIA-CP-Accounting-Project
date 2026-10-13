namespace SIA.Application.Common.Constants;

/// <summary>
/// Kunci <c>TransactionNumberConfig.TransactionType</c> yang dipakai oleh <c>ITransactionNumberService</c>.
/// </summary>
public static class TransactionTypes
{
    public const string Sale = "Sale";
    public const string PurchaseOrder = "PurchaseOrder";
    public const string JournalEntry = "JournalEntry";
    public const string Payment = "Payment";
    public const string SaleReturn = "SaleReturn";
    public const string PurchaseReturn = "PurchaseReturn";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Sale, PurchaseOrder, JournalEntry, Payment, SaleReturn, PurchaseReturn
    };
}
