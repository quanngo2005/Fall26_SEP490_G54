using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Validation;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// A manager hands a work from A to B: the work's expected result (<see cref="Work.TargetValue"/>) leaves A's monthly KPI
/// target and joins B's, in the month of the handover and in the following month. The amount moved is capped at A's
/// target, so A's target never drops below zero and the sum of children under their parent stays exact.
/// A month is left alone, and the handover still goes through, when the work has no expected result, its work type has
/// no in-effect <c>SUM</c> KPI, either employee has no target, the two targets hang under different parents or split
/// dimensions or have children of their own, or the month is locked.
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

        var from = targets.SingleOrDefault(t => t.EmployeeId == fromEmployeeId);
        var to = targets.SingleOrDefault(t => t.EmployeeId == toEmployeeId);
        if (from is null || to is null
            || from.ParentAssignmentId != to.ParentAssignmentId
            || from.SplitDimension != to.SplitDimension)
        {
            return false;
        }

        var amount = Math.Min(expected, from.TargetValue);
        if (amount <= 0
            || !await IsOpenAsync(db, from.EvaluationPeriod, monthDate, cancellationToken)
            || await db.TargetAssignments.AnyAsync(
                t => (t.ParentAssignmentId == from.Id || t.ParentAssignmentId == to.Id) && t.Status == TargetAssignmentStatus.Active,
                cancellationToken))
        {
            return false;
        }

        Change(db, from, from.TargetValue - amount, reason, changedByEmployeeId, today, now);
        Change(db, to, to.TargetValue + amount, reason, changedByEmployeeId, today, now);
        return true;
    }

    /// <summary>
    /// A target already in force is frozen as of yesterday and replaced by a new version from today (BR-15), so results
    /// recorded before the handover keep their old target. One that starts today or later is changed in place.
    /// </summary>
    private static void Change(AppDbContext db, TargetAssignment current, decimal value, string reason, Guid changedByEmployeeId, DateOnly today, DateTimeOffset now)
    {
        if (current.EffectiveFrom >= today)
        {
            current.TargetValue = value;
            current.ChangeReason = reason;
            current.ChangedByEmployeeId = changedByEmployeeId;
            current.UpdatedAt = now;
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
