using Microsoft.AspNetCore.Identity;
using SIA.Domain.Entities.Accounting;
using SIA.Domain.Entities.Purchasing;
using SIA.Domain.Entities.Sales;

namespace SIA.Infrastructure.Identity;

/// <summary>
/// Pengguna aplikasi. Ditempatkan di Infrastructure agar Domain bebas dari dependensi Identity.
/// Email, UserName, PasswordHash sudah tersedia di IdentityUser.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties (relasi FK dikonfigurasi di tahap EF Core)
    public ICollection<JournalEntry> JournalEntries { get; set; } = new List<JournalEntry>();
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public ICollection<PurchaseOrder> CreatedPurchaseOrders { get; set; } = new List<PurchaseOrder>();
    public ICollection<PurchaseOrder> ApprovedPurchaseOrders { get; set; } = new List<PurchaseOrder>();
}
