namespace G54.BLL.Dtos.Department;

public sealed class DepartmentMemberFilterDto
{
    public string? Keyword { get; set; }

    public Guid? JobPositionId { get; set; }

    /// <summary>Wire value: ACTIVE, INACTIVE, ON_LEAVE or TERMINATED.</summary>
    public string? Status { get; set; }

    public Guid? PeriodId { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}
