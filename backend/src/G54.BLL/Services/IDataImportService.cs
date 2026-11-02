using G54.BLL.Dtos.DataImport;

namespace G54.BLL.Services;

/// <summary>
/// Façade in front of the generic import engine: resolves the definition by <c>type</c>, drives the
/// preview/commit session, and shapes the result for the API. Contract: docs/api/DATA_IMPORT_CONTRACT.md.
/// </summary>
public interface IDataImportService
{
    byte[] GetTemplate(string type);

    Task<ImportPreviewDto> PreviewAsync(string type, string fileName, Stream file, Guid userId, CancellationToken cancellationToken);

    /// <summary>Throws <see cref="G54.BLL.Common.Exceptions.NotFoundException"/> when the session is missing, expired, or owned by another user.</summary>
    Task<ImportPreviewDto> GetPreviewAsync(Guid importId, ImportPreviewQueryDto query, Guid userId, CancellationToken cancellationToken);

    Task<ImportCommitResultDto> CommitAsync(Guid importId, Guid userId, CancellationToken cancellationToken);

    Task CancelAsync(Guid importId, Guid userId, CancellationToken cancellationToken);
}
