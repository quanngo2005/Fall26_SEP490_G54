using System.Text.Json;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Sidebar;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace G54.BLL.Services;

public sealed class SidebarConfigurationService(AppDbContext db) : ISidebarConfigurationService
{
    public async Task<IReadOnlyList<SidebarConfigurationDto>> GetAsync(Guid accountId, bool allRoles, CancellationToken cancellationToken)
    {
        var roles = await GetRolesAsync(accountId, cancellationToken);
        if (allRoles && !roles.Contains("admin", StringComparer.Ordinal))
        {
            throw new ForbiddenException();
        }

        var selected = allRoles ? SidebarCatalog.Defaults.Keys.ToArray() : roles;
        var saved = await db.SidebarConfigurations.AsNoTracking()
            .Where(c => selected.Contains(c.Role)).ToDictionaryAsync(c => c.Role, cancellationToken);
        return selected.Select(role => saved.TryGetValue(role, out var config)
            ? new SidebarConfigurationDto(role, config.ItemIds, config.Version, JsonSerializer.Deserialize<Dictionary<string, string>>(config.RoutesJson) ?? [])
            : new SidebarConfigurationDto(role, SidebarCatalog.Defaults[role], 0, new Dictionary<string, string>())).ToArray();
    }

    public async Task<SidebarConfigurationDto> SaveAsync(Guid accountId, string role, SaveSidebarConfigurationDto request, CancellationToken cancellationToken)
    {
        var roles = await GetRolesAsync(accountId, cancellationToken);
        if (!roles.Contains("admin", StringComparer.Ordinal))
        {
            throw new ForbiddenException();
        }

        var ids = request.ItemIds;
        if (!SidebarCatalog.Defaults.ContainsKey(role) || ids is null || ids.Length is < 1 or > 64
            || ids.Any(id => id is null || !SidebarCatalog.ItemIds.Contains(id))
            || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length
            || !ids.Contains("overview", StringComparer.Ordinal)
            || (role == "admin" && !ids.Contains("systemConfiguration", StringComparer.Ordinal))
            || (ids.Any(SidebarCatalog.Children.Contains) && !ids.Contains("organization", StringComparer.Ordinal))
            || request.Version is < 0 or >= int.MaxValue)
        {
            throw new ValidationException("Invalid sidebar configuration.");
        }

        var config = await db.SidebarConfigurations.SingleOrDefaultAsync(c => c.Role == role, cancellationToken);
        if (request.Version != (config?.Version ?? 0))
        {
            throw new ConflictException("Sidebar configuration has changed. Reload before saving.");
        }

        var routes = request.Routes ?? (config is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(config.RoutesJson) ?? []);
        // Older clients may remove an item without sending route overrides.
        if (request.Routes is null)
        {
            routes = routes.Where(pair => ids.Contains(pair.Key, StringComparer.Ordinal)).ToDictionary();
        }
        if (routes.Count > 64 || routes.Any(pair => !ids.Contains(pair.Key, StringComparer.Ordinal)
            || pair.Key == "organization" || pair.Value is null || !SidebarCatalog.Routes.Contains(pair.Value)
            || (pair.Value == "/sidebar-settings" && role != "admin")
            || (pair.Key == "systemConfiguration" && role == "admin" && pair.Value != "/sidebar-settings")))
        {
            throw new ValidationException("Invalid sidebar route configuration.");
        }

        if (config is null)
        {
            config = new SidebarConfiguration { Role = role, CreatedBy = accountId };
            db.SidebarConfigurations.Add(config);
        }

        config.ItemIds = ids.ToArray();
        config.RoutesJson = JsonSerializer.Serialize(routes);
        config.Version++;
        config.UpdatedBy = accountId;
        config.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Sidebar configuration has changed. Reload before saving.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Sidebar configuration has changed. Reload before saving.");
        }

        return new SidebarConfigurationDto(role, config.ItemIds, config.Version, routes);
    }

    private async Task<string[]> GetRolesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking().Include(a => a.Employee)
            .Include(a => a.UserRoles).ThenInclude(r => r.Role)
            .SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null || account.IsLocked || account.Employee.Status != EmployeeStatus.Active)
        {
            throw new ForbiddenException();
        }

        return account.UserRoles.Select(r => SidebarCatalog.MapRole(r.Role.Code))
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
    }
}
