using SIA.Application.Common.Models;
using SIA.Application.DTOs.System;

namespace SIA.Application.Services.System;

public interface ITransactionNumberService
{
    Task<string> GenerateAsync(string transactionType, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TransactionNumberConfigDto>>> GetConfigsAsync(CancellationToken ct = default);
    Task<Result<TransactionNumberConfigDto>> UpdateConfigAsync(int id, UpdateTransactionNumberConfigDto dto, CancellationToken ct = default);
}
