namespace G54.BLL.Dtos.Department;

public sealed record DepartmentWorkMixDto(
    Guid? WorkTypeId,
    string? Code,
    string? Name,
    int Count);
