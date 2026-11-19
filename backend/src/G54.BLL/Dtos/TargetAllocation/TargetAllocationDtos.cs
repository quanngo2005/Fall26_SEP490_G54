namespace G54.BLL.Dtos.TargetAllocation;

// =========================================================================
// Target Allocation DTOs (docs/api/TARGET_ALLOCATION_CONTRACT.md)
// =========================================================================

public sealed record DepartmentYearItemDto(
    Guid DepartmentId,
    Guid KpiDefinitionId,
    decimal TargetValue);

public sealed record AllocateDepartmentsDto(
    List<DepartmentYearItemDto> Items,
    string? ChangeReason);

public sealed record QuarterItemDto(
    Guid QuarterPeriodId,
    Guid KpiDefinitionId,
    decimal TargetValue);

public sealed record AllocateQuartersDto(
    List<QuarterItemDto> Items,
    string? ChangeReason);

public sealed record TargetNodeDto(
    Guid Id,
    Guid? ParentAssignmentId,
    Guid KpiDefinitionId,
    string KpiName,
    Guid DepartmentId,
    string DepartmentName,
    Guid PeriodId,
    string PeriodCode,
    string PeriodType,
    decimal TargetValue,
    decimal AllocatedValue,
    decimal? ActualValue,
    int Version,
    string Status,
    decimal? CompareTargetValue,
    decimal? CompareActualValue,
    List<TargetNodeDto> Children);

// =========================================================================
// Phase 2: Quarter Split DTOs (docs/api/TARGET_ALLOCATION_CONTRACT.md, 2.A)
// =========================================================================

public sealed record QuarterDto(
    Guid PeriodId,
    int Quarter,
    bool Locked);

public sealed record QuarterSplitRowDto(
    Guid KpiId,
    string KpiCode,
    string KpiName,
    string Unit,
    string GroupCode,
    string GroupName,
    string AggregationType,
    bool IsRatio,
    decimal YearValue,
    decimal? Q1,
    decimal? Q2,
    decimal? Q3,
    decimal? Q4,
    decimal? PreviousYearValue,
    QuarterPreviousYearDto? PreviousYear,
    QuarterPreviousYearDto? PreviousYearActual,
    decimal Sum,
    decimal Diff,
    bool Matched);

public sealed record QuarterPreviousYearDto(
    decimal Q1,
    decimal Q2,
    decimal Q3,
    decimal Q4);

public sealed record QuarterRatiosDto(
    decimal Q1,
    decimal Q2,
    decimal Q3,
    decimal Q4);

public sealed record QuarterSplitDto(
    Guid BranchYearId,
    int Year,
    string Status,
    DateTimeOffset? ImportedAt,
    QuarterRatiosDto? FileRatios,
    List<QuarterDto> Quarters,
    List<QuarterSplitRowDto> Rows);

public sealed record SaveQuarterSplitRequest(
    List<SaveQuarterSplitRowRequest> Rows);

public sealed record SaveQuarterSplitRowRequest(
    Guid KpiId,
    decimal Q1,
    decimal Q2,
    decimal Q3,
    decimal Q4);

// =========================================================================
// Phase 2: KPI Allocation DTOs (docs/api/TARGET_ALLOCATION_CONTRACT.md, 2.A)
// =========================================================================

/// <param name="DeptYearId">The department-year row's own id (null when this department has no dept-year row yet,
/// i.e. tier 2 has not been saved/published for it): the Phase 3 anchor for
/// <c>GET/PUT .../employee-allocation</c> and <c>POST .../publish-employees</c>, so the frontend can reach tier-3
/// data for this department/KPI without a second lookup (W2/W8).</param>
public sealed record DepartmentAllocationRowDto(
    Guid DepartmentId,
    string DepartmentName,
    int Headcount,
    decimal Year,
    decimal Q1,
    decimal Q2,
    decimal Q3,
    decimal Q4,
    decimal? PreviousYear,
    decimal? PreviousYearActual,
    bool Matched,
    Guid? DeptYearId = null);

public sealed record DepartmentAllocationMetadataDto(
    Guid DepartmentId,
    string DepartmentName,
    string Reason);

public sealed record AllocationTotalsDto(
    decimal Year,
    decimal Q1,
    decimal Q2,
    decimal Q3,
    decimal Q4);

public sealed record AllocationRemainingDto(
    decimal Year,
    decimal Q1,
    decimal Q2,
    decimal Q3,
    decimal Q4);

public sealed record BranchTargetValuesDto(
    decimal Year,
    decimal Q1,
    decimal Q2,
    decimal Q3,
    decimal Q4);

public sealed record KpiAllocationDto(
    Guid KpiId,
    string KpiName,
    string Unit,
    string WorkType,
    BranchTargetValuesDto Branch,
    List<DepartmentAllocationRowDto> Departments,
    AllocationTotalsDto Totals,
    AllocationRemainingDto Remaining,
    List<DepartmentAllocationMetadataDto> HiddenDepartments);

public sealed record SaveKpiAllocationRequest(
    List<SaveKpiAllocationDepartmentRequest> Departments);

public sealed record SaveKpiAllocationDepartmentRequest(
    Guid DepartmentId,
    decimal Year,
    decimal Q1,
    decimal Q2,
    decimal Q3,
    decimal Q4);

// =========================================================================
// Phase 2: Publish DTOs (docs/api/TARGET_ALLOCATION_CONTRACT.md, 2.A)
// =========================================================================

public sealed record PublishResultDto(
    DateTimeOffset PublishedAt,
    int BranchYearCount,
    int QuarterCount,
    int DepartmentYearCount,
    int DepartmentQuarterCount);

// =========================================================================
// Phase 2: Setup DTO (G19 - extended from Phase 1)
// =========================================================================

public sealed record TargetSetupKpiBlockerDto(
    Guid KpiId,
    string KpiCode,
    string KpiName);

public sealed record TargetSetupQuartersMetadataDto(
    int Done,
    int Total);

public sealed record TargetSetupAllocationMetadataDto(
    int Done,
    int Total);

/// <summary><c>BranchYearId</c> is any branch-year row of this branch+year: the anchor the quarter-split,
/// allocation and publish routes take. Null when nothing has been imported yet.</summary>
public sealed record TargetSetupDto(
    Guid? BranchYearId,
    bool HasBranchYear,
    DateTimeOffset? ImportedAt,
    int RowCount,
    string Step,
    string Status,
    TargetSetupQuartersMetadataDto Quarters,
    TargetSetupAllocationMetadataDto Allocation,
    bool CanPublish,
    List<TargetSetupKpiBlockerDto> Blockers);

// =========================================================================
// Phase 3: Employee allocation DTOs (Tier 3, docs/api/TARGET_ALLOCATION_CONTRACT.md, Phase 3)
// Mirrors the Phase 2 department-allocation shapes one level down: DeptYearId anchors a single
// (department, KPI, year) row, so unlike the branch-year anchor there is no separate kpiId segment.
// =========================================================================

public sealed record EmployeeAllocationRowDto(
    Guid EmployeeId,
    string EmployeeName,
    string JobPositionName,
    decimal Year,
    decimal Q1,
    decimal Q2,
    decimal Q3,
    decimal Q4,
    decimal? PreviousYear,
    decimal? PreviousYearActual,
    bool Matched);

public sealed record EmployeeAllocationMetadataDto(
    Guid EmployeeId,
    string EmployeeName,
    string Reason);

public sealed record DepartmentTargetValuesDto(
    decimal Year,
    decimal Q1,
    decimal Q2,
    decimal Q3,
    decimal Q4);

public sealed record EmployeeAllocationDto(
    Guid DeptYearId,
    Guid KpiId,
    string KpiName,
    string Unit,
    Guid DepartmentId,
    string DepartmentName,
    string Status,
    DepartmentTargetValuesDto Department,
    List<EmployeeAllocationRowDto> Employees,
    AllocationTotalsDto Totals,
    AllocationRemainingDto Remaining,
    List<EmployeeAllocationMetadataDto> LockedEmployees);

public sealed record SaveEmployeeAllocationRequest(
    List<SaveEmployeeAllocationEmployeeRequest> Employees);

public sealed record SaveEmployeeAllocationEmployeeRequest(
    Guid EmployeeId,
    decimal Year,
    decimal Q1,
    decimal Q2,
    decimal Q3,
    decimal Q4);

public sealed record PublishEmployeesResultDto(
    DateTimeOffset PublishedAt,
    Guid DepartmentId,
    int Year,
    int EmployeeYearCount,
    int EmployeeQuarterCount);

// =========================================================================
// Mid-term adjustment (Target Allocation v3, D12/D13): the only way to change a published (ACTIVE) target.
// Reuses the Save* request shapes one-for-one; the ChangeReason is mandatory here (vs. optional on Save), and the
// written rows are versioned (SUPERSEDED + Version+1) instead of updated in place.
// =========================================================================

public sealed record AdjustQuarterSplitRequest(
    List<SaveQuarterSplitRowRequest> Rows,
    string ChangeReason);

public sealed record AdjustKpiAllocationRequest(
    List<SaveKpiAllocationDepartmentRequest> Departments,
    string ChangeReason);

public sealed record AdjustEmployeeAllocationRequest(
    List<SaveEmployeeAllocationEmployeeRequest> Employees,
    string ChangeReason);

public sealed record RemindDepartmentManagerResultDto(
    DateTimeOffset SentAt,
    Guid? RecipientEmployeeId,
    bool Sent);

// =========================================================================
// Progress rollup (BM/DM overview, W1/W2/W8): per department x KPI, target vs. achieved (reconciled actuals
// only) and how far tier 3 has gotten. Read-only; reuses the branch-year anchor like quarter-split/allocation.
// =========================================================================

/// <param name="Achieved">Sum of VALID (reconciled) <c>ActualProgress</c> over the department's whole tree for this
/// KPI and year; never counts a result still IN_REVIEW.</param>
/// <param name="AllocatedToEmployeesPercent">Σ employee-year targets ÷ department year target x 100 (0 when tier 3
/// has not been allocated at all for this department/KPI).</param>
/// <param name="EmployeesConfirmed">Employees assigned this KPI in this department whose own (employee, year) ack
/// is CONFIRMED — ack is per employee-year, not per KPI, so this reflects their overall-year confirmation.</param>
public sealed record ProgressRollupDepartmentRowDto(
    Guid DepartmentId,
    string DepartmentName,
    Guid? DeptYearId,
    decimal Target,
    decimal Achieved,
    decimal AchievedPercent,
    decimal AllocatedToEmployeesPercent,
    int EmployeesTotal,
    int EmployeesConfirmed);

public sealed record ProgressRollupKpiDto(
    Guid KpiId,
    string KpiCode,
    string KpiName,
    string Unit,
    List<ProgressRollupDepartmentRowDto> Departments);

public sealed record ProgressRollupDto(
    Guid BranchYearId,
    int Year,
    List<ProgressRollupKpiDto> Kpis);
