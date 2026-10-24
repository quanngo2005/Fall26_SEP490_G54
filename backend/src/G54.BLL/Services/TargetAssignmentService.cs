using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.TargetAssignment;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace G54.BLL.Services;

/// <summary>
/// Branch / department / employee target allocation. Contract: docs/api/TARGET_ASSIGNMENT_CONTRACT.md.
/// Authorisation beyond the controller's <c>target:manage</c> policy (visible departments, root targets) is enforced here.
/// </summary>
public sealed class TargetAssignmentService(
    AppDbContext db,
    IPermissionService permissions,
    TimeProvider time) : ITargetAssignmentService
{
    private const int DepartmentScopeLevel = 2;
    private const int BranchScopeLevel = 3;
    private const int AllScopeLevel = 4;
    private const int MaxDecimalPlaces = 4;
    private const decimal MaxTargetValue = 99_999_999_999_999m;
    private const int MaxChangeReasonLength = 1000;
    private const int MaxAllocations = 200;
    private const string UniqueViolation = "23505";

    public async Task<TargetAssignmentDto> CreateRootAsync(
        Guid accountId,
        CreateTargetAssignmentDto dto,
        CancellationToken cancellationToken = default)
    {
        if (dto.KpiDefinitionId == Guid.Empty)
        {
            throw new ValidationException("KPI là bắt buộc.");
        }

        if (dto.EvaluationPeriodId == Guid.Empty)
        {
            throw new ValidationException("Kỳ đánh giá là bắt buộc.");
        }

        if (dto.DepartmentId is not { } branchId || branchId == Guid.Empty)
        {
            throw new ValidationException("Chi nhánh nhận target là bắt buộc.");
        }

        var value = ValidateValue(dto.TargetValue);
        var reason = NormalizeReason(dto.ChangeReason);

        var org = await LoadOrgAsync(cancellationToken);
        var caller = await ResolveCallerAsync(accountId, org, cancellationToken);
        if (!caller.IsAll)
        {
            throw new ForbiddenException("Chỉ quản trị viên mới được giao target gốc cho chi nhánh.");
        }

        await LoadKpiAsync(dto.KpiDefinitionId, cancellationToken);
        await EnsurePeriodOpenAsync(dto.EvaluationPeriodId, cancellationToken);

        if (!org.Nodes.TryGetValue(branchId, out var branch))
        {
            throw new NotFoundException("Chi nhánh", branchId);
        }

        if (branch.Level != DepartmentLevelType.Branch)
        {
            throw new ValidationException("Target gốc chỉ được giao cho một chi nhánh.");
        }

        EnsureDepartmentActive(branch);
        await EnsureNoDuplicateAsync(dto.KpiDefinitionId, dto.EvaluationPeriodId, branchId, null, cancellationToken);

        var assignment = NewAssignment(dto.KpiDefinitionId, dto.EvaluationPeriodId, null, branchId, null, value, reason);
        db.TargetAssignments.Add(assignment);
        await SaveAsync(cancellationToken);

        return await BuildDtoAsync(assignment.Id, cancellationToken);
    }

    public async Task<TargetAllocationResultDto> AllocateAsync(
        Guid accountId,
        Guid parentAssignmentId,
        AllocateTargetDto dto,
        CancellationToken cancellationToken = default)
    {
        var items = ValidateItems(dto);
        var reason = NormalizeReason(dto.ChangeReason);

        var org = await LoadOrgAsync(cancellationToken);
        var caller = await ResolveCallerAsync(accountId, org, cancellationToken);

        // The row lock on the parent serialises concurrent splits of the same target; the in-memory test provider has neither.
        var relational = db.Database.IsRelational();
        await using var transaction = relational ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (relational)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM target_assignment WHERE id = {parentAssignmentId} FOR UPDATE",
                cancellationToken);
        }

        var parent = await db.TargetAssignments.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == parentAssignmentId, cancellationToken)
            ?? throw new NotFoundException("Target cấp trên", parentAssignmentId);

        if (!parent.DepartmentId.HasValue)
        {
            throw new ValidationException("Target đã giao cho nhân viên không thể chia nhỏ tiếp.");
        }

        var parentDepartmentId = parent.DepartmentId.Value;
        if (!caller.CanSee(parentDepartmentId))
        {
            throw new ForbiddenException("Bạn không có quyền phân bổ target của đơn vị này.");
        }

        if (parent.Status != TargetAssignmentStatus.Active)
        {
            throw new ConflictException("Chỉ target đang hiệu lực mới được chia cho cấp dưới.");
        }

        var kpi = await LoadKpiAsync(parent.KpiDefinitionId, cancellationToken);
        await EnsurePeriodOpenAsync(parent.EvaluationPeriodId, cancellationToken);

        if (await db.TargetAssignments.AnyAsync(
                t => t.ParentAssignmentId == parent.Id && t.Status != TargetAssignmentStatus.Superseded,
                cancellationToken))
        {
            throw new ConflictException("Target này đã được phân bổ cho cấp dưới.");
        }

        var parentDepartment = org.Nodes[parentDepartmentId];
        var employeeDepartments = await LoadEmployeeDepartmentsAsync(items, cancellationToken);
        var seen = new HashSet<Guid>();
        foreach (var item in items)
        {
            var ownerId = (item.DepartmentId ?? item.EmployeeId)!.Value;
            if (!seen.Add(ownerId))
            {
                throw new ValidationException("Mỗi đối tượng chỉ được xuất hiện một lần trong danh sách phân bổ.");
            }

            EnsureIsDirectChild(parentDepartment, item, org, employeeDepartments);
        }

        if (kpi.AggregationType == KpiAggregationType.Sum)
        {
            var total = items.Sum(i => i.Value);
            if (total != parent.TargetValue)
            {
                throw new BusinessRuleException(
                    ErrorCodes.TargetAllocationSumMismatch,
                    $"Tổng target phân bổ ({total:0.####}) phải bằng target cấp trên ({parent.TargetValue:0.####}).",
                    StatusCodes.Status422UnprocessableEntity);
            }
        }

        var children = new List<TargetAssignment>();
        foreach (var item in items)
        {
            await EnsureNoDuplicateAsync(parent.KpiDefinitionId, parent.EvaluationPeriodId, item.DepartmentId, item.EmployeeId, cancellationToken);
            var child = NewAssignment(
                parent.KpiDefinitionId, parent.EvaluationPeriodId, parent, item.DepartmentId, item.EmployeeId, item.Value, reason);
            children.Add(child);
            db.TargetAssignments.Add(child);
        }

        await SaveAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var parentDto = await BuildDtoAsync(parent.Id, cancellationToken);
        var childDtos = new List<TargetAssignmentDto>(children.Count);
        foreach (var child in children)
        {
            childDtos.Add(await BuildDtoAsync(child.Id, cancellationToken));
        }

        return new TargetAllocationResultDto(parentDto, childDtos);
    }
    public async Task<TargetAssignmentDto> GetAsync(
        Guid accountId,
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        var org = await LoadOrgAsync(cancellationToken);
        var caller = await ResolveCallerAsync(accountId, org, cancellationToken);

        var owner = await db.TargetAssignments.AsNoTracking()
            .Where(t => t.Id == assignmentId)
            .Select(t => new { t.DepartmentId, EmployeeDepartmentId = t.Employee == null ? (Guid?)null : t.Employee.DepartmentId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Target", assignmentId);

        var departmentId = owner.DepartmentId ?? owner.EmployeeDepartmentId;
        if (!departmentId.HasValue || !caller.CanSee(departmentId.Value))
        {
            throw new ForbiddenException("Bạn không có quyền xem target này.");
        }

        return await BuildDtoAsync(assignmentId, cancellationToken);
    }

    // ----- Validation ------------------------------------------------------------------------

    private static decimal ValidateValue(decimal? raw)
    {
        if (raw is not { } value)
        {
            throw new ValidationException("Giá trị target là bắt buộc.");
        }

        if (value < 0)
        {
            throw new ValidationException("Giá trị target không được âm.");
        }

        if (value > MaxTargetValue || decimal.Round(value, MaxDecimalPlaces) != value)
        {
            throw new ValidationException("Giá trị target tối đa 14 chữ số nguyên và 4 chữ số thập phân.");
        }

        return value;
    }

    private static string? NormalizeReason(string? raw)
    {
        var reason = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        if (reason is { Length: > MaxChangeReasonLength })
        {
            throw new ValidationException("Ghi chú không vượt quá 1000 ký tự.");
        }

        return reason;
    }

    private static List<AllocationItem> ValidateItems(AllocateTargetDto dto)
    {
        if (dto.Allocations is not { Count: > 0 })
        {
            throw new ValidationException("Danh sách phân bổ phải có ít nhất một đối tượng.");
        }

        if (dto.Allocations.Count > MaxAllocations)
        {
            throw new ValidationException($"Mỗi lần chỉ được phân bổ tối đa {MaxAllocations} đối tượng.");
        }

        var items = new List<AllocationItem>(dto.Allocations.Count);
        foreach (var raw in dto.Allocations)
        {
            if (raw is null || raw.DepartmentId.HasValue == raw.EmployeeId.HasValue)
            {
                throw new ValidationException("Mỗi dòng phân bổ phải chọn đúng một đối tượng: đơn vị (departmentId) hoặc nhân viên (employeeId).");
            }

            items.Add(new AllocationItem(raw.DepartmentId, raw.EmployeeId, ValidateValue(raw.TargetValue)));
        }

        return items;
    }

    private async Task<KpiInfo> LoadKpiAsync(Guid kpiId, CancellationToken cancellationToken)
    {
        var kpi = await db.KpiDefinitions.AsNoTracking()
            .Where(k => k.Id == kpiId)
            .Select(k => new KpiInfo(k.Status, k.AggregationType))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("KPI", kpiId);
        if (kpi.Status != KpiStatus.Active)
        {
            throw new ValidationException("Chỉ KPI đang hoạt động mới được phân bổ target.");
        }

        return kpi;
    }

    private async Task EnsurePeriodOpenAsync(Guid periodId, CancellationToken cancellationToken)
    {
        var status = await db.EvaluationPeriods.AsNoTracking()
            .Where(p => p.Id == periodId)
            .Select(p => (PeriodStatus?)p.Status)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", periodId);
        if (status != PeriodStatus.Open)
        {
            throw new ConflictException("Kỳ đánh giá không còn mở nên không thể phân bổ target.");
        }
    }

    private static void EnsureDepartmentActive(DeptNode department)
    {
        if (department.Status != RecordStatus.Active)
        {
            throw new ValidationException($"Đơn vị '{department.Name}' không ở trạng thái hoạt động.");
        }
    }

    private async Task<Dictionary<Guid, EmployeeInfo>> LoadEmployeeDepartmentsAsync(
        List<AllocationItem> items,
        CancellationToken cancellationToken)
    {
        var ids = items.Where(i => i.EmployeeId.HasValue).Select(i => i.EmployeeId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var found = await db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.DepartmentId, e.Status })
            .ToDictionaryAsync(e => e.Id, e => new EmployeeInfo(e.DepartmentId, e.Status), cancellationToken);

        var missing = ids.FirstOrDefault(id => !found.ContainsKey(id));
        if (missing != Guid.Empty)
        {
            throw new NotFoundException("Nhân viên", missing);
        }

        return found;
    }

    /// <summary>Each child must sit exactly one level below the parent: a child department/team, or an employee of a department/team.</summary>
    private static void EnsureIsDirectChild(
        DeptNode parent,
        AllocationItem item,
        OrgSnapshot org,
        Dictionary<Guid, EmployeeInfo> employees)
    {
        if (item.DepartmentId is { } childId)
        {
            if (!org.Nodes.TryGetValue(childId, out var child))
            {
                throw new NotFoundException("Phòng ban", childId);
            }

            if (child.ParentId != parent.Id)
            {
                throw new ValidationException(
                    $"Chỉ được giao cho đơn vị trực thuộc '{parent.Name}' (cấp ngay dưới), không được nhảy cấp.");
            }

            EnsureDepartmentActive(child);
            return;
        }

        if (parent.Level == DepartmentLevelType.Branch)
        {
            throw new ValidationException("Chi nhánh phải chia target cho phòng ban trước khi giao cho nhân viên.");
        }

        var employee = employees[item.EmployeeId!.Value];
        if (employee.DepartmentId != parent.Id)
        {
            throw new ValidationException($"Nhân viên phải thuộc trực tiếp đơn vị '{parent.Name}'.");
        }

        if (employee.Status is not (EmployeeStatus.Active or EmployeeStatus.OnLeave))
        {
            throw new ValidationException("Chỉ nhân viên đang làm việc hoặc nghỉ phép mới được nhận target.");
        }
    }

    private async Task EnsureNoDuplicateAsync(
        Guid kpiId,
        Guid periodId,
        Guid? departmentId,
        Guid? employeeId,
        CancellationToken cancellationToken)
    {
        var query = db.TargetAssignments.AsNoTracking()
            .Where(t => t.KpiDefinitionId == kpiId && t.EvaluationPeriodId == periodId);
        var exists = departmentId.HasValue
            ? await query.AnyAsync(t => t.DepartmentId == departmentId, cancellationToken)
            : await query.AnyAsync(t => t.EmployeeId == employeeId, cancellationToken);

        if (exists)
        {
            throw new ConflictException("Đã tồn tại target cho KPI, kỳ đánh giá và đối tượng này.");
        }
    }

    private TargetAssignment NewAssignment(
        Guid kpiId,
        Guid periodId,
        TargetAssignment? parent,
        Guid? departmentId,
        Guid? employeeId,
        decimal value,
        string? reason)
    {
        var now = time.GetUtcNow();
        return new TargetAssignment
        {
            Id = Guid.NewGuid(),
            BusinessObjectiveId = parent?.BusinessObjectiveId,
            KpiDefinitionId = kpiId,
            DepartmentId = departmentId,
            EmployeeId = employeeId,
            EvaluationPeriodId = periodId,
            ParentAssignmentId = parent?.Id,
            SplitDimension = parent is null ? null : TargetSplitDimension.Org,
            Version = 1,
            TargetValue = value,
            Status = TargetAssignmentStatus.Active,
            AssignedAt = now,
            ApprovedAt = now,
            ChangeReason = reason,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            throw new ConflictException("Đã tồn tại target cho KPI, kỳ đánh giá và đối tượng này.");
        }
    }
    // ----- Response --------------------------------------------------------------------------

    private async Task<TargetAssignmentDto> BuildDtoAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.TargetAssignments.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new
            {
                Assignment = t,
                KpiCode = t.KpiDefinition.Code,
                KpiName = t.KpiDefinition.Name,
                KpiUnit = t.KpiDefinition.Unit,
                t.KpiDefinition.AggregationType,
                PeriodCode = t.EvaluationPeriod.Code,
                PeriodName = t.EvaluationPeriod.Name,
                DepartmentCode = t.Department == null ? null : t.Department.Code,
                DepartmentName = t.Department == null ? null : t.Department.Name,
                DepartmentLevel = t.Department == null ? (DepartmentLevelType?)null : t.Department.LevelType,
                EmployeeCode = t.Employee == null ? null : t.Employee.EmployeeCode,
                EmployeeName = t.Employee == null ? null : t.Employee.FullName,
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Target", id);

        var t = row.Assignment;
        decimal? allocated = null;
        decimal? remaining = null;
        if (t.DepartmentId.HasValue && row.AggregationType == KpiAggregationType.Sum)
        {
            allocated = await db.TargetAssignments.AsNoTracking()
                .Where(c => c.ParentAssignmentId == id && c.Status == TargetAssignmentStatus.Active)
                .Select(c => c.TargetValue)
                .SumAsync(cancellationToken);
            remaining = t.TargetValue - allocated;
        }

        return new TargetAssignmentDto(
            t.Id,
            t.KpiDefinitionId,
            row.KpiCode,
            row.KpiName,
            row.KpiUnit,
            row.AggregationType.ToEnumString(),
            t.EvaluationPeriodId,
            row.PeriodCode,
            row.PeriodName,
            t.DepartmentId.HasValue ? "DEPARTMENT" : "EMPLOYEE",
            t.DepartmentId,
            row.DepartmentCode,
            row.DepartmentName,
            row.DepartmentLevel?.ToEnumString(),
            t.EmployeeId,
            row.EmployeeCode,
            row.EmployeeName,
            t.ParentAssignmentId,
            t.SplitDimension?.ToEnumString(),
            t.Version,
            t.TargetValue,
            allocated,
            remaining,
            t.Status.ToEnumString(),
            t.AssignedAt,
            t.ApprovedAt,
            t.ChangeReason,
            t.CreatedAt,
            t.UpdatedAt);
    }

    // ----- Organization & access -------------------------------------------------------------

    private async Task<OrgSnapshot> LoadOrgAsync(CancellationToken cancellationToken)
    {
        var nodes = await db.Departments.AsNoTracking()
            .Select(d => new DeptNode(d.Id, d.Name, d.ParentDepartmentId, d.LevelType, d.Status))
            .ToListAsync(cancellationToken);
        return new OrgSnapshot(nodes);
    }

    private async Task<CallerScope> ResolveCallerAsync(
        Guid accountId,
        OrgSnapshot org,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId && !a.IsLocked && a.Employee.Status == EmployeeStatus.Active)
            .Select(a => new { a.Employee.DepartmentId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ForbiddenException();

        var granted = await permissions.GetEffectivePermissionsAsync(accountId, cancellationToken);
        var level = granted
            .Where(p => (p.Resource == "*" && p.Action == "*")
                || (string.Equals(p.Resource, "target", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Action, "manage", StringComparison.OrdinalIgnoreCase)))
            .Select(p => PermissionScopes.GetScopeLevel(p.Scope))
            .DefaultIfEmpty(0)
            .Max();

        if (level >= AllScopeLevel)
        {
            return new CallerScope(true, null);
        }

        if (level < DepartmentScopeLevel || !org.Nodes.ContainsKey(account.DepartmentId))
        {
            throw new ForbiddenException();
        }

        var rootId = level >= BranchScopeLevel ? org.BranchRoot(account.DepartmentId) : account.DepartmentId;
        return new CallerScope(false, org.Subtree(rootId).ToHashSet());
    }

    // ----- Private shapes --------------------------------------------------------------------

    private sealed record AllocationItem(Guid? DepartmentId, Guid? EmployeeId, decimal Value);

    private sealed record KpiInfo(KpiStatus Status, KpiAggregationType AggregationType);

    private sealed record EmployeeInfo(Guid DepartmentId, EmployeeStatus Status);

    private sealed record DeptNode(Guid Id, string Name, Guid? ParentId, DepartmentLevelType Level, RecordStatus Status);

    private sealed record CallerScope(bool IsAll, HashSet<Guid>? Visible)
    {
        public bool CanSee(Guid departmentId) => IsAll || (Visible?.Contains(departmentId) ?? false);
    }

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
                foreach (var child in _children.GetValueOrDefault(current) ?? NoChildren)
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
}
