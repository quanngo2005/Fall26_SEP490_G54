namespace G54.BLL.Dtos.Work;

public sealed class WorkFilterDto
{
    /// <summary>Case-insensitive contains on the work code or title.</summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// One or more statuses (<c>?status=New&amp;status=Overdue</c> or <c>?status=New,Overdue</c>), PascalCase or
    /// UPPER_SNAKE, case-insensitive. Empty means every status.
    /// </summary>
    public List<string>? Status { get; set; }

    public Guid? AssigneeEmployeeId { get; set; }

    /// <summary>Works whose assignee belongs to this department (and, by default, its sub-departments).</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>Include the sub-departments of <see cref="DepartmentId"/>. Defaults to <c>true</c>.</summary>
    public bool IncludeChildDepartments { get; set; } = true;

    public Guid? WorkTypeId { get; set; }

    /// <summary>LOW, MEDIUM, HIGH or CRITICAL (any casing).</summary>
    public string? Priority { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

/// <summary>A row of the work list. <c>Priority</c> and <c>Status</c> are PascalCase like <see cref="WorkDetailDto"/>.</summary>
public sealed record WorkListItemDto(
    Guid Id,
    string Code,
    string Title,
    Guid? WorkTypeId,
    string? WorkTypeName,
    Guid? BusinessCampaignId,
    Guid AssigneeEmployeeId,
    string AssigneeEmployeeCode,
    string AssigneeEmployeeName,
    Guid DepartmentId,
    string DepartmentCode,
    string DepartmentName,
    string Priority,
    string Status,
    DateOnly? PlannedStartDate,
    DateOnly? PlannedEndDate,
    DateTimeOffset UpdatedAt);
