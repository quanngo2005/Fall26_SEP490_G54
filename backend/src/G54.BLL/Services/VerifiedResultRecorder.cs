using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Validation;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// Records the verified result and KPI contribution of an approved submission. Shared by the verification decision and
/// by an ACCEPT appeal decision. ADJUST requests revision and does not record a verified result.
/// A work with a KPI and a verified value is rejected (409 <c>KPI_TARGET_NOT_ASSIGNED</c>) when any recipient has no active target.
/// </summary>
internal static class VerifiedResultRecorder
{
    /// <param name="work">The submission's work, with <c>WorkType.WorkTypeKpis</c> loaded.</param>
    public static async Task RecordAsync(
        AppDbContext db,
        TimeProvider time,
        Verification verification,
        Work work,
        Guid submitterEmployeeId,
        decimal? verifiedValue,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // A work item (Task ID) may contribute KPI to the system only once.
        var alreadyContributed = await db.WorkResultItems.AnyAsync(
            i => i.VerifiedWorkResult.Verification.ResultSubmission.WorkId == work.Id,
            cancellationToken);
        if (alreadyContributed)
        {
            throw new BusinessRuleException(ErrorCodes.KpiAlreadyContributed, statusCode: StatusCodes.Status409Conflict);
        }

        var progressDate = time.GetBusinessToday();
        var mapping = SelectInEffectMapping(work.WorkType, progressDate);
        var primaryKpiId = mapping?.KpiDefinitionId;
        var multiplier = mapping?.Multiplier ?? 1m;
        (Guid EmployeeId, decimal RawValue)[] contributions = [];
        var targets = new Dictionary<Guid, TargetAssignment>();
        if (primaryKpiId.HasValue && verifiedValue.HasValue)
        {
            var handover = await db.TaskHandovers
                .AsNoTracking()
                .Where(h => h.WorkId == work.Id && h.Status == HandoverStatus.Accepted)
                .OrderByDescending(h => h.AcceptedAt ?? h.HandoverAt)
                .FirstOrDefaultAsync(cancellationToken);

            contributions = handover is null
                ? [(submitterEmployeeId, verifiedValue.Value)]
                : SplitHandoverContributions(handover, verifiedValue.Value);

            // Resolve every recipient's target before writing anything, so one missing target rejects the whole approval.
            foreach (var (employeeId, _) in contributions)
            {
                var target = await FindActiveTargetAsync(db, primaryKpiId.Value, employeeId, progressDate, cancellationToken)
                    ?? throw await TargetNotAssignedAsync(db, employeeId, cancellationToken);

                // Progress is never added to a locked period. Reversal entries (BR-16) are not implemented yet.
                PeriodLockGuard.EnsureWritable(target.EvaluationPeriod);
                targets[employeeId] = target;
            }
        }

        var verifiedResult = new VerifiedWorkResult
        {
            Id = Guid.NewGuid(),
            VerificationId = verification.Id,
            VerifiedAt = now,
            UpdatedAt = now,
            Status = VerifiedResultStatus.Valid,
        };
        db.VerifiedWorkResults.Add(verifiedResult);

        foreach (var (employeeId, rawValue) in contributions)
        {
            await AddResultItemAsync(db, time, verifiedResult.Id, targets[employeeId], employeeId, rawValue, multiplier, now, cancellationToken);
        }
    }

    /// <summary>
    /// The WorkTypeKpi mapping in effect on <paramref name="date"/>
    /// (<c>EffectiveFrom &lt;= date &amp;&amp; (EffectiveTo == null || EffectiveTo &gt;= date)</c>), latest
    /// <c>EffectiveFrom</c> first. <c>null</c> when the work type has no mapping in effect (e.g. expired or not
    /// started), in which case the approval records no KPI contribution.
    /// </summary>
    private static WorkTypeKpi? SelectInEffectMapping(WorkType? workType, DateOnly date) =>
        workType?.WorkTypeKpis
            .Where(k => k.EffectiveFrom <= date && (k.EffectiveTo == null || k.EffectiveTo >= date))
            .OrderByDescending(k => k.EffectiveFrom)
            .FirstOrDefault();

    private static (Guid EmployeeId, decimal RawValue)[] SplitHandoverContributions(TaskHandover handover, decimal totalPoints)
    {
        var (referrerPoints, processorPoints) = ReferralPointSplit.Split(totalPoints, handover.ReferrerSharePercent);
        return
        [
            (handover.FromEmployeeId, referrerPoints),
            (handover.ToEmployeeId, processorPoints),
        ];
    }

    // A verified result item must feed an employee target's progress, so approval needs an active target for each recipient.
    private static Task<TargetAssignment?> FindActiveTargetAsync(
        AppDbContext db,
        Guid kpiDefinitionId,
        Guid employeeId,
        DateOnly progressDate,
        CancellationToken cancellationToken) =>
        db.TargetAssignments
            .Include(t => t.EvaluationPeriod)
            .Where(t => t.KpiDefinitionId == kpiDefinitionId
                && t.EmployeeId == employeeId
                && t.Status == TargetAssignmentStatus.Active
                && t.EvaluationPeriod.StartDate <= progressDate
                && t.EvaluationPeriod.EndDate >= progressDate)
            .OrderBy(t => t.EvaluationPeriod.EndDate)
            .FirstOrDefaultAsync(cancellationToken);

    private static async Task<BusinessRuleException> TargetNotAssignedAsync(
        AppDbContext db,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees
            .AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new { e.EmployeeCode, e.FullName })
            .FirstOrDefaultAsync(cancellationToken);
        var who = employee is null ? "Nhân sự nhận điểm" : $"Nhân sự {employee.FullName} ({employee.EmployeeCode})";
        return new BusinessRuleException(
            ErrorCodes.KpiTargetNotAssigned,
            $"{who} chưa có chỉ tiêu (target) đang hiệu lực cho KPI của công việc này trong kỳ hiện tại nên chưa thể phê duyệt kết quả.",
            StatusCodes.Status409Conflict);
    }

    private static async Task AddResultItemAsync(
        AppDbContext db,
        TimeProvider time,
        Guid verifiedWorkResultId,
        TargetAssignment target,
        Guid employeeId,
        decimal rawValue,
        decimal multiplier,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var weightedValue = Math.Round(rawValue * multiplier, 4, MidpointRounding.ToEven);
        var progress = await AddToProgressAsync(db, target, time.GetBusinessToday(), weightedValue, now, cancellationToken);

        db.WorkResultItems.Add(new WorkResultItem
        {
            Id = Guid.NewGuid(),
            VerifiedWorkResultId = verifiedWorkResultId,
            ActualProgressId = progress.Id,
            EmployeeId = employeeId,
            RawValue = rawValue,
            MultiplierApplied = multiplier,
            Value = weightedValue,
            UpdatedAt = now,
        });
    }

    private static async Task<ActualProgress> AddToProgressAsync(
        AppDbContext db,
        TargetAssignment target,
        DateOnly progressDate,
        decimal value,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var progress = db.ActualProgresses.Local
                .FirstOrDefault(p => p.TargetAssignmentId == target.Id && p.ProgressDate == progressDate)
            ?? await db.ActualProgresses.FirstOrDefaultAsync(
                p => p.TargetAssignmentId == target.Id && p.ProgressDate == progressDate,
                cancellationToken);
        if (progress is null)
        {
            progress = new ActualProgress
            {
                Id = Guid.NewGuid(),
                TargetAssignmentId = target.Id,
                ProgressDate = progressDate,
                Value = 0,
                Status = ActualProgressStatus.Valid,
                UpdatedAt = now,
            };
            db.ActualProgresses.Add(progress);
        }

        progress.Value += value;
        progress.UpdatedAt = now;
        return progress;
    }
}
