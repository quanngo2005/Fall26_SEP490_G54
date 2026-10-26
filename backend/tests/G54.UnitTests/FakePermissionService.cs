using G54.BLL.Dtos.Permission;
using G54.BLL.Services;

namespace G54.UnitTests;

/// <summary>
/// Grants <c>work:read</c> at the given scope to every account (nothing when the scope is null), plus any extra grants.
/// </summary>
internal sealed class FakePermissionService(string? workReadScope, params UserPermissionDto[] grants) : IPermissionService
{
    /// <summary>A grant of <c>employee:read</c> at All, for tests that need an unrestricted caller.</summary>
    public static UserPermissionDto EmployeeReadAll => new("employee", "read", "All", "EMPLOYEE_READ");

    /// <summary>A grant of <c>work:create</c> at the given scope.</summary>
    public static UserPermissionDto WorkCreate(string scope) => new("work", "create", scope, "WORK_CREATE");

    public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<UserPermissionsResponseDto?>(null);

    public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = new List<UserPermissionDto>(grants);
        if (workReadScope is not null)
        {
            result.Add(new UserPermissionDto("work", "read", workReadScope, "WORK_READ"));
        }

        IReadOnlyList<UserPermissionDto> readOnly = result;
        return Task.FromResult(readOnly);
    }

    public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
}
