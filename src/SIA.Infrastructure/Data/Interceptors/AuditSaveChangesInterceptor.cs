using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SIA.Application.Common.Interfaces;
using SIA.Domain.Entities.System;

namespace SIA.Infrastructure.Data.Interceptors;

public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUserService;

    public AuditSaveChangesInterceptor(ICurrentUserService currentUserService)
    {
        _currentUserService = currentUserService;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
        {
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        var context = eventData.Context;
        var auditEntries = new List<AuditLog>();
        var now = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            // Jangan mencatat perubahan pada tabel AuditLog itu sendiri
            if (entry.Entity is AuditLog || entry.State is EntityState.Detached or EntityState.Unchanged)
            {
                continue;
            }

            var entityName = entry.Metadata.ClrType.Name;
            var action = entry.State switch
            {
                EntityState.Added => "Create",
                EntityState.Modified => "Update",
                EntityState.Deleted => "Delete",
                _ => entry.State.ToString()
            };

            var oldValues = new Dictionary<string, object?>();
            var newValues = new Dictionary<string, object?>();

            foreach (var prop in entry.Properties)
            {
                var propName = prop.Metadata.Name;

                if (entry.State == EntityState.Added)
                {
                    newValues[propName] = prop.CurrentValue;
                }
                else if (entry.State == EntityState.Deleted)
                {
                    oldValues[propName] = prop.OriginalValue;
                }
                else if (entry.State == EntityState.Modified)
                {
                    if (prop.IsModified)
                    {
                        oldValues[propName] = prop.OriginalValue;
                        newValues[propName] = prop.CurrentValue;
                    }
                }
            }

            var primaryKey = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString()
                ?? "0";

            var auditLog = new AuditLog
            {
                UserId = _currentUserService.UserId,
                Action = action,
                EntityName = entityName,
                EntityId = primaryKey,
                OldValues = oldValues.Count > 0 ? JsonSerializer.Serialize(oldValues) : null,
                NewValues = newValues.Count > 0 ? JsonSerializer.Serialize(newValues) : null,
                Timestamp = now,
                IpAddress = _currentUserService.IpAddress
            };

            auditEntries.Add(auditLog);
        }

        if (auditEntries.Count > 0)
        {
            context.Set<AuditLog>().AddRange(auditEntries);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
