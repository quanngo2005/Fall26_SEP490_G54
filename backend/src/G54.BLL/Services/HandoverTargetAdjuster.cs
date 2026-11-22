using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Validation;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// A manager hands a work from A to B: the work's expected result (<see cref="Work.TargetValue"/>) leaves A's monthly KPI
/// target and joins B's, in the month of the handover and in the following month. The amount moved is capped at A's
/// monthly target, so it never drops below zero.
/// A monthly target sits under the employee's own quarter (and that under the year ...): each of those ancestors owned by
/// the same employee moves by the same amount, because the database requires the children to sum to their parent. A and B
/// must share the same parent above that chain (their department's target), so the department's total does not change.
/// A month is left alone, and the handover still goes through, when the work has no expected result, its work type has
/// no in-effect <c>SUM</c> KPI, either employee has no target, the two chains differ in shape or parent, a target in the
/// chain has children that cannot follow, or the month is locked.
/// </summary>
internal static class HandoverTargetAdjuster
{
    /// <returns>The number of (KPI, month) pairs in which targets were moved.</returns>
    public static async Task<int> MoveAsync(
        AppDbContext db,
        Work work,
        Guid fromEmployeeId,
        Guid toEmployeeId,
        Guid changedByEmployeeId,
        DateOnly today,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (work.TargetValue is not > 0 || work.WorkTypeId is null)
        {
            return 0;
        }

        var workType = await db.WorkTypes
            .Include(w => w.WorkTypeKpis).ThenInclude(k => k.KpiDefinition)
            .FirstOrDefaultAsync(w => w.Id == work.WorkTypeId, cancellationToken);
        var reason = $"Bàn giao công việc {work.Code}";

        var moved = 0;
        foreach (var monthDate in new[] { today, new DateOnly(today.Year, today.Month, 1).AddMonths(1) })
        {
            var kpiIds = workType?.WorkTypeKpis.Select(k => k.KpiDefinitionId).Distinct() ?? [];
            foreach (var kpiId in kpiIds)
            {
                var mapping = VerifiedResultRecorder.SelectInEffectMapping(workType, monthDate, kpiId);
                if (mapping?.KpiDefinition.AggregationType == KpiAggregationType.Sum
                    && await MoveInMonthAsync(db, kpiId, work.TargetValue.Value, fromEmployeeId, toEmployeeId, monthDate, today, now, reason, changedByEmployeeId, cancellationToken))
                {
                    moved++;
                }
            }
        }

        return moved;
    }

    private static async Task<bool> MoveInMonthAsync(
        AppDbContext db,
        Guid kpiDefinitionId,
        decimal expected,
        Guid fromEmployeeId,
        Guid toEmployeeId,
        DateOnly monthDate,
        DateOnly today,
        DateTimeOffset now,
        string reason,
        Guid changedByEmployeeId,
        CancellationToken cancellationToken)
    {
        var targets = await db.TargetAssignments
            .Include(t => t.EvaluationPeriod)
            .Where(t => t.KpiDefinitionId == kpiDefinitionId
                && t.Status == TargetAssignmentStatus.Active
                && (t.EmployeeId == fromEmployeeId || t.EmployeeId == toEmployeeId)
                && t.EvaluationPeriod.PeriodType == PeriodType.Month
                && t.EvaluationPeriod.StartDate <= monthDate
                && t.EvaluationPeriod.EndDate >= monthDate)
            .ToListAsync(cancellationToken);

        var fromMonth = targets.SingleOrDefault(t => t.EmployeeId == fromEmployeeId);
        var toMonth = targets.SingleOrDefault(t => t.EmployeeId == toEmployeeId);
        if (fromMonth is null || toMonth is null)
        {
            return false;
        }

        var fromChain = await LoadChainAsync(db, fromMonth, cancellationToken);
        var toChain = await LoadChainAsync(db, toMonth, cancellationToken);
        if (fromChain is null || toChain is null || !SameShape(fromChain, toChain))
        {
            return false;
        }

        var amount = Math.Min(expected, fromChain.Min(t => t.TargetValue));
        if (amount <= 0
            || !await IsOpenAsync(db, fromMonth.EvaluationPeriod, monthDate, cancellationToken)
            || await HasChildrenThatCannotFollowAsync(db, fromChain, cancellationToken)
            || await HasChildrenThatCannotFollowAsync(db, toChain, cancellationToken))
        {
            return false;
        }

        ChangeMonth(db, fromMonth, fromMonth.TargetValue - amount, reason, changedByEmployeeId, today, now);
        ChangeMonth(db, toMonth, toMonth.TargetValue + amount, reason, changedByEmployeeId, today, now);
        foreach (var ancestor in fromChain.Skip(1))
        {
            ChangeInPlace(ancestor, ancestor.TargetValue - amount, reason, changedByEmployeeId, now);
        }

        foreach (var ancestor in toChain.Skip(1))
        {
            ChangeInPlace(ancestor, ancestor.TargetValue + amount, reason, changedByEmployeeId, now);
        }

        return true;
    }

    /// <summary>
    /// The monthly target followed by every ancestor owned by the same employee (their quarter, year ...), nearest first.
    /// Null when one of them is not ACTIVE.
    /// </summary>
    private static async Task<List<TargetAssignment>?> LoadChainAsync(AppDbContext db, TargetAssignment month, CancellationToken cancellationToken)
    {
        var chain = new List<TargetAssignment> { month };
        var current = month;
        while (current.ParentAssignmentId is { } parentId)
        {
            var parent = await db.TargetAssignments.FirstOrDefaultAsync(t => t.Id == parentId, cancellationToken);
            if (parent is null || parent.EmployeeId != month.EmployeeId)
            {
                break;
            }

            if (parent.Status != TargetAssignmentStatus.Active)
            {
                return null;
            }

            chain.Add(parent);
            current = parent;
        }

        return chain;
    }

    /// <summary>
    /// The two employees' chains must have the same periods and split dimensions level by level, and their tops must hang
    /// under the same parent (the department's target), so what one loses the other gains inside it.
    /// </summary>
    private static bool SameShape(List<TargetAssignment> from, List<TargetAssignment> to)
    {
        if (from.Count != to.Count || from[^1].ParentAssignmentId != to[^1].ParentAssignmentId)
        {
            return false;
        }

        return from.Zip(to).All(pair =>
            pair.First.EvaluationPeriodId == pair.Second.EvaluationPeriodId
            && pair.First.SplitDimension == pair.Second.SplitDimension);
    }

    /// <summary>
    /// A month with children of its own, or an ancestor with children split another way than the chain, would stop
    /// summing to its parent once only the chain changes.
    /// </summary>
    private static async Task<bool> HasChildrenThatCannotFollowAsync(AppDbContext db, List<TargetAssignment> chain, CancellationToken cancellationToken)
    {
        if (await db.TargetAssignments.AnyAsync(t => t.ParentAssignmentId == chain[0].Id && t.Status == TargetAssignmentStatus.Active, cancellationToken))
        {
            return true;
        }

        for (var i = 1; i < chain.Count; i++)
        {
            var parentId = chain[i].Id;
            var dimension = chain[i - 1].SplitDimension;
            if (await db.TargetAssignments.AnyAsync(
                t => t.ParentAssignmentId == parentId && t.Status == TargetAssignmentStatus.Active && t.SplitDimension != dimension,
                cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static void ChangeInPlace(TargetAssignment target, decimal value, string reason, Guid changedByEmployeeId, DateTimeOffset now)
    {
        target.TargetValue = value;
        target.ChangeReason = reason;
        target.ChangedByEmployeeId = changedByEmployeeId;
        target.UpdatedAt = now;
    }
    /// <summary>
    /// A target already in force is frozen as of yesterday and replaced by a new version from today (BR-15), so results
    /// recorded before the handover keep their old target. One that starts today or later is changed in place.
    /// </summary>
    private static void ChangeMonth(AppDbContext db, TargetAssignment current, decimal value, string reason, Guid changedByEmployeeId, DateOnly today, DateTimeOffset now)
    {
        if (current.EffectiveFrom >= today)
        {
            ChangeInPlace(current, value, reason, changedByEmployeeId, now);
            return;
        }

        current.Status = TargetAssignmentStatus.Superseded;
        current.EffectiveTo = today.AddDays(-1);
        current.UpdatedAt = now;
        db.TargetAssignments.Add(new TargetAssignment
        {
            KpiDefinitionId = current.KpiDefinitionId,
            EvaluationPeriodId = current.EvaluationPeriodId,
            EmployeeId = current.EmployeeId,
            BusinessObjectiveId = current.BusinessObjectiveId,
            ParentAssignmentId = current.ParentAssignmentId,
            SplitDimension = current.SplitDimension,
            Version = current.Version + 1,
            TargetValue = value,
            Status = TargetAssignmentStatus.Active,
            AssignedAt = now,
            ApprovedAt = now,
            ChangeReason = reason,
            CreatedAt = now,
            UpdatedAt = now,
            EffectiveFrom = today,
            PreviousVersionId = current.Id,
            ChangedByEmployeeId = changedByEmployeeId,
        });
    }

    private static async Task<bool> IsOpenAsync(AppDbContext db, EvaluationPeriod month, DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            PeriodLockGuard.EnsureWritable(month);
            PeriodLockGuard.EnsureMonthOpen(month, date);
            await PeriodLockGuard.EnsureDateWritableAsync(db, date, cancellationToken);
            return true;
        }
        catch (ConflictException)
        {
            return false;
        }
    }
}
