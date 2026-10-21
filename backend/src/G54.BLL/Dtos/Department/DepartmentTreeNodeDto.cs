namespace G54.BLL.Dtos.Department;

/// <summary>One node of the department tree. Counts exclude TERMINATED employees.</summary>
public sealed record DepartmentTreeNodeDto(
    Guid Id,
    string Code,
    string Name,
    string LevelType,
    string Status,
    int DirectEmployeeCount,
    int EmployeeCount,
    IReadOnlyList<DepartmentTreeNodeDto> Children);
