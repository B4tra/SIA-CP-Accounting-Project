using System.Globalization;
using SIA.Domain.Enums;

namespace SIA.Web.Formatting;

/// <summary>Format tampilan UI: Rupiah (<c>Rp 1.250.000</c>, culture id-ID) dan label enum Bahasa Indonesia.</summary>
public static class Display
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("id-ID");

    /// <summary>Contoh: 1250000 → "Rp 1.250.000"; -5000 → "-Rp 5.000".</summary>
    public static string Rupiah(decimal value)
    {
        var text = Math.Abs(value).ToString("N0", Culture);
        return value < 0 ? $"-Rp {text}" : $"Rp {text}";
    }

    public static string Label(AccountCategory category) => category switch
    {
        AccountCategory.AsetLancar => "Aset Lancar",
        AccountCategory.AsetTetap => "Aset Tetap",
        AccountCategory.LiabilitasJangkaPendek => "Liabilitas Jangka Pendek",
        AccountCategory.LiabilitasJangkaPanjang => "Liabilitas Jangka Panjang",
        AccountCategory.Ekuitas => "Ekuitas",
        AccountCategory.Pendapatan => "Pendapatan",
        AccountCategory.PendapatanLainLain => "Pendapatan Lain-lain",
        AccountCategory.BebanPokokPenjualan => "Beban Pokok Penjualan",
        AccountCategory.BebanOperasional => "Beban Operasional",
        AccountCategory.BebanLainLain => "Beban Lain-lain",
        _ => category.ToString(),
    };

    public static string Label(NormalBalance normalBalance) => normalBalance switch
    {
        NormalBalance.Debit => "Debit",
        NormalBalance.Credit => "Kredit",
        _ => normalBalance.ToString(),
    };

    public static string Label(BatteryType? batteryType) => batteryType switch
    {
        null => "-",
        BatteryType.Basah => "Basah",
        BatteryType.Kering => "Kering (MF)",
        BatteryType.AGM => "AGM",
        BatteryType.GEL => "GEL",
        _ => batteryType.ToString()!,
    };

    /// <summary>Contoh: 14/10/2026; null → "-".</summary>
    public static string Date(DateTime? value) =>
        value is null ? "-" : value.Value.ToString("dd/MM/yyyy", Culture);

    public static string Label(PaymentStatus status) => status switch
    {
        PaymentStatus.Unpaid => "Belum Bayar",
        PaymentStatus.PartiallyPaid => "Dibayar Sebagian",
        PaymentStatus.Paid => "Lunas",
        _ => status.ToString(),
    };

    public static string Label(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Tunai",
        PaymentMethod.Transfer => "Transfer",
        _ => method.ToString(),
    };
}
