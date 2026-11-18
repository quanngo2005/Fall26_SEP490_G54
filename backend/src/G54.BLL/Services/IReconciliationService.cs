using G54.BLL.Dtos.Reconciliation;

namespace G54.BLL.Services;

/// <summary>
/// The reconciliation screens (SCR-REV-03 / SCR-REV-03-D1): every transaction code of a work against the imported Core Banking
/// Excel file. There is no Core Banking API: the only source is the imported rows. A caller sees the departments their
/// <c>reconciliation:read</c> scope covers (QA and administrators: all, managers: their own department or branch).
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationListDto> ListAsync(Guid userId, ReconciliationFilterDto filter, CancellationToken cancellationToken = default);

    Task<ReconciliationDetailDto> GetDetailAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>The same rows as the list (every page, the filters apply) as an Excel workbook.</summary>
    Task<ReconciliationExportDto> ExportAsync(Guid userId, ReconciliationFilterDto filter, CancellationToken cancellationToken = default);
}
