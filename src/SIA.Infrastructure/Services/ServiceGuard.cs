using System.ComponentModel.DataAnnotations;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;

namespace SIA.Infrastructure.Services;

/// <summary>
/// Helper bersama untuk service: pengecekan hak akses berbasis <see cref="Policies.RoleMap"/>
/// (otorisasi ditegakkan di sisi server) dan validasi DataAnnotations pada DTO.
/// </summary>
internal static class ServiceGuard
{
    public const string ReadDenied = "Anda tidak memiliki hak akses untuk melihat data ini.";
    public const string WriteDenied = "Anda tidak memiliki hak akses untuk mengubah data ini.";

    /// <summary>True jika user memiliki salah satu role yang dipetakan ke policy.</summary>
    public static bool Has(ICurrentUserService user, string policy) =>
        Policies.RoleMap.TryGetValue(policy, out var roles) && roles.Any(user.IsInRole);

    /// <summary>Validasi DataAnnotations; mengembalikan daftar pesan error (kosong jika valid).</summary>
    public static IReadOnlyList<string> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results
            .Select(r => r.ErrorMessage ?? "Data tidak valid.")
            .ToList();
    }

    /// <summary>Trim string; string kosong dikembalikan sebagai null.</summary>
    public static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
