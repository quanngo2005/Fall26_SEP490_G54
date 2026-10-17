using System.Text.Json;
using G54.BLL.Common.Constants;
using G54.BLL.Dtos.Permission;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace G54.BLL.Services;

public sealed class PermissionService(
    AppDbContext dbContext,
    IDistributedCache cache) : IPermissionService
{
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(30);

    public static string GetCacheKey(Guid accountId) => $"auth:permissions:{accountId}";

    public async Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts
            .AsNoTracking()
            .AsSplitQuery()
            .Include(a => a.Employee)
            .Include(a => a.UserRoles)
                .ThenInclude(ur => ur.Role)
            .SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account is null || account.IsLocked || account.Employee.Status != EmployeeStatus.Active)
        {
            return null;
        }

        var roles = account.UserRoles
            .Select(ur => ur.Role.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var effectivePermissions = await GetEffectivePermissionsAsync(accountId, cancellationToken);

        return new UserPermissionsResponseDto(
            account.Id,
            account.Employee.Email,
            account.Employee.FullName,
            roles,
            effectivePermissions);
    }

    public async Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var cacheKey = GetCacheKey(accountId);
        var cachedJson = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cachedJson))
        {
            try
            {
                var cachedPermissions = JsonSerializer.Deserialize<List<UserPermissionDto>>(cachedJson);
                if (cachedPermissions is not null)
                {
                    return cachedPermissions;
                }
            }
            catch (JsonException)
            {
                // Invalidate corrupted cache entry
                await cache.RemoveAsync(cacheKey, cancellationToken);
            }
        }

        var account = await dbContext.Accounts
            .AsNoTracking()
            .AsSplitQuery()
            .Include(a => a.Employee)
            .Include(a => a.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account is null || account.IsLocked || account.Employee.Status != EmployeeStatus.Active)
        {
            return [];
        }

        var isAdmin = account.UserRoles.Any(ur =>
            string.Equals(ur.Role.Code, "ADMIN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ur.Role.Code, "Admin", StringComparison.OrdinalIgnoreCase));

        var permissionDict = new Dictionary<string, UserPermissionDto>(StringComparer.OrdinalIgnoreCase);

        if (isAdmin)
        {
            permissionDict["*:*"] = new UserPermissionDto(
                Resource: "*",
                Action: "*",
                Scope: PermissionScopes.All,
                Code: "FULL_ACCESS");
        }

        var rolePermissions = account.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .ToList();

        foreach (var rp in rolePermissions)
        {
            if (rp.Permission is null)
            {
                continue;
            }

            var key = $"{rp.Permission.Resource}:{rp.Permission.Action}";
            var candidateScope = isAdmin ? PermissionScopes.All : rp.Scope;

            if (permissionDict.TryGetValue(key, out var existing))
            {
                if (PermissionScopes.GetScopeLevel(candidateScope) > PermissionScopes.GetScopeLevel(existing.Scope))
                {
                    permissionDict[key] = new UserPermissionDto(
                        Resource: rp.Permission.Resource,
                        Action: rp.Permission.Action,
                        Scope: candidateScope,
                        Code: rp.Permission.Code);
                }
            }
            else
            {
                permissionDict[key] = new UserPermissionDto(
                    Resource: rp.Permission.Resource,
                    Action: rp.Permission.Action,
                    Scope: candidateScope,
                    Code: rp.Permission.Code);
            }
        }

        var result = permissionDict.Values.OrderBy(p => p.Resource).ThenBy(p => p.Action).ToList();

        var serialized = JsonSerializer.Serialize(result);
        await cache.SetStringAsync(
            cacheKey,
            serialized,
            new DistributedCacheEntryOptions { SlidingExpiration = DefaultCacheTtl },
            cancellationToken);

        return result;
    }

    public async Task<bool> HasPermissionAsync(
        Guid accountId,
        string resource,
        string action,
        string requiredScope,
        CancellationToken cancellationToken)
    {
        var effective = await GetEffectivePermissionsAsync(accountId, cancellationToken);

        if (effective.Any(p => (p.Resource == "*" && p.Action == "*") || p.Code == "FULL_ACCESS"))
        {
            return true;
        }

        var matching = effective.FirstOrDefault(p =>
            p.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase) &&
            p.Action.Equals(action, StringComparison.OrdinalIgnoreCase));

        if (matching is null)
        {
            return false;
        }

        return PermissionScopes.IsSatisfied(matching.Scope, requiredScope);
    }

    public async Task InvalidateUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(GetCacheKey(accountId), cancellationToken);
    }

    public async Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var accountIds = await dbContext.UserRoles
            .AsNoTracking()
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.AccountId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var accId in accountIds)
        {
            await cache.RemoveAsync(GetCacheKey(accId), cancellationToken);
        }
    }
}
