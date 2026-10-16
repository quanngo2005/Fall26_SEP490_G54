using G54.BLL.Dtos.Permission;

namespace G54.BLL.Services;

public interface IPermissionService
{
    Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid accountId, CancellationToken cancellationToken);

    Task<bool> HasPermissionAsync(Guid accountId, string resource, string action, string requiredScope, CancellationToken cancellationToken);

    Task InvalidateUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken);

    Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken);
}
