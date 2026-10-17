using G54.DAL.Entities;

namespace G54.BLL.Dtos.Employee;

public sealed record EmployeeDetailDto(
    Guid Id,
    string EmployeeCode,
    string FullName,
    string Email,
    string? Phone,
    Guid DepartmentId,
    string DepartmentCode,
    string DepartmentName,
    Guid JobPositionId,
    string JobPositionCode,
    string JobPositionTitle,
    Guid? ManagerId,
    string? ManagerCode,
    string? ManagerName,
    EmployeeStatus Status,
    int DirectReportsCount,
    DateTimeOffset LastSyncAt,
    DateTimeOffset UpdatedAt);
