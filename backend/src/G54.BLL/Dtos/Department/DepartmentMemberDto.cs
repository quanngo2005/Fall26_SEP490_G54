namespace G54.BLL.Dtos.Department;

public sealed record DepartmentMemberDto(
    Guid Id,
    string EmployeeCode,
    string FullName,
    Guid DepartmentId,
    string DepartmentName,
    Guid JobPositionId,
    string JobPositionTitle,
    string Status,
    int OpenWorkCount,
    decimal? PerformanceScore);
