using G54.BLL.Common.Models;
using G54.BLL.Dtos.TransactionImport;

namespace G54.BLL.Services;

public interface ITransactionImportService
{
    Task<ImportBatchDto> ImportAsync(string fileName, Stream excel, Guid userId, CancellationToken ct = default);

    Task<PagedResult<ImportBatchDto>> ListBatchesAsync(int page, int pageSize, CancellationToken ct = default);

    Task<ImportBatchDto> GetBatchAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<ImportedTransactionDto>> ListTransactionsAsync(
        Guid batchId, string? employeeCode, string? productCode, int page, int pageSize, CancellationToken ct = default);
}
