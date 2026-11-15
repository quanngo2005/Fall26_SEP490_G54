using System.Text.Json.Serialization;
using G54.DAL.Entities;

namespace G54.BLL.Dtos.Employee;

/// <summary>`Status` is the UPPER_SNAKE API value of <see cref="EmployeeStatus"/>: ACTIVE, INACTIVE, ON_LEAVE, TERMINATED.</summary>
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
    string Status,
    int DirectReportsCount,
    DateTimeOffset LastSyncAt,
    DateTimeOffset UpdatedAt,
    string? Username = null)
{
    /// <summary>Only set in the response of POST /employees: the one-time password of the account created with the employee.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemporaryPassword { get; init; }
}
