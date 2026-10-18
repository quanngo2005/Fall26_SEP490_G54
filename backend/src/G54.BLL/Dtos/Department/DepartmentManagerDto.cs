namespace G54.BLL.Dtos.Department;

public sealed record DepartmentManagerDto(
    Guid Id,
    string EmployeeCode,
    string FullName,
    string JobPositionTitle);
