namespace SIA.Application.Common.Interfaces;

/// <summary>
/// Informasi user yang sedang login. Dipakai service untuk menegakkan otorisasi di sisi server
/// dan untuk mengisi UserId pada transaksi serta AuditLog.
/// </summary>
public interface ICurrentUserService
{
    string? UserId { get; }

    string? UserName { get; }

    bool IsAuthenticated { get; }

    string? IpAddress { get; }

    /// <summary>Gunakan konstanta pada <c>SIA.Application.Common.Constants.Roles</c>.</summary>
    bool IsInRole(string role);
}
