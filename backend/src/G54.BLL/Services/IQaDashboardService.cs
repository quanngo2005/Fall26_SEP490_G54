using G54.BLL.Common.Models;
using G54.BLL.Dtos.Quality;

namespace G54.BLL.Services;

/// <summary>
/// Operational QA Dashboard service powering SCR-PRF-04.
/// Calculates discrepancy counts, QA queues, risk event penalties, 8-week match rates, and reconciliation items.
/// </summary>
public interface IQaDashboardService
{
    /// <summary>Calculates and retrieves main QA dashboard metrics, banner, charts, and rankings.</summary>
    Task<QaDashboardDto> GetDashboardAsync(QaDashboardFilterDto filter, CancellationToken cancellationToken = default);

    /// <summary>Queries paginated reconciliation items with discrepancy status and reasons.</summary>
    Task<PagedResult<QaReconciliationItemDto>> GetReconciliationsAsync(QaReconciliationFilterDto filter, CancellationToken cancellationToken = default);

    /// <summary>Returns dropdown filter options (periods, departments, positions) for the dashboard selectors.</summary>
    Task<QaDashboardFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default);
}

