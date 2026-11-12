using G54.BLL.Common;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Admin;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class AdminAccountService(AppDbContext dbContext) : IAdminAccountService
{
    public async Task<IReadOnlyList<RoleSummaryDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Roles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .Select(role => new RoleSummaryDto(
                role.Id,
                role.Code,
                role.Name,
                role.Code == "ADMIN" || role.Code == "SYSTEM_ADMIN",
                role.Description,
                role.UserRoles.Count,
                role.Status == RecordStatus.Active ? "Active" : "Inactive"))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<UserAccountSummaryDto>> GetAccountsAsync(
        UserAccountFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var query = dbContext.Accounts
            .AsNoTracking()
            .Include(account => account.Employee)
                .ThenInclude(employee => employee.Department)
            .Include(account => account.Employee)
                .ThenInclude(employee => employee.JobPosition)
            .Include(account => account.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            // ILike is PostgreSQL's case-insensitive LIKE; the user's text is escaped so % and _ match literally.
            var pattern = LikePattern.Contains(filter.Keyword.Trim());
            query = query.Where(account =>
                EF.Functions.ILike(account.Username, pattern, LikePattern.Escape)
                || EF.Functions.ILike(account.Employee.EmployeeCode, pattern, LikePattern.Escape)
                || EF.Functions.ILike(account.Employee.FullName, pattern, LikePattern.Escape));
        }

        if (filter.RoleId.HasValue)
        {
            query = query.Where(account => account.UserRoles.Any(userRole => userRole.RoleId == filter.RoleId));
        }

        if (filter.DepartmentId.HasValue)
        {
            query = query.Where(account => account.Employee.DepartmentId == filter.DepartmentId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query = filter.Status.ToUpperInvariant() switch
            {
                "LOCKED" => query.Where(account => account.IsLocked
                    && account.Employee.Status != EmployeeStatus.Terminated),
                "ACTIVE" => query.Where(account => !account.IsLocked
                    && account.Employee.Status == EmployeeStatus.Active),
                "EMPLOYEE_TERMINATED" => query.Where(account => account.Employee.Status == EmployeeStatus.Terminated),
                _ => query.Where(_ => false),
            };
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var accounts = await query
            .OrderBy(account => account.Employee.EmployeeCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = accounts.Select(account =>
        {
            var role = account.UserRoles
                .Select(userRole => userRole.Role)
                .OrderBy(candidate => candidate.Name)
                .FirstOrDefault();
            var status = account.Employee.Status == EmployeeStatus.Terminated
                ? "EMPLOYEE_TERMINATED"
                : account.IsLocked ? "LOCKED" : "ACTIVE";
            return new UserAccountSummaryDto(
                account.Id,
                account.Username,
                account.Employee.EmployeeCode,
                account.Employee.FullName,
                account.Employee.Department.Code,
                account.Employee.JobPosition.Title,
                role?.Id ?? Guid.Empty,
                role?.Name ?? string.Empty,
                status,
                null);
        }).ToList();

        return new PagedResult<UserAccountSummaryDto>(items, totalCount, page, pageSize);
    }

}
