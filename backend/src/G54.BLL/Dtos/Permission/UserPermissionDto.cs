namespace G54.BLL.Dtos.Permission;

public sealed record UserPermissionDto(
    string Resource,
    string Action,
    string Scope,
    string? Code = null);

public sealed record UserPermissionsResponseDto(
    Guid AccountId,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<UserPermissionDto> Permissions);
