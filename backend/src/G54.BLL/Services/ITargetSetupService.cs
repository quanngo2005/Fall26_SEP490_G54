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
    /// Get department allocation grids for every allocatable KPI of the branch-year (matrix view). Ratio/no-aggregation
    /// KPIs and KPIs whose quarter split is not yet matched to the year value are omitted. Ordered like quarter-split
    /// rows (KPI group code, then KPI code).
    /// </summary>
    Task<IReadOnlyList<KpiAllocationDto>> GetAllAllocationsAsync(Guid branchYearId, Guid userId, CancellationToken cancellationToken);

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

    // =========================================================================
    // Tier 3: department -> employee allocation (opens only once the department's own tier,
    // tier 2, is Published/ACTIVE). docs/api/TARGET_ALLOCATION_CONTRACT.md, Phase 3.
    // =========================================================================

    /// <summary>
    /// Get the employee allocation grid for a single department-year KPI row (deptYearId pins department + KPI + year).
    /// Only employees holding a responsible position for the KPI (evaluation profile, falling back to work-type) are
    /// editable; others are returned in LockedEmployees with a reason.
    /// Throws ConflictException (409 TARGET_TIER_NOT_PUBLISHED) if the department tier is not yet ACTIVE.
    /// </summary>
    Task<EmployeeAllocationDto> GetEmployeeAllocationAsync(Guid deptYearId, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Get employee allocation grids for every KPI of the department that owns deptYearId (matrix view), ordered like
    /// quarter-split/department-allocation rows (KPI group code, then KPI code).
    /// </summary>
    Task<IReadOnlyList<EmployeeAllocationDto>> GetAllEmployeeAllocationsAsync(Guid deptYearId, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Save employee-year and employee-quarter allocations for a single department-year KPI row.
    /// Validates exact sums (employees = department year; each employee's quarters = their year) and employee
    /// position eligibility.
    /// Throws ConflictException (409 TARGET_TIER_NOT_PUBLISHED, PERIOD_LOCKED) or
    /// BusinessRuleException (422 TARGET_EMPLOYEE_NOT_ELIGIBLE, TARGET_EMPLOYEE_ALLOCATION_SUM_MISMATCH, TARGET_EMPLOYEE_QUARTER_SUM_MISMATCH).
    /// </summary>
    Task<EmployeeAllocationDto> SaveEmployeeAllocationAsync(Guid deptYearId, SaveEmployeeAllocationRequest request, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Publish employee allocations for every KPI of the department that owns deptYearId (sets employee-year and
    /// employee-quarter rows to ACTIVE). Requires every allocatable KPI's department tier to already be ACTIVE.
    /// Throws BusinessRuleException (422 TARGET_EMPLOYEE_PUBLISH_BLOCKED with details).
    /// </summary>
    Task<PublishEmployeesResultDto> PublishEmployeesAsync(Guid deptYearId, Guid userId, CancellationToken cancellationToken);

    // =========================================================================
    // Mid-term adjustment (Target Allocation v3, D12/D13): the only way to change a published target.
    // =========================================================================

    /// <summary>
    /// BM adjusts the branch-wide quarter split (tier 1) of an already-published (ACTIVE) branch-year, for one or
    /// more KPIs. Validates the same Σquarters = year rule as <see cref="SaveQuartersAsync"/>, versions the changed
    /// quarter rows (SUPERSEDED + Version+1), and writes an ADJUST change-log entry per changed quarter.
    /// Throws ConflictException (409 TARGET_INVALID_STATE if not yet published, PERIOD_LOCKED) or
    /// BusinessRuleException (422 TARGET_QUARTER_SUM_MISMATCH).
    /// </summary>
    Task<QuarterSplitDto> AdjustQuartersAsync(Guid branchYearId, AdjustQuarterSplitRequest request, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// BM adjusts the department allocation (tier 2) of an already-published KPI for one or more departments.
    /// Validates the same sums as <see cref="SaveAllocationAsync"/>, versions the changed department-year/quarter
    /// rows, and reopens that department's tier 3 for this KPI as mismatched (the employee totals no longer equal
    /// the new department year; <see cref="GetEmployeeAllocationAsync"/> reflects this live) until the DM rebalances
    /// via <see cref="AdjustEmployeeAllocationAsync"/>. Notifies the department's manager.
    /// Throws ConflictException (409 TARGET_INVALID_STATE if not yet published, PERIOD_LOCKED) or
    /// BusinessRuleException (422 TARGET_ALLOCATION_SUM_MISMATCH, TARGET_DEPT_QUARTER_SUM_MISMATCH, TARGET_DEPARTMENT_NOT_ELIGIBLE).
    /// </summary>
    Task<KpiAllocationDto> AdjustAllocationAsync(Guid branchYearId, Guid kpiId, AdjustKpiAllocationRequest request, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// DM adjusts the employee allocation (tier 3) of an already-published department-year KPI row. Validates the
    /// same sums as <see cref="SaveEmployeeAllocationAsync"/>, versions the changed employee-year/quarter rows, and
    /// flips every affected employee's ack (CONFIRMED or PENDING) to RECONFIRM. Notifies each affected employee.
    /// Throws ConflictException (409 TARGET_INVALID_STATE if not yet published, PERIOD_LOCKED) or
    /// BusinessRuleException (422 TARGET_EMPLOYEE_NOT_ELIGIBLE, TARGET_EMPLOYEE_ALLOCATION_SUM_MISMATCH, TARGET_EMPLOYEE_QUARTER_SUM_MISMATCH).
    /// </summary>
    Task<EmployeeAllocationDto> AdjustEmployeeAllocationAsync(Guid deptYearId, AdjustEmployeeAllocationRequest request, Guid userId, CancellationToken cancellationToken);

    /// <summary>BM "remind" (W8): sends a one-off notification to the department manager to rebalance/republish tier 3. No-op (Sent = false) when the department has no resolvable manager.</summary>
    Task<RemindDepartmentManagerResultDto> RemindDepartmentManagerAsync(Guid branchYearId, Guid departmentId, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Per-department x KPI progress rollup (W1/W2/W8): target, achieved (reconciled actuals only), how much of
    /// the department's target has been pushed down to employees, and how many employees have confirmed.
    /// </summary>
    Task<ProgressRollupDto> GetProgressRollupAsync(Guid branchYearId, Guid userId, CancellationToken cancellationToken);

    // Phase 2.1 allocation (departments, quarters, confirm, tree)
    Task<List<TargetNodeDto>> AllocateDepartmentsAsync(Guid branchYearId, AllocateDepartmentsDto dto, Guid userId, CancellationToken ct = default);

    Task<List<TargetNodeDto>> AllocateQuartersAsync(Guid deptYearId, AllocateQuartersDto dto, Guid userId, CancellationToken ct = default);

    Task<TargetNodeDto> ConfirmAsync(Guid deptYearId, Guid userId, CancellationToken ct = default);

    Task<List<TargetNodeDto>> GetTreeAsync(Guid branchId, int year, int? compareYear, Guid userId, CancellationToken ct = default);
}
