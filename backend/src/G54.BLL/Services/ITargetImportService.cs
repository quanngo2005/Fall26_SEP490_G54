using G54.BLL.Dtos.TargetImport;

namespace G54.BLL.Services;

/// <summary>
/// Stateless preview/commit for the year-target Excel import (docs/api/TARGET_ALLOCATION_CONTRACT.md), same shape as
/// <see cref="IEmployeeImportService"/>: both calls take the workbook, and commit re-validates it before writing.
/// </summary>
public interface ITargetImportService
{
    byte[] BuildTemplate(int year);

    Task<TargetImportPreviewDto> PreviewAsync(Guid branchId, int year, string fileName, Stream file, Guid userId, CancellationToken cancellationToken);

    /// <summary>Writes the valid rows (D9: invalid rows are skipped). Throws 422 <c>TARGET_IMPORT_NO_VALID_ROWS</c> when none are valid.</summary>
    Task<TargetImportCommitResultDto> CommitAsync(Guid branchId, int year, string fileName, Stream file, Guid userId, CancellationToken cancellationToken);
}
