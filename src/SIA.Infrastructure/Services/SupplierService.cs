using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.Purchasing;
using SIA.Application.Services.Purchasing;
using SIA.Domain.Entities.Purchasing;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Services;

/// <summary>Pemasok. Baca: semua role. Tulis: Admin, Staff. Pencarian nama/telepon.</summary>
public class SupplierService : ISupplierService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public SupplierService(AppDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<SupplierDto>>> GetAllAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<SupplierDto>>.Failure(ServiceGuard.ReadDenied);

        var query = _context.Suppliers.AsNoTracking();
        var term = ServiceGuard.Clean(search);
        if (term is not null)
        {
            var pattern = $"%{term}%";
            query = query.Where(s =>
                EF.Functions.Like(s.Name, pattern) ||
                (s.Phone != null && EF.Functions.Like(s.Phone, pattern)));
        }

        var suppliers = await query.OrderBy(s => s.Name).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<SupplierDto>>.Success(suppliers.Select(ToDto).ToList());
    }

    public async Task<Result<SupplierDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<SupplierDto>.Failure(ServiceGuard.ReadDenied);

        var supplier = await _context.Suppliers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return supplier is null
            ? Result<SupplierDto>.Failure("Pemasok tidak ditemukan.")
            : Result<SupplierDto>.Success(ToDto(supplier));
    }

    public async Task<Result<SupplierDto>> CreateAsync(CreateSupplierDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result<SupplierDto>.Failure(ServiceGuard.WriteDenied);

        var errors = ServiceGuard.Validate(dto);
        if (errors.Count > 0)
            return Result<SupplierDto>.Failure(errors);

        var supplier = new Supplier();
        Apply(supplier, dto);
        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync(cancellationToken);
        return Result<SupplierDto>.Success(ToDto(supplier));
    }

    public async Task<Result<SupplierDto>> UpdateAsync(UpdateSupplierDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result<SupplierDto>.Failure(ServiceGuard.WriteDenied);

        var errors = ServiceGuard.Validate(dto);
        if (errors.Count > 0)
            return Result<SupplierDto>.Failure(errors);

        var supplier = await _context.Suppliers.SingleOrDefaultAsync(s => s.Id == dto.Id, cancellationToken);
        if (supplier is null)
            return Result<SupplierDto>.Failure("Pemasok tidak ditemukan.");

        Apply(supplier, dto);
        await _context.SaveChangesAsync(cancellationToken);
        return Result<SupplierDto>.Success(ToDto(supplier));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result.Failure(ServiceGuard.WriteDenied);

        var supplier = await _context.Suppliers.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (supplier is null)
            return Result.Failure("Pemasok tidak ditemukan.");

        _context.Suppliers.Remove(supplier); // soft delete
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static void Apply(Supplier supplier, CreateSupplierDto dto)
    {
        supplier.Name = dto.Name.Trim();
        supplier.Phone = ServiceGuard.Clean(dto.Phone);
        supplier.Address = ServiceGuard.Clean(dto.Address);
    }

    private static SupplierDto ToDto(Supplier s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Phone = s.Phone,
        Address = s.Address,
    };
}
