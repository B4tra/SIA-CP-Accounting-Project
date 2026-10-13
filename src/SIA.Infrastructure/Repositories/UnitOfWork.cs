using Microsoft.EntityFrameworkCore.Storage;
using SIA.Application.Common.Interfaces;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Repositories;

/// <summary>
/// Unit of Work di atas <see cref="AppDbContext"/>. Repository di-cache per tipe entity.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork, IDisposable, IAsyncDisposable
{
    private readonly AppDbContext _context;
    private readonly Dictionary<Type, object> _repositories = new();
    private IDbContextTransaction? _transaction;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public IRepository<T> Repository<T>() where T : class
    {
        if (!_repositories.TryGetValue(typeof(T), out var repository))
        {
            repository = new Repository<T>(_context);
            _repositories[typeof(T)] = repository;
        }
        return (IRepository<T>)repository;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("Transaksi database sudah berjalan.");

        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("Tidak ada transaksi database yang berjalan.");

        try
        {
            await _transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null) return;

        try
        {
            await _transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    private async Task DisposeTransactionAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _transaction = null;
    }

    public async ValueTask DisposeAsync() => await DisposeTransactionAsync();
}
