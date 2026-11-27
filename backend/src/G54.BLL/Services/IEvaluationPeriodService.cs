using G54.BLL.Common.Models;
using G54.BLL.Dtos.EvaluationPeriod;

namespace G54.BLL.Services;

public interface IEvaluationPeriodService
{
    /// <summary>Lists evaluation periods with optional search, type/status filter, hierarchy navigation, and paging.</summary>
    Task<PagedResult<EvaluationPeriodItemDto>> GetPeriodsAsync(EvaluationPeriodFilterDto filter, CancellationToken cancellationToken = default);

    /// <summary>Gets evaluation period detail by ID.</summary>
    Task<EvaluationPeriodItemDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates a period that starts <c>OPEN</c>.</summary>
    Task<EvaluationPeriodStatusDto> CreateAsync(CreateEvaluationPeriodRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Creates the whole period tree of a calendar year in one step: the year, its 2 half-years and 4 quarters, all <c>OPEN</c>. 409 when the year already exists.</summary>
    Task<CreateYearPeriodsResultDto> CreateYearAsync(CreateYearPeriodsRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>OPEN</c> → <c>IN_REVIEW</c>. Returns the pre-close check: result submissions still in review (carried to the
    /// next period) and appeals still open (they must be settled before the period is locked, BR-31).
    /// </summary>
    Task<ClosePeriodResultDto> CloseAsync(Guid periodId, CancellationToken cancellationToken = default);

    /// <summary>Gets the focus/priority KPIs configured by BM for the given evaluation period.</summary>
    Task<PeriodFocusKpisDto> GetFocusKpisAsync(Guid periodId, CancellationToken cancellationToken = default);

    /// <summary>Updates the focus/priority KPIs for an open or in-review evaluation period (max 5 KPIs).</summary>
    Task<PeriodFocusKpisDto> UpdateFocusKpisAsync(Guid periodId, UpdatePeriodFocusKpisRequestDto request, CancellationToken cancellationToken = default);
}
