using System.Linq.Expressions;
using System.Text.RegularExpressions;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Common.Validation;
using G54.BLL.Services.Import.EmployeeOrganization;
using G54.BLL.Dtos.Employee;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed class EmployeeService(
    AppDbContext db,
    ILogger<EmployeeService> logger,
    IPermissionService permissions,
    IPasswordEncoder passwordEncoder) : IEmployeeService
{
    private static readonly Action<ILogger, string, Guid, Exception?> LogEmployeeAudit =
        LoggerMessage.Define<string, Guid>(LogLevel.Information, new EventId(1, "EmployeeAudit"), "Employee audit: Action={Action}, EntityId={EntityId}");

    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private static readonly Expression<Func<Employee, EmployeeDetailDto>> DetailProjection = e => new EmployeeDetailDto(
        e.Id,
        e.EmployeeCode,
        e.FullName,
        e.Email,
        e.Phone,
        e.DepartmentId,
        e.Department.Code,
        e.Department.Name,
        e.JobPositionId,
        e.JobPosition.Code,
        e.JobPosition.Title,
        e.ManagerId,
        e.Manager != null ? e.Manager.EmployeeCode : null,
        e.Manager != null ? e.Manager.FullName : null,
        e.Status.ToEnumString(),
        e.InverseManager.Count,
        e.LastSyncAt,
        e.UpdatedAt,
        e.Account != null ? e.Account.Username : null);

    public async Task<PagedResult<EmployeeDetailDto>> GetEmployeesAsync(
        Guid callerAccountId,
        EmployeeFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var status = EnumApiFormat.ParseEnum<EmployeeStatus>(filter.Status, "status");
        var visible = await ResolveVisibilityAsync(callerAccountId, cancellationToken);
        var query = db.Employees.AsNoTracking().Where(visible);

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            // ILike is PostgreSQL's case-insensitive LIKE; the user's text is escaped so % and _ match literally.
            var pattern = LikePattern.Contains(filter.Keyword.Trim());
            query = query.Where(e =>
                EF.Functions.ILike(e.EmployeeCode, pattern, LikePattern.Escape) ||
                EF.Functions.ILike(e.FullName, pattern, LikePattern.Escape) ||
                EF.Functions.ILike(e.Email, pattern, LikePattern.Escape) ||
                (e.Phone != null && EF.Functions.ILike(e.Phone, pattern, LikePattern.Escape)));
        }

        if (filter.DepartmentId.HasValue)
        {
            query = query.Where(e => e.DepartmentId == filter.DepartmentId.Value);
        }

        if (filter.JobPositionId.HasValue)
        {
            query = query.Where(e => e.JobPositionId == filter.JobPositionId.Value);
        }

        if (filter.ManagerId.HasValue)
        {
            query = query.Where(e => e.ManagerId == filter.ManagerId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(e => e.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(e => e.EmployeeCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(DetailProjection)
            .ToListAsync(cancellationToken);

        return new PagedResult<EmployeeDetailDto>(items, totalCount, page, pageSize);
    }

    /// <summary>Null when the employee does not exist or lies outside the caller's employee:read scope.</summary>
    public async Task<EmployeeDetailDto?> GetEmployeeByIdAsync(
        Guid callerAccountId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var visible = await ResolveVisibilityAsync(callerAccountId, cancellationToken);
        return await db.Employees
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Where(visible)
            .Select(DetailProjection)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Null when the code is unknown or the employee lies outside the caller's employee:read scope.</summary>
    public async Task<EmployeeDetailDto?> GetEmployeeByCodeAsync(
        Guid callerAccountId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var trimmed = code?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var visible = await ResolveVisibilityAsync(callerAccountId, cancellationToken);
        return await db.Employees
            .AsNoTracking()
            .Where(e => e.EmployeeCode == trimmed)
            .Where(visible)
            .Select(DetailProjection)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<EmployeeDetailDto> GetCurrentEmployeeAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new { a.EmployeeId, a.IsLocked, a.Employee.Status })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Tài khoản", accountId);

        if (account.IsLocked || account.Status != EmployeeStatus.Active)
        {
            throw new ForbiddenException();
        }

        return await GetRequiredEmployeeAsync(account.EmployeeId, cancellationToken);
    }

    public async Task<List<DepartmentOptionDto>> GetDepartmentOptionsAsync(
        Guid callerAccountId,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveReadScopeAsync(callerAccountId, cancellationToken);
        var query = db.Departments
            .AsNoTracking()
            .Where(d => d.Status == RecordStatus.Active);

        if (scope.Departments is { } departments)
        {
            query = query.Where(d => departments.Contains(d.Id));
        }

        return await query
            .OrderBy(d => d.Code)
            .Select(d => new DepartmentOptionDto(d.Id, d.Code, d.Name, d.LevelType))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<JobPositionOptionDto>> GetJobPositionOptionsAsync(
        Guid callerAccountId,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveReadScopeAsync(callerAccountId, cancellationToken);
        var query = db.JobPositions
            .AsNoTracking()
            .Where(j => j.Status == RecordStatus.Active);

        if (scope.Departments is not null)
        {
            var positionIds = await db.Employees
                .AsNoTracking()
                .Where(scope.Visible)
                .Select(e => e.JobPositionId)
                .Distinct()
                .ToListAsync(cancellationToken);
            query = query.Where(j => positionIds.Contains(j.Id));
        }

        return await query
            .OrderBy(j => j.Code)
            .Select(j => new JobPositionOptionDto(j.Id, j.Code, j.Title))
            .ToListAsync(cancellationToken);
    }

    public async Task<EmployeeDetailDto> CreateEmployeeAsync(
        CreateEmployeeDto dto,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(dto.EmployeeCode, dto.FullName, dto.Email, dto.Phone, dto.DepartmentId, dto.JobPositionId);
        var status = EnumApiFormat.ParseEnum<EmployeeStatus>(dto.Status, "status") ?? EmployeeStatus.Active;

        var code = dto.EmployeeCode.Trim();
        var email = dto.Email.Trim().ToLowerInvariant();

        await EnsureCodeUniqueAsync(code, null, cancellationToken);
        await EnsureEmailUniqueAsync(email, null, cancellationToken);
        await EnsureDepartmentActiveAsync(dto.DepartmentId, cancellationToken);
        var roleCode = await ResolveAccountRoleCodeAsync(dto.JobPositionId, cancellationToken);
        await EnsureUsernameFreeAsync(email, cancellationToken);

        if (dto.ManagerId.HasValue && dto.ManagerId.Value != Guid.Empty)
        {
            await EnsureManagerExistsAsync(dto.ManagerId.Value, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = dto.FullName.Trim(),
            Email = email,
            Phone = NormalizeOptional(dto.Phone),
            DepartmentId = dto.DepartmentId,
            JobPositionId = dto.JobPositionId,
            ManagerId = dto.ManagerId == Guid.Empty ? null : dto.ManagerId,
            Status = status,
            LastSyncAt = now,
            UpdatedAt = now,
        };

        db.Employees.Add(employee);

        // Same as the employee import: every new employee gets an account (username = email) with a one-time
        // password and the role of the job position. A terminated employee's account is locked (BR-22).
        var temporaryPassword = TemporaryPassword.Generate();
        var account = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = email,
            PasswordHash = passwordEncoder.Encode(temporaryPassword),
            IsLocked = status == EmployeeStatus.Terminated,
        };
        db.Accounts.Add(account);

        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.Code == roleCode, cancellationToken);
        if (role is not null)
        {
            db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = account.Id, RoleId = role.Id });
        }

        await db.SaveChangesAsync(cancellationToken);

        LogEmployeeAudit(logger, "CREATE_EMPLOYEE", employee.Id, null);
        var created = await GetRequiredEmployeeAsync(employee.Id, cancellationToken);
        return created with { TemporaryPassword = temporaryPassword };
    }

    public async Task<EmployeeDetailDto> UpdateEmployeeAsync(
        Guid id,
        UpdateEmployeeDto dto,
        CancellationToken cancellationToken = default)
    {
        // The phone rule is applied below, and only when the phone actually changes, so an employee whose stored
        // number predates the rule can still have their other details edited.
        ValidateInput(dto.EmployeeCode, dto.FullName, dto.Email, phone: null, dto.DepartmentId, dto.JobPositionId);
        var status = EnumApiFormat.ParseRequiredEnum<EmployeeStatus>(dto.Status, "status");

        var employee = await db.Employees.SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new NotFoundException("Nhân viên", id);

        var phone = PhoneRules.Normalize(dto.Phone);
        if (!string.Equals(phone, employee.Phone, StringComparison.Ordinal))
        {
            ValidatePhone(phone);
        }

        var code = dto.EmployeeCode.Trim();
        var email = dto.Email.Trim().ToLowerInvariant();

        await EnsureCodeUniqueAsync(code, id, cancellationToken);
        await EnsureEmailUniqueAsync(email, id, cancellationToken);
        await EnsureDepartmentActiveAsync(dto.DepartmentId, cancellationToken);
        await EnsureJobPositionActiveAsync(dto.JobPositionId, cancellationToken);

        var managerId = dto.ManagerId == Guid.Empty ? null : dto.ManagerId;
        if (managerId.HasValue)
        {
            if (managerId.Value == id)
            {
                throw new ValidationException("Nhân viên không thể tự làm quản lý của chính mình.");
            }

            await EnsureManagerExistsAsync(managerId.Value, cancellationToken);
            await EnsureNoCircularManagerHierarchyAsync(id, managerId.Value, cancellationToken);
        }

        employee.EmployeeCode = code;
        employee.FullName = dto.FullName.Trim();
        employee.Email = email;
        employee.Phone = phone;
        employee.DepartmentId = dto.DepartmentId;
        employee.JobPositionId = dto.JobPositionId;
        employee.ManagerId = managerId;
        employee.Status = status;
        employee.UpdatedAt = DateTimeOffset.UtcNow;
        await LockAccountIfTerminatedAsync(id, status, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        LogEmployeeAudit(logger, "UPDATE_EMPLOYEE", id, null);
        return await GetRequiredEmployeeAsync(id, cancellationToken);
    }

    public async Task ChangeStatusAsync(
        Guid id,
        EmployeeStatusDto dto,
        CancellationToken cancellationToken = default)
    {
        var status = EnumApiFormat.ParseRequiredEnum<EmployeeStatus>(dto.Status, "status");

        var employee = await db.Employees.SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new NotFoundException("Nhân viên", id);

        employee.Status = status;
        employee.UpdatedAt = DateTimeOffset.UtcNow;
        await LockAccountIfTerminatedAsync(id, status, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        LogEmployeeAudit(logger, "CHANGE_EMPLOYEE_STATUS", id, null);
    }

    /// <summary>
    /// Employees are never removed: "delete" moves the employee to TERMINATED and locks the account, exactly as an
    /// import row with status TERMINATED does. Calling it again on a terminated employee changes nothing.
    /// </summary>
    public async Task DeleteEmployeeAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var employee = await db.Employees.SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new NotFoundException("Nhân viên", id);

        if (employee.Status != EmployeeStatus.Terminated)
        {
            employee.Status = EmployeeStatus.Terminated;
            employee.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await LockAccountIfTerminatedAsync(id, EmployeeStatus.Terminated, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        LogEmployeeAudit(logger, "TERMINATE_EMPLOYEE", id, null);
    }

    /// <summary>BR-22: a terminated employee's account is locked. Like the import, this never unlocks an account.</summary>
    private async Task LockAccountIfTerminatedAsync(Guid employeeId, EmployeeStatus status, CancellationToken cancellationToken)
    {
        if (status != EmployeeStatus.Terminated)
        {
            return;
        }

        var account = await db.Accounts.SingleOrDefaultAsync(a => a.EmployeeId == employeeId, cancellationToken);
        if (account is not null)
        {
            account.IsLocked = true;
        }
    }

    /// <summary>Validates the job position and returns the role its account must get (same mapping as the import).</summary>
    private async Task<string> ResolveAccountRoleCodeAsync(Guid jobPositionId, CancellationToken cancellationToken)
    {
        await EnsureJobPositionActiveAsync(jobPositionId, cancellationToken);

        var positionCode = await db.JobPositions.AsNoTracking()
            .Where(j => j.Id == jobPositionId)
            .Select(j => j.Code)
            .SingleAsync(cancellationToken);

        return PositionMapping.RoleCodeFor(positionCode)
            ?? throw new ValidationException($"Vị trí công việc '{positionCode}' chưa được ánh xạ sang vai trò, không thể tạo tài khoản cho nhân viên.");
    }

    private async Task EnsureUsernameFreeAsync(string username, CancellationToken cancellationToken)
    {
        if (await db.Accounts.AnyAsync(a => a.Username == username, cancellationToken))
        {
            throw new ConflictException($"Tên đăng nhập '{username}' đã tồn tại trong hệ thống.");
        }
    }

    private async Task EnsureCodeUniqueAsync(string code, Guid? excludeId, CancellationToken cancellationToken)
    {
        var exists = await db.Employees.AnyAsync(
            e => e.EmployeeCode == code && (!excludeId.HasValue || e.Id != excludeId.Value),
            cancellationToken);

        if (exists)
        {
            throw new ConflictException($"Mã nhân viên '{code}' đã tồn tại trong hệ thống.");
        }
    }

    private async Task EnsureEmailUniqueAsync(string email, Guid? excludeId, CancellationToken cancellationToken)
    {
        var exists = await db.Employees.AnyAsync(
            e => e.Email == email && (!excludeId.HasValue || e.Id != excludeId.Value),
            cancellationToken);

        if (exists)
        {
            throw new ConflictException($"Địa chỉ email '{email}' đã được sử dụng bởi nhân viên khác.");
        }
    }

    private async Task EnsureDepartmentActiveAsync(Guid departmentId, CancellationToken cancellationToken)
    {
        var department = await db.Departments
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == departmentId, cancellationToken);

        if (department is null)
        {
            throw new NotFoundException("Phòng ban", departmentId);
        }

        if (department.Status != RecordStatus.Active)
        {
            throw new ValidationException($"Phòng ban '{department.Name}' hiện không ở trạng thái hoạt động.");
        }
    }

    private async Task EnsureJobPositionActiveAsync(Guid jobPositionId, CancellationToken cancellationToken)
    {
        var position = await db.JobPositions
            .AsNoTracking()
            .SingleOrDefaultAsync(j => j.Id == jobPositionId, cancellationToken);

        if (position is null)
        {
            throw new NotFoundException("Vị trí công việc", jobPositionId);
        }

        if (position.Status != RecordStatus.Active)
        {
            throw new ValidationException($"Vị trí công việc '{position.Title}' hiện không ở trạng thái hoạt động.");
        }
    }

    private async Task EnsureManagerExistsAsync(Guid managerId, CancellationToken cancellationToken)
    {
        var managerExists = await db.Employees
            .AsNoTracking()
            .AnyAsync(e => e.Id == managerId, cancellationToken);

        if (!managerExists)
        {
            throw new NotFoundException("Người quản lý", managerId);
        }
    }

    private async Task EnsureNoCircularManagerHierarchyAsync(Guid employeeId, Guid targetManagerId, CancellationToken cancellationToken)
    {
        var current = targetManagerId;
        var visited = new HashSet<Guid> { employeeId };

        while (current != Guid.Empty)
        {
            if (visited.Contains(current))
            {
                throw new ValidationException("Cấu trúc quản lý bị vòng lặp. Không thể chọn cấp dưới làm người quản lý trực tiếp.");
            }

            visited.Add(current);

            var parentManagerId = await db.Employees
                .AsNoTracking()
                .Where(e => e.Id == current)
                .Select(e => e.ManagerId)
                .SingleOrDefaultAsync(cancellationToken);

            if (!parentManagerId.HasValue)
            {
                break;
            }

            current = parentManagerId.Value;
        }
    }

    private async Task<EmployeeDetailDto> GetRequiredEmployeeAsync(Guid id, CancellationToken cancellationToken)
    {
        return await db.Employees
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(DetailProjection)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Nhân viên", id);
    }

    /// <summary>
    /// The employees the caller may read: everyone (All), the caller's department subtree (Dept, Branch widens to the
    /// nearest branch), or only the caller (Own). Callers without employee:read are rejected.
    /// </summary>
    private async Task<Expression<Func<Employee, bool>>> ResolveVisibilityAsync(Guid callerAccountId, CancellationToken cancellationToken) =>
        (await ResolveReadScopeAsync(callerAccountId, cancellationToken)).Visible;

    /// <summary>The employees the caller may read and the departments those employees belong to (null = every department).</summary>
    private async Task<ReadScope> ResolveReadScopeAsync(Guid callerAccountId, CancellationToken cancellationToken)
    {
        var granted = await permissions.GetEffectivePermissionsAsync(callerAccountId, cancellationToken);
        var level = AccessScope.LevelFor(granted, "employee", "read");
        if (level < AccessScope.OwnLevel)
        {
            throw new ForbiddenException();
        }

        if (level >= AccessScope.AllLevel)
        {
            return new ReadScope(_ => true, null);
        }

        var caller = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == callerAccountId && !a.IsLocked && a.Employee.Status == EmployeeStatus.Active)
            .Select(a => new { a.EmployeeId, a.Employee.DepartmentId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ForbiddenException();

        var selfEmployeeId = caller.EmployeeId;
        if (level < AccessScope.DeptLevel)
        {
            return new ReadScope(e => e.Id == selfEmployeeId, [caller.DepartmentId]);
        }

        var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments, cancellationToken);
        var departments = AccessScope.DepartmentsInScope(level, caller.DepartmentId, hierarchy) ?? new HashSet<Guid>();
        var optionDepartments = new HashSet<Guid>(departments) { caller.DepartmentId };
        return new ReadScope(e => e.Id == selfEmployeeId || departments.Contains(e.DepartmentId), optionDepartments);
    }

    private sealed record ReadScope(Expression<Func<Employee, bool>> Visible, HashSet<Guid>? Departments);

    private static void ValidatePhone(string? phone)
    {
        var normalized = PhoneRules.Normalize(phone);
        if (normalized is not null && !PhoneRules.IsValid(normalized))
        {
            throw new ValidationException("Số điện thoại phải gồm đúng 10 chữ số và bắt đầu bằng 0.");
        }
    }

    private static void ValidateInput(string employeeCode, string fullName, string email, string? phone, Guid departmentId, Guid jobPositionId)
    {
        if (string.IsNullOrWhiteSpace(employeeCode) || employeeCode.Trim().Length > 50)
        {
            throw new ValidationException("Mã nhân viên là bắt buộc và không quá 50 ký tự.");
        }

        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > 200)
        {
            throw new ValidationException("Họ và tên nhân viên là bắt buộc và không quá 200 ký tự.");
        }

        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 255 || !EmailRegex.IsMatch(email.Trim()))
        {
            throw new ValidationException("Email là bắt buộc và phải có định dạng hợp lệ, tối đa 255 ký tự.");
        }

        ValidatePhone(phone);

        if (departmentId == Guid.Empty)
        {
            throw new ValidationException("Phòng ban (DepartmentId) là bắt buộc.");
        }

        if (jobPositionId == Guid.Empty)
        {
            throw new ValidationException("Vị trí công việc (JobPositionId) là bắt buộc.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
