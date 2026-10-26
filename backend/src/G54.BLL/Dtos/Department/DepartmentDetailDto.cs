namespace G54.BLL.Dtos.Department;

public sealed record DepartmentDetailDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string LevelType,
    string Status,
    Guid? ParentDepartmentId,
    IReadOnlyList<DepartmentRefDto> Ancestors,
    DepartmentManagerDto? Manager,
    int DirectEmployeeCount,
    int EmployeeCount,
    int ChildDepartmentCount,
    DateTimeOffset LastSyncAt,
    DateTimeOffset UpdatedAt);
