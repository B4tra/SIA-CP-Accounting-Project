using System.Linq.Expressions;

namespace SIA.Application.Common.Interfaces;

/// <summary>
/// Repository generik. Untuk entity turunan <c>BaseEntity</c>, <see cref="Delete"/> dijalankan sebagai
/// soft delete oleh DbContext, dan data yang sudah dihapus otomatis tersaring oleh global query filter.
/// </summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> FindAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    void Update(T entity);

    void Delete(T entity);
}
