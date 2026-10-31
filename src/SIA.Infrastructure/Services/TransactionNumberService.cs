using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Application.Common.Models;
using SIA.Application.DTOs.System;
using SIA.Application.Services.System;
using SIA.Domain.Entities.System;
using SIA.Infrastructure.Data;

namespace SIA.Infrastructure.Services;

/// <summary>
/// Satu-satunya sumber nomor transaksi. Placeholder format: <c>{Prefix}</c>, <c>{Year}</c>,
/// <c>{Seq}</c> / <c>{Seq:0000}</c>. Sequence di-reset saat tahun transaksi lebih baru dari
/// tahun konfigurasi. Kenaikan sequence dilindungi concurrency token <c>CurrentSequence</c>.
/// </summary>
public class TransactionNumberService : ITransactionNumberService
{
    public const int MaxRetries = 3;

    private static readonly Regex SeqPattern = new(@"\{Seq(?::(?<fmt>[^}]*))?\}", RegexOptions.Compiled);

    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public TransactionNumberService(AppDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<string> GenerateAsync(string transactionType, DateTime transactionDate, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var config = await _context.TransactionNumberConfigs
                .SingleOrDefaultAsync(c => c.TransactionType == transactionType, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Konfigurasi nomor transaksi untuk jenis '{transactionType}' tidak ditemukan.");

            // Pastikan nilai terbaru dari database (entity mungkin sudah dilacak context ini)
            await _context.Entry(config).ReloadAsync(cancellationToken);

            var year = transactionDate.Year;
            if (year < config.Year)
            {
                // Satu baris konfigurasi hanya menyimpan sequence tahun berjalan; mundur tahun
                // akan menghasilkan nomor ganda, sehingga ditolak.
                throw new InvalidOperationException(
                    $"Penomoran '{transactionType}' sudah berjalan untuk tahun {config.Year}; " +
                    $"transaksi bertanggal tahun {year} tidak dapat diberi nomor.");
            }

            if (year > config.Year)
            {
                config.Year = year;
                config.CurrentSequence = 0;
            }

            config.CurrentSequence++;
            var number = Render(config.Format, config.Prefix, config.Year, config.CurrentSequence);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return number;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxRetries)
            {
                // Nomor sudah diambil proses lain: muat ulang dan coba lagi
                await _context.Entry(config).ReloadAsync(cancellationToken);
            }
        }
    }

    public async Task<Result<IReadOnlyList<TransactionNumberConfigDto>>> GetConfigsAsync(CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.RequireAdmin))
            return Result<IReadOnlyList<TransactionNumberConfigDto>>.Failure(ServiceGuard.ReadDenied);

        var configs = await _context.TransactionNumberConfigs
            .AsNoTracking()
            .OrderBy(c => c.TransactionType)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<TransactionNumberConfigDto>>.Success(configs.Select(ToDto).ToList());
    }

    public async Task<Result<TransactionNumberConfigDto>> UpdateConfigAsync(UpdateTransactionNumberConfigDto dto, CancellationToken cancellationToken = default)
    {
        if (!ServiceGuard.Has(_currentUser, Policies.RequireAdmin))
            return Result<TransactionNumberConfigDto>.Failure(ServiceGuard.WriteDenied);

        var errors = ServiceGuard.Validate(dto).ToList();
        var format = dto.Format?.Trim() ?? string.Empty;
        var prefix = dto.Prefix?.Trim() ?? string.Empty;

        if (format.Length > 0 && !format.Contains("{Seq", StringComparison.Ordinal))
            errors.Add("Format wajib memuat placeholder {Seq} atau {Seq:0000}.");

        if (errors.Count == 0)
        {
            try
            {
                Render(format, prefix, DateTime.Today.Year, 1);
            }
            catch (FormatException)
            {
                errors.Add("Format nomor transaksi tidak valid.");
            }
        }

        if (errors.Count > 0)
            return Result<TransactionNumberConfigDto>.Failure(errors);

        var config = await _context.TransactionNumberConfigs
            .SingleOrDefaultAsync(c => c.Id == dto.Id, cancellationToken);
        if (config is null)
            return Result<TransactionNumberConfigDto>.Failure("Konfigurasi nomor transaksi tidak ditemukan.");

        config.Prefix = prefix;
        config.Format = format;
        await _context.SaveChangesAsync(cancellationToken);

        return Result<TransactionNumberConfigDto>.Success(ToDto(config));
    }

    /// <summary>Merender format nomor transaksi.</summary>
    public static string Render(string format, string prefix, int year, int sequence)
    {
        var result = format
            .Replace("{Prefix}", prefix, StringComparison.Ordinal)
            .Replace("{Year}", year.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        return SeqPattern.Replace(result, m =>
        {
            var fmt = m.Groups["fmt"];
            return fmt.Success && fmt.Value.Length > 0
                ? sequence.ToString(fmt.Value, CultureInfo.InvariantCulture)
                : sequence.ToString(CultureInfo.InvariantCulture);
        });
    }

    private static TransactionNumberConfigDto ToDto(TransactionNumberConfig c) => new()
    {
        Id = c.Id,
        TransactionType = c.TransactionType,
        Prefix = c.Prefix,
        Format = c.Format,
        CurrentSequence = c.CurrentSequence,
        Year = c.Year,
    };
}
