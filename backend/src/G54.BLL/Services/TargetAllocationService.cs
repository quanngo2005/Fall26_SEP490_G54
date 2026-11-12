using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Validation;
using G54.BLL.Dtos.TargetAllocation;
using G54.DAL.Entities;
using G54.DAL.Uow;

namespace G54.BLL.Services;

public sealed class TargetAllocationService(IUnitOfWork uow, IPermissionService permissions) : ITargetAllocationService
{
    private Task<HashSet<Guid>?> ResolveVisibleAsync(Guid userId, CancellationToken ct) =>
        DepartmentAccessGuard.ResolveVisibleAsync(uow, permissions, userId, ct);

    private Task EnsureDepartmentAccessAsync(Guid userId, Guid? departmentId, CancellationToken ct) =>
        DepartmentAccessGuard.EnsureDepartmentAccessAsync(uow, permissions, userId, departmentId, ct);

    private static bool IsVisibleRow(TargetAssignment row, HashSet<Guid>? visible) =>
        visible is null
        || row.ParentAssignmentId == null
        || (row.DepartmentId.HasValue && visible.Contains(row.DepartmentId.Value));

    public async Task<List<TargetNodeDto>> AllocateDepartmentsAsync(Guid branchYearId, AllocateDepartmentsDto dto, Guid userId, CancellationToken ct = default)
    {
        var branchYearTarget = await uow.TargetAssignments.GetWithContextAsync(branchYearId, ct);
        if (branchYearTarget == null || branchYearTarget.ParentAssignmentId != null)
            throw new NotFoundException("Branch-year target not found.");

        await EnsureDepartmentAccessAsync(userId, branchYearTarget.DepartmentId, ct);

        if (branchYearTarget.EvaluationPeriod?.Status != PeriodStatus.Open)
            throw new ConflictException("Evaluation period is not open.", ErrorCodes.TargetPeriodNotOpen);

        if (dto.Items is not { Count: > 0 })
            throw new ValidationException("Danh sách phân bổ phải có ít nhất một phòng ban.");

        var deptIds = dto.Items.Select(i => i.DepartmentId).Distinct().ToList();
        var depts = await uow.Departments.ListAsync(d => deptIds.Contains(d.Id), ct);

        foreach (var deptId in deptIds)
        {
            var dept = depts.FirstOrDefault(d => d.Id == deptId);
            if (dept == null || dept.ParentDepartmentId != branchYearTarget.DepartmentId)
                throw new BusinessRuleException(ErrorCodes.TargetDepartmentNotInBranch, null, 422);
        }

        var byKpi = dto.Items.GroupBy(i => i.KpiDefinitionId).ToList();
        if (byKpi.Count > 1 || byKpi[0].Key != branchYearTarget.KpiDefinitionId)
            throw new ValidationException("Request may only contain the branch year target's own KPI.");

        foreach (var item in dto.Items)
        {
            var clash = await uow.TargetAssignments.AnyAsync(
                t => t.KpiDefinitionId == item.KpiDefinitionId && t.DepartmentId == item.DepartmentId
                     && t.EvaluationPeriodId == branchYearTarget.EvaluationPeriodId && t.ParentAssignmentId != branchYearTarget.Id,
                ct);
            if (clash)
                throw new ConflictException("A year target for this department and KPI already exists outside this allocation.", ErrorCodes.TargetInvalidState);
        }

        var totalTarget = dto.Items.Sum(i => i.TargetValue);
        if (totalTarget < branchYearTarget.TargetValue)
            throw new BusinessRuleException(ErrorCodes.TargetDepartmentSumBelow, null, 422);

        await uow.ExecuteInTransactionAsync(async transaction =>
        {
            foreach (var item in dto.Items)
            {
                var existing = (await uow.TargetAssignments.GetCurrentChildrenAsync(branchYearTarget.Id, transaction))
                    .FirstOrDefault(c => c.DepartmentId == item.DepartmentId && c.KpiDefinitionId == item.KpiDefinitionId);

                if (existing != null)
                {
                    if (existing.Status == TargetAssignmentStatus.Draft)
                    {
                        existing.TargetValue = item.TargetValue;
                    }
                    else if (existing.Status == TargetAssignmentStatus.Active)
                    {
                        if (string.IsNullOrWhiteSpace(dto.ChangeReason))
                            throw new ValidationException("changeReason is required when changing an active target.");

                        var descendants = await uow.TargetAssignments.GetActiveDescendantsAsync(existing.Id, transaction);
                        foreach (var desc in descendants)
                            desc.Status = TargetAssignmentStatus.Superseded;
                        existing.Status = TargetAssignmentStatus.Superseded;

                        var newRow = new TargetAssignment
                        {
                            Id = Guid.NewGuid(),
                            DepartmentId = item.DepartmentId,
                            KpiDefinitionId = item.KpiDefinitionId,
                            EvaluationPeriodId = branchYearTarget.EvaluationPeriodId,
                            ParentAssignmentId = branchYearTarget.Id,
                            SplitDimension = TargetSplitDimension.Org,
                            TargetValue = item.TargetValue,
                            Version = existing.Version + 1,
                            Status = TargetAssignmentStatus.Draft,
                            AssignedAt = DateTimeOffset.UtcNow,
                            ChangeReason = dto.ChangeReason,
                            EffectiveFrom = branchYearTarget.EvaluationPeriod!.StartDate,
                        };
                        await uow.TargetAssignments.AddAsync(newRow, transaction);
                    }
                }
                else
                {
                    var newRow = new TargetAssignment
                    {
                        Id = Guid.NewGuid(),
                        DepartmentId = item.DepartmentId,
                        KpiDefinitionId = item.KpiDefinitionId,
                        EvaluationPeriodId = branchYearTarget.EvaluationPeriodId,
                        ParentAssignmentId = branchYearTarget.Id,
                        SplitDimension = TargetSplitDimension.Org,
                        TargetValue = item.TargetValue,
                        Version = 1,
                        Status = TargetAssignmentStatus.Draft,
                        AssignedAt = DateTimeOffset.UtcNow,
                        EffectiveFrom = branchYearTarget.EvaluationPeriod!.StartDate,
                    };
                    await uow.TargetAssignments.AddAsync(newRow, transaction);
                }
            }
        }, ct);

        var tree = await GetTreeAsync(branchYearTarget.DepartmentId!.Value, int.Parse(branchYearTarget.EvaluationPeriod!.Code, System.Globalization.CultureInfo.InvariantCulture), null, userId, ct);
        return tree;
    }

    public async Task<List<TargetNodeDto>> AllocateQuartersAsync(Guid deptYearId, AllocateQuartersDto dto, Guid userId, CancellationToken ct = default)
    {
        var deptYearTarget = await uow.TargetAssignments.GetWithContextAsync(deptYearId, ct);
        if (deptYearTarget == null || deptYearTarget.ParentAssignmentId == null)
            throw new NotFoundException("Department-year target not found.");

        await EnsureDepartmentAccessAsync(userId, deptYearTarget.DepartmentId, ct);

        var branchYearTarget = await uow.TargetAssignments.GetWithContextAsync(deptYearTarget.ParentAssignmentId.Value, ct);
        if (branchYearTarget?.ParentAssignmentId != null)
            throw new ValidationException("Branch year target reference invalid.");

        if (deptYearTarget.EvaluationPeriod?.Status != PeriodStatus.Open)
            throw new ConflictException("Evaluation period is not open.", ErrorCodes.TargetPeriodNotOpen);

        var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(
            PeriodType.Quarter,
            branchYearTarget!.EvaluationPeriod!.StartDate,
            branchYearTarget.EvaluationPeriod.EndDate,
            ct);

        foreach (var item in dto.Items)
        {
            var clash = await uow.TargetAssignments.AnyAsync(
                t => t.KpiDefinitionId == deptYearTarget.KpiDefinitionId && t.DepartmentId == deptYearTarget.DepartmentId
                     && t.EvaluationPeriodId == item.QuarterPeriodId && t.ParentAssignmentId != deptYearTarget.Id,
                ct);
            if (clash)
                throw new ConflictException("A target for this department, KPI and quarter already exists outside this allocation.", ErrorCodes.TargetInvalidState);
        }

        var totalTarget = dto.Items.Sum(i => i.TargetValue);
        if (totalTarget < deptYearTarget.TargetValue)
            throw new BusinessRuleException(ErrorCodes.TargetQuarterSumBelow, null, 422);

        await uow.ExecuteInTransactionAsync(async transaction =>
        {
            foreach (var item in dto.Items)
            {
                var quarterPeriod = quarterPeriods.FirstOrDefault(q => q.Id == item.QuarterPeriodId);
                if (quarterPeriod == null)
                    throw new ValidationException("Quarter period not in valid range.");

                var existing = (await uow.TargetAssignments.GetCurrentChildrenAsync(deptYearTarget.Id, transaction))
                    .FirstOrDefault(c => c.EvaluationPeriodId == item.QuarterPeriodId);

                if (existing != null)
                {
                    if (existing.Status == TargetAssignmentStatus.Draft)
                    {
                        existing.TargetValue = item.TargetValue;
                    }
                    else if (existing.Status == TargetAssignmentStatus.Active)
                    {
                        if (string.IsNullOrWhiteSpace(dto.ChangeReason))
                            throw new ValidationException("changeReason is required when changing an active target.");

                        var descendants = await uow.TargetAssignments.GetActiveDescendantsAsync(existing.Id, transaction);
                        foreach (var desc in descendants)
                            desc.Status = TargetAssignmentStatus.Superseded;
                        existing.Status = TargetAssignmentStatus.Superseded;

                        var newRow = new TargetAssignment
                        {
                            Id = Guid.NewGuid(),
                            DepartmentId = deptYearTarget.DepartmentId,
                            KpiDefinitionId = deptYearTarget.KpiDefinitionId,
                            EvaluationPeriodId = item.QuarterPeriodId,
                            ParentAssignmentId = deptYearTarget.Id,
                            SplitDimension = TargetSplitDimension.Period,
                            TargetValue = item.TargetValue,
                            Version = existing.Version + 1,
                            Status = TargetAssignmentStatus.Draft,
                            AssignedAt = DateTimeOffset.UtcNow,
                            ChangeReason = dto.ChangeReason,
                            EffectiveFrom = quarterPeriod.StartDate,
                        };
                        await uow.TargetAssignments.AddAsync(newRow, transaction);
                    }
                }
                else
                {
                    var newRow = new TargetAssignment
                    {
                        Id = Guid.NewGuid(),
                        DepartmentId = deptYearTarget.DepartmentId,
                        KpiDefinitionId = deptYearTarget.KpiDefinitionId,
                        EvaluationPeriodId = item.QuarterPeriodId,
                        ParentAssignmentId = deptYearTarget.Id,
                        SplitDimension = TargetSplitDimension.Period,
                        TargetValue = item.TargetValue,
                        Version = 1,
                        Status = TargetAssignmentStatus.Draft,
                        AssignedAt = DateTimeOffset.UtcNow,
                        EffectiveFrom = quarterPeriod.StartDate,
                    };
                    await uow.TargetAssignments.AddAsync(newRow, transaction);
                }
            }
        }, ct);

        var tree = await GetTreeAsync(branchYearTarget.DepartmentId!.Value, int.Parse(branchYearTarget.EvaluationPeriod!.Code, System.Globalization.CultureInfo.InvariantCulture), null, userId, ct);
        return tree;
    }

    public async Task<TargetNodeDto> ConfirmAsync(Guid deptYearId, Guid userId, CancellationToken ct = default)
    {
        var deptYearTarget = await uow.TargetAssignments.GetWithContextAsync(deptYearId, ct);
        if (deptYearTarget == null)
            throw new NotFoundException("Department-year target not found.");

        await EnsureDepartmentAccessAsync(userId, deptYearTarget.DepartmentId, ct);

        // Confirming rewrites the quarter and month targets, so a locked year (or any locked quarter) is read-only.
        PeriodLockGuard.EnsureWritable(deptYearTarget.EvaluationPeriod);

        var branchYearTarget = await uow.TargetAssignments.GetWithContextAsync(deptYearTarget.ParentAssignmentId!.Value, ct);

        var quarterTargets = await uow.TargetAssignments.GetCurrentChildrenAsync(deptYearTarget.Id, ct);
        if (quarterTargets.Count == 0)
            throw new ConflictException("Department has no quarter allocation to confirm.", ErrorCodes.TargetInvalidState);

        // The month and day rows are generated under each quarter, so a locked quarter, month or day blocks the confirm.
        foreach (var quarterTarget in quarterTargets)
        {
            PeriodLockGuard.EnsureWritable(quarterTarget.EvaluationPeriod);

            var quarter = quarterTarget.EvaluationPeriod!;
            foreach (var childType in new[] { PeriodType.Month, PeriodType.Day })
            {
                foreach (var childPeriod in await uow.EvaluationPeriods.GetInRangeAsync(childType, quarter.StartDate, quarter.EndDate, ct))
                    PeriodLockGuard.EnsureWritable(childPeriod);
            }
        }

        // Unique index (kpi, department, period, version): new rows must take the next free version.
        var versions = (await uow.TargetAssignments.ListAsync(
                t => t.KpiDefinitionId == deptYearTarget.KpiDefinitionId && t.DepartmentId == deptYearTarget.DepartmentId, ct))
            .GroupBy(t => t.EvaluationPeriodId)
            .ToDictionary(g => g.Key, g => g.Max(t => t.Version));

        // DB rule: ACTIVE children of an ACTIVE SUM-KPI parent must sum exactly to the parent target.
        var isSum = deptYearTarget.KpiDefinition?.AggregationType == KpiAggregationType.Sum;
        if (isSum && quarterTargets.Sum(c => c.TargetValue) != deptYearTarget.TargetValue)
            throw new BusinessRuleException(ErrorCodes.TargetInvalidState, "Quarter targets must sum exactly to the department year target before confirming.", 422);

        var deptYearTargets = await uow.TargetAssignments.GetCurrentChildrenAsync(branchYearTarget!.Id, ct);
        var branchReady = deptYearTargets.All(s => s.Id == deptYearTarget.Id || s.Status == TargetAssignmentStatus.Active);
        if (branchReady && isSum && deptYearTargets.Sum(c => c.TargetValue) != branchYearTarget.TargetValue)
            throw new BusinessRuleException(ErrorCodes.TargetInvalidState, "Department year targets must sum exactly to the branch year target before confirming.", 422);

        await uow.ExecuteInTransactionAsync(async transaction =>
        {
            if (branchReady)
            {
                branchYearTarget.Status = TargetAssignmentStatus.Active;
                branchYearTarget.ApprovedAt = DateTimeOffset.UtcNow;
            }

            deptYearTarget.Status = TargetAssignmentStatus.Active;
            deptYearTarget.ApprovedAt = DateTimeOffset.UtcNow;

            foreach (var quarterTarget in quarterTargets)
            {
                var staleChildTargets = await uow.TargetAssignments.GetActiveDescendantsAsync(quarterTarget.Id, transaction);
                foreach (var staleChild in staleChildTargets)
                    staleChild.Status = TargetAssignmentStatus.Superseded;

                quarterTarget.Status = TargetAssignmentStatus.Active;
                quarterTarget.ApprovedAt = DateTimeOffset.UtcNow;

                var months = await uow.EvaluationPeriods.GetInRangeAsync(
                    PeriodType.Month,
                    quarterTarget.EvaluationPeriod!.StartDate,
                    quarterTarget.EvaluationPeriod.EndDate,
                    transaction);

                var monthParts = months.Select(m => (m.StartDate, m.EndDate)).ToList();
                var monthValues = SplitByWorkingDays(quarterTarget.TargetValue, monthParts);

                for (int i = 0; i < months.Count; i++)
                {
                    var monthId = Guid.NewGuid();
                    var monthTarget = new TargetAssignment
                    {
                        Id = monthId,
                        DepartmentId = deptYearTarget.DepartmentId,
                        KpiDefinitionId = deptYearTarget.KpiDefinitionId,
                        EvaluationPeriodId = months[i].Id,
                        ParentAssignmentId = quarterTarget.Id,
                        SplitDimension = TargetSplitDimension.Period,
                        TargetValue = monthValues[i],
                        Version = NextVersion(versions, months[i].Id),
                        Status = TargetAssignmentStatus.Active,
                        AssignedAt = DateTimeOffset.UtcNow,
                        ApprovedAt = DateTimeOffset.UtcNow,
                        EffectiveFrom = months[i].StartDate,
                    };
                    await uow.TargetAssignments.AddAsync(monthTarget, transaction);

                    // DB rules: DAY sits directly under MONTH and ACTIVE children must sum exactly to their parent,
                    // so only days are stored under a month. Week totals are derived from the day rows.
                    await AddDaysAsync(deptYearTarget, monthTarget, months[i].StartDate, months[i].EndDate, versions, transaction);
                }
            }
        }, ct);

        var tree = await GetTreeAsync(branchYearTarget!.DepartmentId!.Value, int.Parse(branchYearTarget.EvaluationPeriod!.Code, System.Globalization.CultureInfo.InvariantCulture), null, userId, ct);
        return FindNode(tree, deptYearTarget.Id) ?? throw new NotFoundException("Department-year target not found.");
    }

    public async Task<List<TargetNodeDto>> GetTreeAsync(Guid branchId, int year, int? compareYear, Guid userId, CancellationToken ct = default)
    {
        // Callers whose subtree holds the branch see the whole tree; a department inside the branch
        // sees the branch root plus its own subtree; anyone else is refused.
        var visible = await ResolveVisibleAsync(userId, ct);
        if (visible is not null && visible.Contains(branchId))
            visible = null;
        if (visible is not null
            && !visible.Overlaps(await uow.TargetAssignments.GetDescendantDepartmentIdsAsync(branchId, ct)))
            throw new ForbiddenException(DepartmentAccessGuard.AccessDenied);

        var yearPeriod = await uow.EvaluationPeriods.GetYearAsync(year, ct);
        if (yearPeriod == null)
            return [];

        var rows = (await uow.TargetAssignments.GetTreeAsync(branchId, yearPeriod.Id, ct))
            .Where(r => IsVisibleRow(r, visible))
            .ToList();

        var compareTree = new Dictionary<(Guid, Guid?, int), (decimal Target, decimal Actual)>();
        if (compareYear.HasValue)
        {
            var compareYearPeriod = await uow.EvaluationPeriods.GetYearAsync(compareYear.Value, ct);
            if (compareYearPeriod != null)
            {
                var compareRows = await uow.TargetAssignments.GetTreeAsync(branchId, compareYearPeriod.Id, ct);
                foreach (var row in compareRows.Where(r => r.Status != TargetAssignmentStatus.Superseded && IsVisibleRow(r, visible)))
                {
                    var key = (row.KpiDefinitionId, row.DepartmentId, GetPeriodPosition(row.EvaluationPeriod?.Code ?? ""));
                    var actual = row.ActualProgresses?.Sum(ap => ap.Value) ?? 0;
                    compareTree[key] = (row.TargetValue, actual);
                }
            }
        }

        var rootIds = rows.Where(r => r.ParentAssignmentId == null && r.Status != TargetAssignmentStatus.Superseded)
            .Select(r => r.Id)
            .ToList();

        var nodes = new List<TargetNodeDto>();
        foreach (var rootId in rootIds)
        {
            var node = BuildNode(rootId, rows, compareTree);
            if (node != null)
                nodes.Add(node);
        }

        return nodes;
    }

    private static TargetNodeDto? BuildNode(Guid id, IReadOnlyList<TargetAssignment> allRows, Dictionary<(Guid, Guid?, int), (decimal Target, decimal Actual)> compareTree)
    {
        var row = allRows.FirstOrDefault(r => r.Id == id);
        if (row == null || row.Status == TargetAssignmentStatus.Superseded)
            return null;

        var children = allRows
            .Where(r => r.ParentAssignmentId == id && r.Status != TargetAssignmentStatus.Superseded)
            .Select(c => BuildNode(c.Id, allRows, compareTree))
            .Where(c => c != null)
            .Cast<TargetNodeDto>()
            .ToList();

        // A node can hold both its own period split and same-period allocations to sub-units; count only the
        // allocations when present so the two are not summed together.
        var periodType = row.EvaluationPeriod?.PeriodType.ToEnumString() ?? "";
        var allocations = children.Where(c => c.PeriodType == periodType).ToList();
        var allocatedValue = (allocations.Count > 0 ? allocations : children.Where(c => c.PeriodType != PeriodType.Week.ToEnumString()))
            .Sum(c => c.TargetValue);
        var actualValue = row.ActualProgresses?.Sum(ap => ap.Value) ?? 0m;

        var compareKey = (row.KpiDefinitionId, row.DepartmentId, GetPeriodPosition(row.EvaluationPeriod?.Code ?? ""));
        decimal? compareTarget = null, compareActual = null;
        if (compareTree.TryGetValue(compareKey, out var compareValues))
        {
            compareTarget = compareValues.Target;
            compareActual = compareValues.Actual;
        }

        return new TargetNodeDto(
            row.Id,
            row.ParentAssignmentId,
            row.KpiDefinitionId,
            row.KpiDefinition?.Name ?? "",
            row.DepartmentId ?? Guid.Empty,
            row.Department?.Name ?? "",
            row.EvaluationPeriodId,
            row.EvaluationPeriod?.Code ?? "",
            row.EvaluationPeriod?.PeriodType.ToEnumString() ?? "",
            row.TargetValue,
            allocatedValue,
            actualValue > 0 ? actualValue : null,
            row.Version,
            row.Status.ToEnumString(),
            compareTarget,
            compareActual,
            children);
    }

    private static TargetNodeDto? FindNode(IEnumerable<TargetNodeDto> nodes, Guid id)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id)
                return node;
            var found = FindNode(node.Children, id);
            if (found != null)
                return found;
        }

        return null;
    }

    private static int NextVersion(Dictionary<Guid, int> versions, Guid periodId) =>
        versions.TryGetValue(periodId, out var v) ? v + 1 : 1;

    private static TargetAssignment NewPeriodRow(TargetAssignment deptYearTarget, Guid periodId, Guid parentId, decimal value, int version, DateOnly effectiveFrom) => new()
    {
        Id = Guid.NewGuid(),
        DepartmentId = deptYearTarget.DepartmentId,
        KpiDefinitionId = deptYearTarget.KpiDefinitionId,
        BusinessObjectiveId = deptYearTarget.BusinessObjectiveId,
        EvaluationPeriodId = periodId,
        ParentAssignmentId = parentId,
        SplitDimension = TargetSplitDimension.Period,
        TargetValue = value,
        Version = version,
        Status = TargetAssignmentStatus.Active,
        AssignedAt = DateTimeOffset.UtcNow,
        ApprovedAt = DateTimeOffset.UtcNow,
        EffectiveFrom = effectiveFrom,
    };

    private async Task AddDaysAsync(TargetAssignment deptYearTarget, TargetAssignment parent, DateOnly start, DateOnly end, Dictionary<Guid, int> versions, CancellationToken ct)
    {
        var days = await uow.EvaluationPeriods.GetInRangeAsync(PeriodType.Day, start, end, ct);
        if (days.Count == 0)
            return;

        var values = SplitByWorkingDays(parent.TargetValue, days.Select(d => (d.StartDate, d.EndDate)).ToList());
        for (int k = 0; k < days.Count; k++)
            await uow.TargetAssignments.AddAsync(NewPeriodRow(deptYearTarget, days[k].Id, parent.Id, values[k], NextVersion(versions, days[k].Id), days[k].StartDate), ct);
    }

    private static int GetPeriodPosition(string code)
    {
        if (int.TryParse(code, System.Globalization.CultureInfo.InvariantCulture, out var year))
            return 0;
        if (code.Contains("-Q"))
            return int.Parse(code.Split("-Q")[1], System.Globalization.CultureInfo.InvariantCulture) * 100;
        if (code.Contains('-'))
            return int.Parse(code.Split("-")[1], System.Globalization.CultureInfo.InvariantCulture) * 10;
        return 0;
    }

    internal static List<decimal> SplitByWorkingDays(decimal total, IReadOnlyList<(DateOnly Start, DateOnly End)> parts)
    {
        if (parts.Count == 0)
            return [];

        var workingDaysByPart = parts.Select(p => CountWorkingDays(p.Start, p.End)).ToList();
        var totalWorkingDays = workingDaysByPart.Sum();

        if (totalWorkingDays == 0)
        {
            var result = new List<decimal>();
            var perPart = total / parts.Count;
            for (int i = 0; i < parts.Count - 1; i++)
                result.Add(Math.Round(perPart, 2));
            result.Add(total - result.Sum());
            return result;
        }

        // The rounding remainder goes to the last part that has working days, so no part can go negative.
        var lastIdx = workingDaysByPart.FindLastIndex(w => w > 0);
        var split = new List<decimal>();
        var assigned = 0m;
        for (int i = 0; i < parts.Count; i++)
        {
            if (i == lastIdx)
            {
                split.Add(total - assigned);
                assigned = total;
                continue;
            }

            var value = workingDaysByPart[i] == 0 ? 0m : Math.Round(total * workingDaysByPart[i] / totalWorkingDays, 2);
            split.Add(value);
            assigned += value;
        }

        return split;
    }

    private static int CountWorkingDays(DateOnly start, DateOnly end)
    {
        int count = 0;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                count++;
        }
        return count;
    }
}
