namespace G54.BLL.Dtos.Department;

public sealed class DepartmentMemberFilterDto
{
    public string? Keyword { get; set; }

    public Guid? JobPositionId { get; set; }

    /// <summary>API value: ACTIVE, INACTIVE, ON_LEAVE or TERMINATED.</summary>
    public string? Status { get; set; }

    public Guid? PeriodId { get; set; }

    /// <summary>API value: PREVIOUS (default) or SAME_LAST_YEAR.</summary>
    public string? Compare { get; set; }

    /// <summary>API value: CHANGE_DESC, TOTAL_ASC or CODE, applied inside each job position; omitted = employee code order.</summary>
    public string? Sort { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}
