using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.Sales;
using SIA.Application.Services.Sales;
using SIA.Domain.Entities.Sales;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Services;

/// <summary>Pelanggan. Baca: semua role. Tulis: Admin, Staff. Pencarian nama/telepon.</summary>
public class CustomerService : ICustomerService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public CustomerService(AppDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<CustomerDto>>> GetAllAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<IReadOnlyList<CustomerDto>>.Failure(ServiceGuard.ReadDenied);

        var query = _context.Customers.AsNoTracking();
        var term = ServiceGuard.Clean(search);
        if (term is not null)
        {
            var pattern = $"%{term}%";
            query = query.Where(c =>
                EF.Functions.Like(c.Name, pattern) ||
                (c.Phone != null && EF.Functions.Like(c.Phone, pattern)));
        }

        var customers = await query.OrderBy(c => c.Name).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<CustomerDto>>.Success(customers.Select(ToDto).ToList());
    }

    public async Task<Result<CustomerDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ViewAllData))
            return Result<CustomerDto>.Failure(ServiceGuard.ReadDenied);

        var customer = await _context.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        return customer is null
            ? Result<CustomerDto>.Failure("Pelanggan tidak ditemukan.")
            : Result<CustomerDto>.Success(ToDto(customer));
    }

    public async Task<Result<CustomerDto>> CreateAsync(CreateCustomerDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result<CustomerDto>.Failure(ServiceGuard.WriteDenied);

        var errors = ServiceGuard.Validate(dto);
        if (errors.Count > 0)
            return Result<CustomerDto>.Failure(errors);

        var customer = new Customer();
        Apply(customer, dto);
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(cancellationToken);
        return Result<CustomerDto>.Success(ToDto(customer));
    }

    public async Task<Result<CustomerDto>> UpdateAsync(UpdateCustomerDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result<CustomerDto>.Failure(ServiceGuard.WriteDenied);

        var errors = ServiceGuard.Validate(dto);
        if (errors.Count > 0)
            return Result<CustomerDto>.Failure(errors);

        var customer = await _context.Customers.SingleOrDefaultAsync(c => c.Id == dto.Id, cancellationToken);
        if (customer is null)
            return Result<CustomerDto>.Failure("Pelanggan tidak ditemukan.");

        Apply(customer, dto);
        await _context.SaveChangesAsync(cancellationToken);
        return Result<CustomerDto>.Success(ToDto(customer));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.ManageTransactions))
            return Result.Failure(ServiceGuard.WriteDenied);

        var customer = await _context.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
            return Result.Failure("Pelanggan tidak ditemukan.");

        _context.Customers.Remove(customer); // soft delete
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static void Apply(Customer customer, CreateCustomerDto dto)
    {
        customer.Name = dto.Name.Trim();
        customer.Phone = ServiceGuard.Clean(dto.Phone);
        customer.Address = ServiceGuard.Clean(dto.Address);
    }

    private static CustomerDto ToDto(Customer c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Phone = c.Phone,
        Address = c.Address,
    };
}
