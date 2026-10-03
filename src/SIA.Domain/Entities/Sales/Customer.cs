using SIA.Domain.Common;

namespace SIA.Domain.Entities.Sales;

public class Customer : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }

    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
}
