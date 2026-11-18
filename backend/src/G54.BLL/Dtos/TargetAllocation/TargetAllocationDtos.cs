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
    bool IsRatio,
    decimal YearValue,
    decimal Q1,
    decimal Q2,
    decimal Q3,
    decimal Q4,
    QuarterPreviousYearDto? PreviousYear,
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
    bool Matched);

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
