namespace SIA.Domain.Common;

/// <summary>
/// Kelas dasar semua entity bisnis: primary key int auto-increment dan dukungan soft delete.
/// </summary>
public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
