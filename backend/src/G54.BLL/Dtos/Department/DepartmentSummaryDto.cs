namespace G54.BLL.Dtos.Department;

public sealed record DepartmentSummaryDto(
    Guid Id,
    string Code,
    string Name,
    string LevelType,
    string Status,
    Guid? ParentDepartmentId,
    string? ParentDepartmentName,
    int EmployeeCount,
    int ChildDepartmentCount);
