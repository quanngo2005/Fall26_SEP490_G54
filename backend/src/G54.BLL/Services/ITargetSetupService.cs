using G54.BLL.Dtos.TargetAllocation;

namespace G54.BLL.Services;

/// <summary>
/// Quarter split, department allocation, and publish operations for branch-year targets.
/// Phase 2 of the year-target workflow (docs/api/TARGET_ALLOCATION_CONTRACT.md, 2.A).
/// </summary>
public interface ITargetSetupService
{
    /// <summary>
    /// Get the branch-year quarters, their current values, and metadata for editing.
    /// KPI ratios (NO_AGGREGATION) are read-only.
    /// </summary>
    Task<QuarterSplitDto> GetQuartersAsync(Guid branchYearId, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Save branch-year quarter splits for non-ratio KPIs.
    /// Validates exact sums and locked/past quarters.
    /// Throws InvalidOperationException (409 PERIOD_LOCKED, TARGET_INVALID_STATE) or ApiValidationException (422 TARGET_QUARTER_SUM_MISMATCH).
    /// </summary>
    Task<QuarterSplitDto> SaveQuartersAsync(Guid branchYearId, SaveQuarterSplitRequest request, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Get per-KPI department allocation grid including headcount and totals.
    /// Departments with zero headcount for this KPI are in HiddenDepartments.
    /// </summary>
    Task<KpiAllocationDto> GetAllocationAsync(Guid branchYearId, Guid kpiId, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Save department-year and department-quarter allocations for a single KPI.
    /// Validates exact sums against branch year/quarters.
    /// Throws InvalidOperationException (409 TARGET_QUARTERS_NOT_READY, TARGET_INVALID_STATE)
    /// or ApiValidationException (422 TARGET_DEPARTMENT_NOT_ELIGIBLE, TARGET_ALLOCATION_SUM_MISMATCH, TARGET_DEPT_QUARTER_SUM_MISMATCH).
    /// </summary>
    Task<KpiAllocationDto> SaveAllocationAsync(Guid branchYearId, Guid kpiId, SaveKpiAllocationRequest request, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Get the wizard state: current step, completion counts, and blockers (KPIs not yet matched).
    /// </summary>
    Task<TargetSetupDto> GetSetupAsync(Guid branchId, int year, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Publish all quarter splits and allocations (sets branch-year, branch-quarters, dept-years, dept-quarters to ACTIVE).
    /// Throws InvalidOperationException (409 TARGET_INVALID_STATE) or ApiValidationException (422 TARGET_PUBLISH_BLOCKED with details).
    /// </summary>
    Task<PublishResultDto> PublishAsync(Guid branchYearId, Guid userId, CancellationToken cancellationToken);
}
