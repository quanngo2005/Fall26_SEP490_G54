using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Queue;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G54.BLL.Services;

public sealed class QueueLobbyService
{
    private readonly IUnitOfWork unit;
    private readonly IPermissionService permissions;
    private readonly QueueConnection queue;
    private readonly IConfiguration config;

    internal QueueLobbyService(IUnitOfWork unit, IPermissionService permissions, QueueConnection queue, IConfiguration config)
    {
        this.unit = unit;
        this.permissions = permissions;
        this.queue = queue;
        this.config = config;
    }

    public async Task<LobbyState> ReadAsync(Guid user, int? counter, LobbyUpdate? update, CancellationToken ct)
    {
        var granted = await permissions.GetEffectivePermissionsAsync(user, ct);
        var level = AccessScope.LevelFor(granted, "queue_counter", "supervise");
        var department = await unit.Employees.Query().Where(e => e.Account != null && e.Account.Id == user && !e.Account.IsLocked &&
            e.Status == EmployeeStatus.Active).Select(e => (Guid?)e.DepartmentId).SingleOrDefaultAsync(ct);
        if (level < AccessScope.DeptLevel || department == null) throw new ForbiddenException();
        var tree = await DepartmentHierarchy.LoadAsync(unit.Departments.Query(), ct);
        if (!string.Equals(config["Queue:BranchCode"], tree.Nodes[tree.BranchRoot(department.Value)].Code, StringComparison.Ordinal))
            throw new ForbiddenException();
        var departments = AccessScope.DepartmentsInScope(Math.Min(level, AccessScope.BranchLevel), department.Value, tree)!;
        var staff = await unit.Employees.Query().Where(e => departments.Contains(e.DepartmentId) && e.Status == EmployeeStatus.Active &&
            e.Account != null && !e.Account.IsLocked && e.Account.UserRoles.Any(u => u.Role.Code == "EMPLOYEE") &&
            (e.JobPosition.Code == "SALES" || e.JobPosition.Code == "TELLER") && e.JobPosition.Status == RecordStatus.Active)
            .OrderBy(e => e.FullName).Select(e => new LobbyStaff(e.Id, e.FullName, e.JobPosition.Code)).ToArrayAsync(ct);
        var scope = new { staff = staff.Select(e => e.Id), positions = staff.Select(e => e.Position).Distinct() };
        if (update != null)
        {
            var target = Array.Find(staff, e => e.Id == update.Employee);
            if (update.Employee.HasValue && target == null) throw new ForbiddenException();
            await queue.RequestAsync<bool>(HttpMethod.Post, "/internal/desks",
                new { counter, update.Employee, position = target?.Position, update.Services, update.Locked, update.Revision, scope }, ct);
        }
        var result = await queue.RequestAsync<LobbyState>(HttpMethod.Post, "/internal/lobby", scope, ct);
        return result with
        {
            Staff = staff,
            WaitLimitMinutes = Math.Max(1, config.GetValue("Queue:WaitLimitMinutes", 15)),
            ServiceLimitMinutes = Math.Max(1, config.GetValue("Queue:ServiceLimitMinutes", 20))
        };
    }
}
