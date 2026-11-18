using G54.BLL.Dtos.TransactionImport;
using G54.BLL.Dtos.WorkTransaction;

namespace G54.BLL.Services;

public interface IWorkTransactionService
{
    Task<WorkTransactionsDto> ListAsync(Guid workId, Guid userId, CancellationToken ct = default);

    Task<WorkTransactionsDto> AddAsync(Guid workId, Guid userId, AddWorkTransactionsRequestDto request, CancellationToken ct = default);

    Task<WorkTransactionsDto> RemoveAsync(Guid workId, Guid transactionId, Guid userId, CancellationToken ct = default);

    /// <summary>Re-checks every IN_REVIEW code that appears in the import batch and counts how they ended up.</summary>
    Task<ReconcileSummaryDto> ReconcileBatchAsync(Guid batchId, CancellationToken ct = default);
}
