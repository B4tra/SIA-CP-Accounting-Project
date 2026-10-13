namespace SIA.Application.Common.Interfaces;

/// <summary>
/// Unit of Work: menyediakan repository bertipe dan menyimpan perubahan secara atomik.
/// Transaksi eksplisit dipakai bila satu operasi menyentuh banyak tabel
/// (mis. penjualan + pengurangan stok + StockCard).
/// </summary>
public interface IUnitOfWork
{
    IRepository<T> Repository<T>() where T : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
