namespace SIA.Domain.Entities.System;

/// <summary>
/// Catatan audit perubahan entity. Bukan turunan BaseEntity (Id long, tidak di-soft-delete).
/// </summary>
public class AuditLog
{
    public long Id { get; set; }
    public string? UserId { get; set; }

    /// <summary>"Create", "Update", atau "Delete".</summary>
    public string Action { get; set; } = string.Empty;

    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;

    /// <summary>JSON snapshot sebelum perubahan.</summary>
    public string? OldValues { get; set; }

    /// <summary>JSON snapshot sesudah perubahan.</summary>
    public string? NewValues { get; set; }

    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
}
