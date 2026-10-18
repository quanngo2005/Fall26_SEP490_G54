namespace G54.BLL.Dtos.Department;

public sealed class DepartmentFilterDto
{
    public string? Keyword { get; set; }

    /// <summary>Wire value: ACTIVE, INACTIVE or ARCHIVED.</summary>
    public string? Status { get; set; }

    /// <summary>Wire value: BRANCH, DEPARTMENT or TEAM.</summary>
    public string? LevelType { get; set; }

    public Guid? ParentDepartmentId { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}
