using System.Security.Claims;
using SIA.Application.Common.Constants;
using SIA.Infrastructure.Identity;

namespace SIA.Web.Security;

/// <summary>Helper tampilan identitas pengguna (nama, inisial, label role) untuk UI.</summary>
public static class UserDisplay
{
    public static string DisplayName(ClaimsPrincipal user) =>
        user.FindFirstValue(AppUserClaimsPrincipalFactory.FullNameClaimType)
        ?? user.Identity?.Name
        ?? "Pengguna";

    public static string Initials(ClaimsPrincipal user)
    {
        var parts = DisplayName(user).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
        return string.IsNullOrEmpty(initials) ? "?" : initials;
    }

    public static string RoleLabel(ClaimsPrincipal user)
    {
        if (user.IsInRole(Roles.Admin)) return "Admin (Pemilik)";
        if (user.IsInRole(Roles.Staff)) return "Staf";
        if (user.IsInRole(Roles.Viewer)) return "Viewer (Akuntan)";
        return "Tanpa role";
    }
}
