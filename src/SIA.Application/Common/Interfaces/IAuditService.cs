using SIA.Application.Common.Models;
using SIA.Application.DTOs.System;

namespace SIA.Application.Common.Interfaces;

public interface IAuditService
{
    Task<Result> LogAsync(
        string action,
        string entityName,
        string entityId,
        string? oldValues = null,
        string? newValues = null,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<AuditLogDto>>> GetLogsAsync(
        AuditLogFilterDto filter,
        CancellationToken ct = default);
}
