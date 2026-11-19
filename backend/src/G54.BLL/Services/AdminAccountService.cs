using System.Text.RegularExpressions;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Admin;
using G54.BLL.Services.Import.EmployeeOrganization;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed partial class AdminAccountService(
    AppDbContext dbContext,
    IPasswordEncoder passwordEncoder,
    IAuthSessionStore sessionStore) : IAdminAccountService
{
    [GeneratedRegex("^[a-z0-9._@-]{3,50}$", RegexOptions.Compiled)]
    private static partial Regex UsernameRegex();

    public async Task<IReadOnlyList<RoleSummaryDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Roles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .Select(role => new RoleSummaryDto(
                role.Id,
                role.Code,
                role.Name,
                role.Code == RoleCodes.SystemAdmin,
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
            return new UserAccountSummaryDto(
                account.Id,
                account.Username,
                account.Employee.EmployeeCode,
                account.Employee.FullName,
                account.Employee.Department.Code,
                account.Employee.Department.Name,
                account.Employee.JobPosition.Title,
                role?.Id ?? Guid.Empty,
                role?.Name ?? string.Empty,
                ResolveStatus(account));
        }).ToList();

        return new PagedResult<UserAccountSummaryDto>(items, totalCount, page, pageSize);
    }

    public async Task<UserAccountSummaryCountsDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var activeCount = await dbContext.Accounts
            .AsNoTracking()
            .CountAsync(account => !account.IsLocked && account.Employee.Status == EmployeeStatus.Active, cancellationToken);

        var lockedCount = await dbContext.Accounts
            .AsNoTracking()
            .CountAsync(account => account.IsLocked && account.Employee.Status != EmployeeStatus.Terminated, cancellationToken);

        var employeesWithoutAccountCount = await dbContext.Employees
            .AsNoTracking()
            .CountAsync(employee => employee.Status != EmployeeStatus.Terminated && employee.Account == null, cancellationToken);

        return new UserAccountSummaryCountsDto(activeCount, lockedCount, employeesWithoutAccountCount);
    }

    public async Task<IReadOnlyList<EligibleEmployeeDto>> GetEligibleEmployeesAsync(CancellationToken cancellationToken = default)
    {
        var rolesByCode = await dbContext.Roles
            .AsNoTracking()
            .Where(role => role.Status == RecordStatus.Active)
            .ToDictionaryAsync(role => role.Code, role => role.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var employees = await dbContext.Employees
            .AsNoTracking()
            .Include(employee => employee.Department)
            .Include(employee => employee.JobPosition)
            .Where(employee => employee.Status != EmployeeStatus.Terminated && employee.Account == null)
            .OrderBy(employee => employee.EmployeeCode)
            .ToListAsync(cancellationToken);

        return employees.Select(employee =>
        {
            var roleCode = PositionMapping.RoleCodeFor(employee.JobPosition.Code);
            Guid? suggestedRoleId = roleCode is not null && rolesByCode.TryGetValue(roleCode, out var rId) ? rId : null;
            var suggestedUsername = SuggestUsername(employee.Email, employee.EmployeeCode);
            return new EligibleEmployeeDto(
                employee.Id,
                employee.EmployeeCode,
                employee.FullName,
                employee.Department.Code,
                employee.Department.Name,
                employee.JobPosition.Title,
                employee.Email,
                "HRM Sync",
                suggestedRoleId,
                suggestedUsername);
        }).ToList();
    }

    public async Task<UserAccountDetailDto> GetAccountDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.Accounts
            .AsNoTracking()
            .Include(candidate => candidate.Employee)
                .ThenInclude(employee => employee.Department)
            .Include(candidate => candidate.Employee)
                .ThenInclude(employee => employee.JobPosition)
            .Include(candidate => candidate.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            ?? throw new NotFoundException("Tài khoản", id);

        var role = account.UserRoles
            .Select(userRole => userRole.Role)
            .OrderBy(candidate => candidate.Name)
            .FirstOrDefault();

        var suggestedRoleCode = PositionMapping.RoleCodeFor(account.Employee.JobPosition.Code);
        Guid? suggestedRoleId = null;
        if (suggestedRoleCode is not null)
        {
            suggestedRoleId = await dbContext.Roles
                .AsNoTracking()
                .Where(candidate => candidate.Code == suggestedRoleCode)
                .Select(candidate => (Guid?)candidate.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var idText = account.Id.ToString();
        var entityLogs = await dbContext.AuditLogs
            .AsNoTracking()
            .Where(log => log.EntityName == nameof(Account) && log.EntityId == idText)
            .OrderByDescending(log => log.OccurredAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        var authLogs = await dbContext.AuthAuditLogs
            .AsNoTracking()
            .Where(log => log.ActorId == account.Id)
            .OrderByDescending(log => log.OccurredAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        var createLog = entityLogs
            .Where(log => log.Action == "Create")
            .OrderBy(log => log.OccurredAt)
            .FirstOrDefault();

        var passwordChangeLog = entityLogs
            .Where(log => log.Action == "Update" && log.ChangedColumns != null && log.ChangedColumns.Contains("PasswordHash"))
            .OrderByDescending(log => log.OccurredAt)
            .FirstOrDefault();

        var history = entityLogs
            .Select(log => new UserAccountHistoryEntryDto(
                log.OccurredAt,
                FormatAuditEvent(log.Action, log.ChangedColumns),
                string.IsNullOrWhiteSpace(log.ActorName) ? "System" : log.ActorName))
            .Concat(authLogs.Select(log => new UserAccountHistoryEntryDto(
                log.OccurredAt,
                FormatAuthAction(log.Action),
                account.Username)))
            .OrderByDescending(entry => entry.OccurredAt)
            .Take(25)
            .ToList();

        return new UserAccountDetailDto(
            account.Id,
            account.Username,
            account.Employee.EmployeeCode,
            account.Employee.FullName,
            account.Employee.Department.Code,
            account.Employee.JobPosition.Title,
            role?.Id ?? Guid.Empty,
            role?.Name ?? string.Empty,
            ResolveStatus(account),
            account.EmployeeId,
            account.Employee.Department.Name,
            suggestedRoleId,
            createLog?.OccurredAt ?? account.LastSyncAt,
            string.IsNullOrWhiteSpace(createLog?.ActorName) ? "System / HRM Sync" : createLog.ActorName,
            passwordChangeLog?.OccurredAt,
            history);
    }

    public async Task<CreateUserAccountResultDto> CreateAccountAsync(
        CreateUserAccountRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var username = (request.Username ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(username) || !UsernameRegex().IsMatch(username))
        {
            throw new ValidationException("Tên đăng nhập phải từ 3–50 ký tự chữ thường, số hoặc dấu chấm, gạch ngang, gạch dưới.");
        }

        var employee = await dbContext.Employees
            .Include(candidate => candidate.Account)
            .SingleOrDefaultAsync(candidate => candidate.Id == request.EmployeeId, cancellationToken)
            ?? throw new NotFoundException("Nhân sự", request.EmployeeId);

        if (employee.Status == EmployeeStatus.Terminated)
        {
            throw new ValidationException("Không thể tạo tài khoản cho nhân sự đã nghỉ việc.");
        }

        if (employee.Account is not null)
        {
            throw new ConflictException(ErrorCodes.GetDefaultMessage(ErrorCodes.AccountExists), ErrorCodes.AccountExists);
        }

        var roleExists = await dbContext.Roles
            .AsNoTracking()
            .AnyAsync(role => role.Id == request.RoleId && role.Status == RecordStatus.Active, cancellationToken);
        if (!roleExists)
        {
            throw new NotFoundException("Vai trò", request.RoleId);
        }

        var exactPattern = LikePattern.Exact(username);
        var usernameTaken = await dbContext.Accounts
            .AsNoTracking()
            .AnyAsync(account => EF.Functions.ILike(account.Username, exactPattern, LikePattern.Escape), cancellationToken);
        if (usernameTaken)
        {
            throw new ConflictException(ErrorCodes.GetDefaultMessage(ErrorCodes.UsernameExists), ErrorCodes.UsernameExists);
        }

        var now = DateTimeOffset.UtcNow;
        var temporaryPassword = TemporaryPassword.Generate();
        var account = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = username,
            PasswordHash = passwordEncoder.Encode(temporaryPassword),
            MustChangePassword = request.MustChangePassword,
            IsLocked = false,
            LastSyncAt = now,
            UpdatedAt = now,
        };

        dbContext.Accounts.Add(account);
        dbContext.UserRoles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            RoleId = request.RoleId,
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflict.IsUniqueViolation(ex))
        {
            throw new ConflictException(ErrorCodes.GetDefaultMessage(ErrorCodes.UsernameExists), ErrorCodes.UsernameExists);
        }

        var detail = await GetAccountDetailAsync(account.Id, cancellationToken);
        return new CreateUserAccountResultDto(detail, temporaryPassword);
    }

    public async Task<UserAccountDetailDto> UpdateAccountAsync(
        Guid id,
        UpdateUserAccountRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await dbContext.Accounts
            .Include(candidate => candidate.Employee)
            .Include(candidate => candidate.UserRoles)
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            ?? throw new NotFoundException("Tài khoản", id);

        if (account.Employee.Status == EmployeeStatus.Terminated)
        {
            throw new ValidationException("Không thể chỉnh sửa tài khoản của nhân sự đã nghỉ việc.");
        }

        var roleExists = await dbContext.Roles
            .AsNoTracking()
            .AnyAsync(role => role.Id == request.RoleId && role.Status == RecordStatus.Active, cancellationToken);
        if (!roleExists)
        {
            throw new NotFoundException("Vai trò", request.RoleId);
        }

        var currentRoles = account.UserRoles.ToList();
        if (currentRoles.Count != 1 || currentRoles[0].RoleId != request.RoleId)
        {
            dbContext.UserRoles.RemoveRange(currentRoles);
            dbContext.UserRoles.Add(new UserRole
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                RoleId = request.RoleId,
            });
            account.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await GetAccountDetailAsync(account.Id, cancellationToken);
    }

    public async Task<ResetUserPasswordResultDto> ResetPasswordAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var account = await LoadEditableAsync(id, cancellationToken);
        var temporaryPassword = TemporaryPassword.Generate();
        account.PasswordHash = passwordEncoder.Encode(temporaryPassword);
        account.MustChangePassword = true;
        account.FailedLoginAttempts = 0;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        // The old password must stop working everywhere, so every refresh token is dropped.
        await sessionStore.RevokeAllAsync(account.Id, cancellationToken);
        return new ResetUserPasswordResultDto(temporaryPassword);
    }

    public async Task<UserAccountDetailDto> LockAccountAsync(
        Guid actorAccountId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (actorAccountId == id)
        {
            throw new BusinessRuleException(ErrorCodes.CannotLockSelf);
        }

        var account = await LoadEditableAsync(id, cancellationToken);
        if (!account.IsLocked || account.LockedUntil is not null)
        {
            // An open-ended manual lock: LockedUntil stays null so it does not expire by itself.
            account.IsLocked = true;
            account.LockedUntil = null;
            account.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await sessionStore.RevokeAllAsync(account.Id, cancellationToken);
        return await GetAccountDetailAsync(account.Id, cancellationToken);
    }

    public async Task<UserAccountDetailDto> UnlockAccountAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var account = await LoadEditableAsync(id, cancellationToken);
        if (account.IsLocked || account.FailedLoginAttempts != 0)
        {
            account.IsLocked = false;
            account.LockedUntil = null;
            account.FailedLoginAttempts = 0;
            account.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await GetAccountDetailAsync(account.Id, cancellationToken);
    }

    private async Task<Account> LoadEditableAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts
            .Include(candidate => candidate.Employee)
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            ?? throw new NotFoundException("Tài khoản", id);

        if (account.Employee.Status == EmployeeStatus.Terminated)
        {
            throw new ValidationException("Không thể thao tác trên tài khoản của nhân sự đã nghỉ việc.");
        }

        return account;
    }

    private static string ResolveStatus(Account account) =>
        account.Employee.Status == EmployeeStatus.Terminated
            ? "EMPLOYEE_TERMINATED"
            : account.IsLocked ? "LOCKED" : "ACTIVE";

    private static string SuggestUsername(string email, string employeeCode)
    {
        var atIdx = email.IndexOf('@', StringComparison.Ordinal);
        var prefix = atIdx > 0 ? email[..atIdx] : employeeCode;
        var sanitized = Regex.Replace(prefix.ToLowerInvariant(), "[^a-z0-9._-]", "");
        return sanitized.Length >= 3 ? sanitized : employeeCode.ToLowerInvariant();
    }

    private static string FormatAuditEvent(string action, string? changedColumns) =>
        action switch
        {
            "Create" => "Tạo tài khoản",
            "Update" when changedColumns != null && changedColumns.Contains("PasswordHash") => "Đổi mật khẩu",
            "Update" when changedColumns != null && changedColumns.Contains("IsLocked") => "Cập nhật trạng thái khóa",
            "Update" => "Cập nhật tài khoản",
            "Delete" or "SoftDelete" => "Xóa tài khoản",
            _ => action,
        };

    private static string FormatAuthAction(string action) =>
        action switch
        {
            "LOGIN_SUCCESS" => "Đăng nhập thành công",
            "LOGIN_FAILED" => "Đăng nhập thất bại",
            "LOGIN_BLOCKED" => "Đăng nhập bị chặn (tài khoản khóa)",
            "LOGOUT" => "Đăng xuất",
            "PASSWORD_CHANGED" => "Đổi mật khẩu",
            _ => action,
        };
}
