using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.TargetAssignment;

/// <summary>Root target for a branch. Contract: docs/api/TARGET_ASSIGNMENT_CONTRACT.md.</summary>
public sealed class CreateTargetAssignmentDto
{
    [Required(ErrorMessage = "KPI là bắt buộc.")]
    public Guid KpiDefinitionId { get; set; }

    [Required(ErrorMessage = "Kỳ đánh giá là bắt buộc.")]
    public Guid EvaluationPeriodId { get; set; }

    /// <summary>The branch receiving the target.</summary>
    [Required(ErrorMessage = "Chi nhánh là bắt buộc.")]
    public Guid? DepartmentId { get; set; }

    [Required(ErrorMessage = "Giá trị target là bắt buộc.")]
    public decimal? TargetValue { get; set; }

    [StringLength(1000, ErrorMessage = "Ghi chú không vượt quá 1000 ký tự.")]
    public string? ChangeReason { get; set; }
}

/// <summary>Splits an ACTIVE department target across the level directly below it, in one transaction.</summary>
public sealed class AllocateTargetDto
{
    [Required(ErrorMessage = "Danh sách phân bổ là bắt buộc.")]
    public List<TargetAllocationItemDto>? Allocations { get; set; }

    [StringLength(1000, ErrorMessage = "Ghi chú không vượt quá 1000 ký tự.")]
    public string? ChangeReason { get; set; }
}

public sealed class TargetAllocationItemDto
{
    /// <summary>Exactly one of <see cref="DepartmentId"/> / <see cref="EmployeeId"/> must be set.</summary>
    public Guid? DepartmentId { get; set; }

    public Guid? EmployeeId { get; set; }

    [Required(ErrorMessage = "Giá trị target là bắt buộc.")]
    public decimal? TargetValue { get; set; }
}

public sealed record TargetAllocationResultDto(
    TargetAssignmentDto Parent,
    IReadOnlyList<TargetAssignmentDto> Children);

public sealed record TargetAssignmentDto(
    Guid Id,
    Guid KpiDefinitionId,
    string KpiCode,
    string KpiName,
    string KpiUnit,
    string KpiAggregationType,
    Guid EvaluationPeriodId,
    string PeriodCode,
    string PeriodName,
    string OwnerType,
    Guid? DepartmentId,
    string? DepartmentCode,
    string? DepartmentName,
    string? DepartmentLevelType,
    Guid? EmployeeId,
    string? EmployeeCode,
    string? EmployeeName,
    Guid? ParentAssignmentId,
    string? SplitDimension,
    int Version,
    decimal TargetValue,
    decimal? AllocatedValue,
    decimal? RemainingValue,
    string Status,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? ApprovedAt,
    string? ChangeReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
