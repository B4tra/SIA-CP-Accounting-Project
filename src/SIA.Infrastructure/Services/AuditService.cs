using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.System;
using SIA.Domain.Entities.System;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Services;

/// <summary>
/// Implementasi <see cref="IAuditService"/>.
/// Menangani pencatatan manual (<see cref="LogAsync"/>) serta pembacaan log khusus Admin
/// (<see cref="GetLogsAsync"/> dan <see cref="GetByEntityAsync"/>).
/// </summary>
public class AuditService : IAuditService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public AuditService(AppDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task LogAsync(
        string action,
        string entityName,
        string entityId,
        string? oldValues = null,
        string? newValues = null,
        CancellationToken cancellationToken = default)
    {
        var log = new AuditLog
        {
            UserId = _currentUser.UserId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
            Timestamp = DateTime.UtcNow,
            IpAddress = _currentUser.IpAddress,
        };

        _context.AuditLogs.Add(log);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<IReadOnlyList<AuditLogDto>>> GetLogsAsync(
        AuditLogFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.RequireAdmin))
            return Result<IReadOnlyList<AuditLogDto>>.Failure(ServiceGuard.ReadDenied);

        filter ??= new AuditLogFilterDto();
        var query = _context.AuditLogs.AsNoTracking().AsQueryable();

        if (filter.DateFrom.HasValue)
            query = query.Where(a => a.Timestamp >= filter.DateFrom.Value.Date);

        if (filter.DateTo.HasValue)
        {
            var end = filter.DateTo.Value.Date.AddDays(1);
            query = query.Where(a => a.Timestamp < end);
        }

        var userId = ServiceGuard.Clean(filter.UserId);
        if (userId is not null)
            query = query.Where(a => a.UserId == userId);

        var entityName = ServiceGuard.Clean(filter.EntityName);
        if (entityName is not null)
            query = query.Where(a => a.EntityName == entityName);

        var entityId = ServiceGuard.Clean(filter.EntityId);
        if (entityId is not null)
            query = query.Where(a => a.EntityId == entityId);

        var action = ServiceGuard.Clean(filter.Action);
        if (action is not null)
            query = query.Where(a => a.Action == action);

        var limit = filter.MaxResults > 0 ? filter.MaxResults : 500;
        var logs = await query
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var userIds = logs
            .Select(l => l.UserId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        var userMap = userIds.Count > 0
            ? await _context.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.UserName ?? u.FullName, cancellationToken)
            : new Dictionary<string, string>();

        var dtos = logs.Select(l => new AuditLogDto
        {
            Id = l.Id,
            UserId = l.UserId,
            UserName = l.UserId != null && userMap.TryGetValue(l.UserId, out var name) ? name : null,
            Action = l.Action,
            EntityName = l.EntityName,
            EntityId = l.EntityId,
            OldValues = l.OldValues,
            NewValues = l.NewValues,
            Timestamp = l.Timestamp,
            IpAddress = l.IpAddress,
        }).ToList();

        return Result<IReadOnlyList<AuditLogDto>>.Success(dtos);
    }

    public Task<Result<IReadOnlyList<AuditLogDto>>> GetByEntityAsync(
        string entityName,
        string entityId,
        CancellationToken cancellationToken = default)
    {
        return GetLogsAsync(new AuditLogFilterDto
        {
            EntityName = entityName,
            EntityId = entityId,
            MaxResults = 500,
        }, cancellationToken);
    }
}
