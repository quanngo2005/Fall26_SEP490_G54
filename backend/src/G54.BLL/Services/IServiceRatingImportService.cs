using G54.BLL.Common.Models;
using G54.BLL.Dtos.ServiceRatingImport;
using G54.BLL.Dtos.TransactionImport;

namespace G54.BLL.Services;

/// <summary>Customer service rating import (UC32). Contract: docs/api/SERVICE_RATING_IMPORT_CONTRACT.md.</summary>
public interface IServiceRatingImportService
{
    Task<ImportBatchDto> ImportAsync(string fileName, Stream excel, Guid userId, CancellationToken ct = default);

    Task<PagedResult<ImportBatchDto>> ListBatchesAsync(int page, int pageSize, CancellationToken ct = default);

    Task<ImportBatchDto> GetBatchAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<ImportRowErrorDto>> ListErrorsAsync(Guid batchId, int page, int pageSize, CancellationToken ct = default);

    Task<PagedResult<ServiceRatingDto>> ListRatingsAsync(Guid batchId, int page, int pageSize, CancellationToken ct = default);
}
