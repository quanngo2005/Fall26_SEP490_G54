using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.PerformanceDashboard;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed partial class PerformanceDashboardService(
    AppDbContext db,
    IPermissionService permissions,
    TimeProvider time) : IPerformanceDashboardService
{
    private const string PermissionResource = "evaluation";
    private const string PermissionAction = "read";
    private const int OwnScopeLevel = 1;
    private const int DepartmentScopeLevel = 2;
    private const int BranchScopeLevel = 3;
    private const int AllScopeLevel = 4;

    public async Task<PersonalPerformanceDashboardDto> GetMyDashboardAsync(
        Guid accountId,
        Guid? periodId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(accountId, cancellationToken);
        return await BuildPersonalAsync(caller.EmployeeId, periodId, cancellationToken);
    }

    public async Task<PersonalPerformanceDashboardDto> GetEmployeeDashboardAsync(
        Guid accountId,
        Guid employeeId,
        Guid? periodId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(accountId, cancellationToken);

        if (employeeId != caller.EmployeeId)
        {
            var employeeDepartmentId = await db.Employees.AsNoTracking()
                .Where(e => e.Id == employeeId)
                .Select(e => (Guid?)e.DepartmentId)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Nhân viên", employeeId);

            var org = await LoadOrgAsync(cancellationToken);
            EnsureVisible(ResolveVisible(caller, org), employeeDepartmentId, "Bạn không có quyền xem hiệu suất của nhân viên này.");
        }

        return await BuildPersonalAsync(employeeId, periodId, cancellationToken);
    }

    public async Task<UnitPerformanceDashboardDto> GetDepartmentDashboardAsync(
        Guid accountId,
        Guid departmentId,
        Guid? periodId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(accountId, cancellationToken);
        var org = await LoadOrgAsync(cancellationToken);

        if (!org.Nodes.ContainsKey(departmentId))
        {
            throw new NotFoundException("Phòng ban", departmentId);
        }

        EnsureVisible(ResolveVisible(caller, org), departmentId, "Bạn không có quyền xem phòng ban này.");
        return await BuildUnitAsync(org, departmentId, periodId, cancellationToken);
    }

    public async Task<UnitPerformanceDashboardDto> GetBranchDashboardAsync(
        Guid accountId,
        Guid branchId,
        Guid? periodId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(accountId, cancellationToken);
        var org = await LoadOrgAsync(cancellationToken);

        if (!org.Nodes.TryGetValue(branchId, out var node) || node.Level != DepartmentLevelType.Branch)
        {
            throw new NotFoundException("Chi nhánh", branchId);
        }

        EnsureVisible(ResolveVisible(caller, org), branchId, "Bạn không có quyền xem chi nhánh này.");
        return await BuildUnitAsync(org, branchId, periodId, cancellationToken);
    }

    // ----- Authorization --------------------------------------------------------------------

    private async Task<CallerInfo> ResolveCallerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId && !a.IsLocked && a.Employee.Status == EmployeeStatus.Active)
            .Select(a => new { a.EmployeeId, a.Employee.DepartmentId })
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            throw new ForbiddenException();
        }

        var granted = await permissions.GetEffectivePermissionsAsync(accountId, cancellationToken);
        var level = granted
            .Where(p => (p.Resource == "*" && p.Action == "*")
                || (string.Equals(p.Resource, PermissionResource, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Action, PermissionAction, StringComparison.OrdinalIgnoreCase)))
            .Select(p => PermissionScopes.GetScopeLevel(p.Scope))
            .DefaultIfEmpty(0)
            .Max();

        if (level < OwnScopeLevel)
        {
            throw new ForbiddenException();
        }

        return new CallerInfo(account.EmployeeId, account.DepartmentId, level);
    }

    /// <summary>The departments the caller may read as a set; null when every department is visible.</summary>
    private static HashSet<Guid>? ResolveVisible(CallerInfo caller, OrgSnapshot org)
    {
        if (caller.Level >= AllScopeLevel)
        {
            return null;
        }

        if (caller.Level < DepartmentScopeLevel || !org.Nodes.ContainsKey(caller.DepartmentId))
        {
            throw new ForbiddenException();
        }

        var rootId = caller.Level >= BranchScopeLevel ? org.BranchRoot(caller.DepartmentId) : caller.DepartmentId;
        return org.Subtree(rootId).ToHashSet();
    }

    private static void EnsureVisible(HashSet<Guid>? visible, Guid departmentId, string message)
    {
        if (visible is not null && !visible.Contains(departmentId))
        {
            throw new ForbiddenException(message);
        }
    }

    // ----- Periods --------------------------------------------------------------------------

    private async Task<(EvaluationPeriod? Period, List<EvaluationPeriod> Selectable)> ResolvePeriodAsync(
        Guid? periodId,
        CancellationToken cancellationToken)
    {
        var selectable = (await db.EvaluationPeriods.AsNoTracking()
                .Where(p => p.PeriodType != PeriodType.Day)
                .ToListAsync(cancellationToken))
            .OrderByDescending(p => p.StartDate)
            .ThenByDescending(p => (int)p.PeriodType)
            .ToList();

        if (periodId.HasValue)
        {
            var existing = selectable.FirstOrDefault(p => p.Id == periodId.Value);
            if (existing is not null)
            {
                return (existing, selectable);
            }

            var isDay = await db.EvaluationPeriods.AsNoTracking()
                .AnyAsync(p => p.Id == periodId.Value && p.PeriodType == PeriodType.Day, cancellationToken);
            if (isDay)
            {
                throw new ValidationException("Kỳ đánh giá theo ngày không được hỗ trợ cho bảng hiệu suất.");
            }

            throw new NotFoundException("Kỳ đánh giá", periodId.Value);
        }

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var current = selectable
            .Where(p => p.StartDate <= today && p.EndDate >= today)
            .OrderByDescending(p => (int)p.PeriodType)
            .FirstOrDefault()
            ?? selectable
                .Where(p => p.EndDate < today)
                .OrderByDescending(p => p.EndDate)
                .ThenByDescending(p => (int)p.PeriodType)
                .FirstOrDefault();

        return (current, selectable);
    }

    /// <summary>The latest earlier period of the same type.</summary>
    private static EvaluationPeriod? PreviousPeriod(EvaluationPeriod period, List<EvaluationPeriod> selectable) =>
        selectable
            .Where(p => p.PeriodType == period.PeriodType && p.EndDate < period.StartDate)
            .OrderByDescending(p => p.EndDate)
            .FirstOrDefault();

    private static DashboardPeriodDto ToDto(EvaluationPeriod p) =>
        new(p.Id, p.Code, p.Name, p.PeriodType.ToEnumString(), p.StartDate, p.EndDate);

    // ----- Rounding & statistics ------------------------------------------------------------

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal? RoundOrNull(decimal? value) => value.HasValue ? Round(value.Value) : null;

    private static decimal? Mean(IEnumerable<decimal> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : Round(list.Average());
    }

    private static decimal? Change(decimal? current, decimal? previous) =>
        current.HasValue && previous.HasValue ? Round(current.Value - previous.Value) : null;

    // ----- Private shapes -------------------------------------------------------------------

    private sealed record CallerInfo(Guid EmployeeId, Guid DepartmentId, int Level);

    private sealed record DeptNode(Guid Id, string Code, string Name, Guid? ParentId, DepartmentLevelType Level, RecordStatus Status);

    private sealed class OrgSnapshot
    {
        private static readonly List<Guid> NoChildren = [];
        private readonly Dictionary<Guid, List<Guid>> _children;

        public OrgSnapshot(IReadOnlyCollection<DeptNode> nodes)
        {
            Nodes = nodes.ToDictionary(n => n.Id);
            _children = nodes
                .Where(n => n.ParentId.HasValue && Nodes.ContainsKey(n.ParentId.Value))
                .GroupBy(n => n.ParentId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());
        }

        public Dictionary<Guid, DeptNode> Nodes { get; }

        public List<Guid> ChildrenOf(Guid id) => _children.GetValueOrDefault(id) ?? NoChildren;

        /// <summary>The department and all of its descendants (cycle-safe).</summary>
        public List<Guid> Subtree(Guid id)
        {
            var result = new List<Guid>();
            var seen = new HashSet<Guid>();
            var queue = new Queue<Guid>();
            queue.Enqueue(id);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!seen.Add(current))
                {
                    continue;
                }

                result.Add(current);
                foreach (var child in ChildrenOf(current))
                {
                    queue.Enqueue(child);
                }
            }

            return result;
        }

        /// <summary>Nearest BRANCH ancestor-or-self; the root-most ancestor when none exists.</summary>
        public Guid BranchRoot(Guid id)
        {
            var current = id;
            var seen = new HashSet<Guid>();
            while (seen.Add(current))
            {
                var node = Nodes[current];
                if (node.Level == DepartmentLevelType.Branch || !node.ParentId.HasValue
                    || !Nodes.ContainsKey(node.ParentId.Value))
                {
                    return current;
                }

                current = node.ParentId.Value;
            }

            return current;
        }
    }

    private async Task<OrgSnapshot> LoadOrgAsync(CancellationToken cancellationToken)
    {
        var nodes = await db.Departments.AsNoTracking()
            .Select(d => new DeptNode(d.Id, d.Code, d.Name, d.ParentDepartmentId, d.LevelType, d.Status))
            .ToListAsync(cancellationToken);

        return new OrgSnapshot(nodes);
    }
}
