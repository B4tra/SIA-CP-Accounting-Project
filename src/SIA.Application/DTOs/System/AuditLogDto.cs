namespace SIA.Application.DTOs.System;

public record AuditLogDto
{
    public long Id { get; init; }
    public string? UserId { get; init; }
    public string Action { get; init; } = string.Empty;
    public string EntityName { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string? OldValues { get; init; }
    public string? NewValues { get; init; }
    public DateTime Timestamp { get; init; }
    public string? IpAddress { get; init; }
}

public class AuditLogFilterDto
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? EntityName { get; set; }
    public string? UserId { get; set; }
    public string? Action { get; set; }
    public string? SearchTerm { get; set; }
}
