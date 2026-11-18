using G54.BLL.Dtos.DataImport;

namespace G54.BLL.Services;

/// <summary>
/// Employee/organization Excel import (docs/api/DATA_IMPORT_CONTRACT.md). Stateless: preview validates the
/// uploaded file without writing; commit takes the same file again, re-validates and writes in one transaction.
/// </summary>
public interface IEmployeeImportService
{
    byte[] GetTemplate();

    Task<ImportPreviewDto> PreviewAsync(string fileName, Stream file, CancellationToken cancellationToken);

    Task<ImportCommitResultDto> CommitAsync(Stream file, CancellationToken cancellationToken);
}
