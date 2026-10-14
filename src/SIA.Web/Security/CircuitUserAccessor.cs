using System.Security.Claims;

namespace SIA.Web.Security;

/// <summary>
/// Menyimpan <see cref="ClaimsPrincipal"/> milik circuit Blazor Server yang sedang aktif,
/// karena <c>HttpContext</c> tidak tersedia setelah koneksi interaktif terbentuk.
/// </summary>
public sealed class CircuitUserAccessor
{
    public ClaimsPrincipal? User { get; set; }
}
