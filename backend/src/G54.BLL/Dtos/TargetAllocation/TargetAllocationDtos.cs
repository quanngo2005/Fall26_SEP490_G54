namespace G54.BLL.Dtos.TargetAllocation;

// =========================================================================
// Target Allocation DTOs (docs/api/TARGET_ALLOCATION_CONTRACT.md)
// =========================================================================

public sealed record TargetImportRowErrorDto(
    int RowNumber,
    string? KpiName,
    string Message);

public sealed record TargetImportResultDto(
    Guid BranchId,
    int Year,
    List<TargetNodeDto> Targets);

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
