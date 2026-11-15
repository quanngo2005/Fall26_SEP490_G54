using G54.BLL.Common.Models;

namespace G54.BLL.Dtos.Admin;

public sealed record RoleOptionDto(
    Guid Id,
    string Code,
    string Name,
    bool IsSystem);

public sealed record RoleSummaryDto(
    Guid Id,
    string Code,
    string Name,
    bool IsSystem,
    string? Description,
    int AccountCount,
    string Status);

public sealed record UserAccountSummaryDto(
    Guid Id,
    string Username,
    string EmployeeCode,
    string FullName,
    string DepartmentCode,
    string? JobPositionTitle,
    Guid RoleId,
    string RoleName,
    string Status,
    DateTimeOffset? LastLoginAt);

public sealed record UserAccountFilterDto(
    string? Keyword = null,
    Guid? RoleId = null,
    Guid? DepartmentId = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20);

public sealed record UserAccountSummaryCountsDto(
    int ActiveCount,
    int LockedCount,
    int EmployeesWithoutAccountCount);

public sealed record UserAccountListDto(
    PagedResult<UserAccountSummaryDto> Items,
    UserAccountSummaryCountsDto Counts);
