using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SIA.Application.Common.Interfaces;
using SIA.Domain.Common;
using SIA.Domain.Entities.System;

namespace SIA.Infrastructure.Data.Interceptors;

/// <summary>
/// Menulis <see cref="AuditLog"/> secara otomatis setiap kali entity berubah.
/// <list type="bullet">
/// <item>Create: semua nilai baru (dicatat setelah simpan agar Id hasil auto-increment terisi).</item>
/// <item>Update: hanya kolom yang berubah (nilai lama dan baru).</item>
/// <item>Delete: soft delete (IsDeleted false → true), snapshot nilai lama.</item>
/// </list>
/// Dikecualikan: AuditLog sendiri, tabel ASP.NET Identity (agar hash password tidak tercatat),
/// dan TransactionNumberConfig kecuali perubahan Prefix/Format.
/// Untuk entity yang di-attach tanpa dilacak (detached update) nilai lama tidak diketahui,
/// sehingga service sebaiknya memuat entity terlebih dahulu sebelum mengubahnya.
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    public const string ActionCreate = "Create";
    public const string ActionUpdate = "Update";
    public const string ActionDelete = "Delete";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] TransactionNumberAuditedProperties =
    {
        nameof(TransactionNumberConfig.Prefix),
        nameof(TransactionNumberConfig.Format)
    };

    private readonly ICurrentUserService? _currentUser;
    private readonly ConditionalWeakTable<DbContext, List<PendingAudit>> _pending = new();

    public AuditSaveChangesInterceptor(ICurrentUserService? currentUser = null)
    {
        _currentUser = currentUser;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        CollectChanges(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CollectChanges(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (TryBuildAuditLogs(eventData.Context, out var context, out var logs))
        {
            context.Set<AuditLog>().AddRange(logs);
            context.SaveChanges();
        }
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (TryBuildAuditLogs(eventData.Context, out var context, out var logs))
        {
            context.Set<AuditLog>().AddRange(logs);
            await context.SaveChangesAsync(cancellationToken);
        }
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is not null) _pending.Remove(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) _pending.Remove(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void CollectChanges(DbContext? context)
    {
        if (context is null) return;

        var pending = new List<PendingAudit>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (IsExcluded(entry.Entity.GetType())) continue;

            var audit = entry.State switch
            {
                EntityState.Added => CreateAudit(entry),
                EntityState.Modified => UpdateOrDeleteAudit(entry),
                EntityState.Deleted => DeleteAudit(entry),
                _ => null
            };

            if (audit is not null) pending.Add(audit);
        }

        _pending.Remove(context);
        if (pending.Count > 0) _pending.Add(context, pending);
    }

    private bool TryBuildAuditLogs(DbContext? eventContext, out DbContext context, out List<AuditLog> logs)
    {
        context = eventContext!;
        logs = new List<AuditLog>();

        if (eventContext is null || !_pending.TryGetValue(eventContext, out var pending)) return false;
        _pending.Remove(eventContext);

        var timestamp = DateTime.UtcNow;
        var userId = _currentUser?.UserId;
        var ipAddress = _currentUser?.IpAddress;

        foreach (var audit in pending)
        {
            // Nilai baru untuk Create diambil setelah simpan agar Id auto-increment ikut tercatat
            var newValues = audit.Action == ActionCreate
                ? audit.Entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue)
                : audit.NewValues;

            logs.Add(new AuditLog
            {
                UserId = userId,
                Action = audit.Action,
                EntityName = audit.Entry.Metadata.ClrType.Name,
                EntityId = GetPrimaryKey(audit.Entry),
                OldValues = Serialize(audit.OldValues),
                NewValues = Serialize(newValues),
                Timestamp = timestamp,
                IpAddress = ipAddress
            });
        }

        return logs.Count > 0;
    }

    private static PendingAudit? CreateAudit(EntityEntry entry)
    {
        if (entry.Entity is TransactionNumberConfig) return null; // dibuat oleh seeding/sistem
        return new PendingAudit(entry, ActionCreate, null, null);
    }

    private static PendingAudit? UpdateOrDeleteAudit(EntityEntry entry)
    {
        if (entry.Entity is BaseEntity && IsSoftDelete(entry))
            return DeleteAudit(entry);

        var changed = entry.Properties
            .Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))
            .ToList();

        if (entry.Entity is TransactionNumberConfig)
        {
            // Kenaikan sequence tidak dicatat; hanya perubahan Prefix/Format
            if (!changed.Any(p => TransactionNumberAuditedProperties.Contains(p.Metadata.Name)))
                return null;
        }

        if (changed.Count == 0 || changed.All(p => p.Metadata.Name == nameof(BaseEntity.UpdatedAt)))
            return null;

        return new PendingAudit(
            entry,
            ActionUpdate,
            changed.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue),
            changed.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));
    }

    private static PendingAudit? DeleteAudit(EntityEntry entry)
    {
        if (entry.Entity is TransactionNumberConfig) return null;
        return new PendingAudit(
            entry,
            ActionDelete,
            entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue),
            null);
    }

    private static bool IsSoftDelete(EntityEntry entry)
    {
        var isDeleted = entry.Property(nameof(BaseEntity.IsDeleted));
        return isDeleted.IsModified
               && isDeleted.CurrentValue is true
               && isDeleted.OriginalValue is false;
    }

    private static bool IsExcluded(Type type)
    {
        if (type == typeof(AuditLog)) return true;
        if (typeof(IdentityUser).IsAssignableFrom(type) || typeof(IdentityRole).IsAssignableFrom(type)) return true;
        return type.Namespace == typeof(IdentityUser).Namespace; // IdentityUserRole, Claim, Login, Token, RoleClaim
    }

    private static string GetPrimaryKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return string.Empty;
        return string.Join(",", key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString()));
    }

    private static string? Serialize(Dictionary<string, object?>? values) =>
        values is null || values.Count == 0 ? null : JsonSerializer.Serialize(values, JsonOptions);

    private sealed record PendingAudit(
        EntityEntry Entry,
        string Action,
        Dictionary<string, object?>? OldValues,
        Dictionary<string, object?>? NewValues);
}
