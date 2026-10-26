namespace G54.BLL.Dtos.Department;

/// <summary>
/// One row of the department list. <see cref="EmployeeCount"/>, <see cref="TellerCount"/> and
/// <see cref="SalesCount"/> aggregate the whole subtree over ACTIVE employees only (counts are 0 when nobody matches);
/// <see cref="ManagerName"/> is null when no manager can be derived.
/// </summary>
public sealed record DepartmentSummaryDto(
    Guid Id,
    string Code,
    string Name,
    string LevelType,
    string Status,
    Guid? ParentDepartmentId,
    string? ParentDepartmentName,
    int EmployeeCount,
    int ChildDepartmentCount,
    string? ManagerName,
    int TellerCount,
    int SalesCount,
    DateTimeOffset LastSyncAt);
