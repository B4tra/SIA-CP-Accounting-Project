namespace SIA.Application.DTOs.System;

public class AuditLogDto
{
    public long Id { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
}

public class AuditLogFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? UserId { get; set; }
    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public string? Action { get; set; }

    /// <summary>Batas jumlah baris yang dikembalikan (terbaru lebih dulu).</summary>
    public int MaxResults { get; set; } = 500;
}
