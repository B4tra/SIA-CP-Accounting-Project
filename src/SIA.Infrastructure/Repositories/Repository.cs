using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Interfaces;
using SIA.Domain.Common;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Repositories;

/// <summary>
/// Implementasi generik <see cref="IRepository{T}"/>. Data yang sudah di-soft-delete otomatis
/// tersaring oleh global query filter; <see cref="Delete"/> dikonversi menjadi soft delete oleh
/// <see cref="AppDbContext"/> untuk entity turunan <see cref="BaseEntity"/>.
/// </summary>
public class Repository<T> : IRepository<T> where T : class
{
    protected readonly AppDbContext Context;
    protected readonly DbSet<T> DbSet;

    public Repository(AppDbContext context)
    {
        Context = context;
        DbSet = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await DbSet.FindAsync(new object[] { id }, cancellationToken);

        // FindAsync dapat mengembalikan entity yang masih dilacak walau sudah di-soft-delete
        return entity is BaseEntity { IsDeleted: true } ? null : entity;
    }

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await DbSet.ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<T>> FindAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default) =>
        await DbSet.Where(predicate).ToListAsync(cancellationToken);

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default) =>
        await DbSet.AddAsync(entity, cancellationToken);

    public void Update(T entity) => DbSet.Update(entity);

    public void Delete(T entity) => DbSet.Remove(entity);
}
