using SIA.Application.Common.Models;
using SIA.Application.DTOs.System;

namespace SIA.Application.Common.Interfaces;

/// <summary>
/// Audit log. Perubahan entity dicatat otomatis oleh SaveChangesInterceptor;
/// <see cref="LogAsync"/> dipakai untuk event non-entity (mis. login/logout).
/// Pembacaan log hanya untuk Admin.
/// </summary>
public interface IAuditService
{
    /// <summary>Mencatat event audit manual. UserId dan IpAddress diambil dari ICurrentUserService.</summary>
    Task LogAsync(
        string action,
        string entityName,
        string entityId,
        string? oldValues = null,
        string? newValues = null,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<AuditLogDto>>> GetLogsAsync(
        AuditLogFilterDto filter,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<AuditLogDto>>> GetByEntityAsync(
        string entityName,
        string entityId,
        CancellationToken cancellationToken = default);
}
