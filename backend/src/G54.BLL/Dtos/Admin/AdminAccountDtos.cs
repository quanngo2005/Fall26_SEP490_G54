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
    string DepartmentName,
    string? JobPositionTitle,
    Guid RoleId,
    string RoleName,
    string Status);

public sealed record UserAccountHistoryEntryDto(
    DateTimeOffset OccurredAt,
    string Event,
    string Actor);

public sealed record UserAccountDetailDto(
    Guid Id,
    string Username,
    string EmployeeCode,
    string FullName,
    string DepartmentCode,
    string? JobPositionTitle,
    Guid RoleId,
    string RoleName,
    string Status,
    Guid EmployeeId,
    string DepartmentName,
    Guid? SuggestedRoleId,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? PasswordChangedAt,
    IReadOnlyList<UserAccountHistoryEntryDto> History);

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

public sealed record EligibleEmployeeDto(
    Guid Id,
    string EmployeeCode,
    string FullName,
    string DepartmentCode,
    string DepartmentName,
    string? JobPositionTitle,
    string Email,
    string Source,
    Guid? SuggestedRoleId,
    string? SuggestedUsername);

public sealed record CreateUserAccountRequestDto(
    Guid EmployeeId,
    string Username,
    Guid RoleId,
    bool MustChangePassword = true);

public sealed record CreateUserAccountResultDto(
    UserAccountDetailDto Account,
    string TemporaryPassword);

public sealed record UpdateUserAccountRequestDto(
    Guid RoleId);

public sealed record ResetUserPasswordResultDto(
    string TemporaryPassword);
