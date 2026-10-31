using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.TargetAssignment;

/// <summary>Root target for a branch. Contract: docs/api/TARGET_ASSIGNMENT_CONTRACT.md.</summary>
public sealed class CreateTargetAssignmentDto
{
    [Required(ErrorMessage = "KPI là bắt buộc.")]
    public Guid? KpiDefinitionId { get; set; }

    [Required(ErrorMessage = "Kỳ đánh giá là bắt buộc.")]
    public Guid? EvaluationPeriodId { get; set; }

    /// <summary>The branch receiving the target.</summary>
    [Required(ErrorMessage = "Chi nhánh là bắt buộc.")]
    public Guid? DepartmentId { get; set; }

    [Required(ErrorMessage = "Giá trị target là bắt buộc.")]
    public decimal? TargetValue { get; set; }

    [StringLength(1000, ErrorMessage = "Ghi chú không vượt quá 1000 ký tự.")]
    public string? ChangeReason { get; set; }
}

/// <summary>Splits an ACTIVE department target across the level directly below it.</summary>
public sealed class AllocateTargetDto
{
    [Required(ErrorMessage = "Danh sách phân bổ là bắt buộc.")]
    [MinLength(1, ErrorMessage = "Danh sách phân bổ phải có ít nhất một đối tượng.")]
    public List<TargetAllocationItemDto>? Allocations { get; set; }

    [StringLength(1000, ErrorMessage = "Ghi chú không vượt quá 1000 ký tự.")]
    public string? ChangeReason { get; set; }
}

public sealed class TargetAllocationItemDto
{
    /// <summary>Exactly one of <see cref="DepartmentId"/> / <see cref="EmployeeId"/> is set.</summary>
    public Guid? DepartmentId { get; set; }

    public Guid? EmployeeId { get; set; }

    [Required(ErrorMessage = "Giá trị target là bắt buộc.")]
    public decimal? TargetValue { get; set; }
}

/// <summary>Splits a target across evaluation periods (e.g., year to quarters).</summary>
public sealed class AllocateTargetByPeriodDto
{
    [Required(ErrorMessage = "Danh sách phân bổ là bắt buộc.")]
    [MinLength(1, ErrorMessage = "Danh sách phân bổ phải có ít nhất một đối tượng.")]
    public List<TargetPeriodAllocationItemDto>? Allocations { get; set; }

    [StringLength(1000, ErrorMessage = "Ghi chú không vượt quá 1000 ký tự.")]
    public string? ChangeReason { get; set; }
}

public sealed class TargetPeriodAllocationItemDto
{
    [Required(ErrorMessage = "Kỳ đánh giá là bắt buộc.")]
    public Guid? EvaluationPeriodId { get; set; }

    [Required(ErrorMessage = "Giá trị target là bắt buộc.")]
    public decimal? TargetValue { get; set; }
}

/// <summary>Query parameters for listing target assignments.</summary>
public sealed class TargetAssignmentQueryDto
{
    public Guid? KpiDefinitionId { get; set; }

    public Guid? EvaluationPeriodId { get; set; }

    public Guid? DepartmentId { get; set; }

    public string? Status { get; set; }
}

public sealed record TargetAssignmentDto(
    Guid Id,
    Guid KpiDefinitionId,
    Guid EvaluationPeriodId,
    Guid? DepartmentId,
    Guid? EmployeeId,
    Guid? ParentAssignmentId,
    string? SplitDimension,
    int Version,
    decimal TargetValue,
    string Status,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? ApprovedAt,
    string? ChangeReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
