namespace SIA.Domain.Enums;

public enum StockMovementType
{
    /// <summary>Masuk dari pembelian.</summary>
    In,

    /// <summary>Keluar dari penjualan.</summary>
    Out,

    /// <summary>Masuk dari retur penjualan.</summary>
    ReturnIn,

    /// <summary>Keluar dari retur pembelian.</summary>
    ReturnOut
}
