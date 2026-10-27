using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

/// <summary>
/// Records the verified result and KPI contribution of an approved submission. Shared by the verification decision and
/// by an ACCEPT appeal decision. ADJUST requests revision and does not record a verified result.
/// </summary>
internal static class VerifiedResultRecorder
{
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogContributionSkipped =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(2, "ContributionSkippedNoActiveTarget"),
            "KPI contribution skipped: EmployeeId={EmployeeId} has no active target for KpiDefinitionId={KpiDefinitionId}");

    /// <param name="work">The submission's work, with <c>WorkType.WorkTypeKpis</c> loaded.</param>
    public static async Task RecordAsync(
        AppDbContext db,
        ILogger logger,
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

        var verifiedResult = new VerifiedWorkResult
        {
            Id = Guid.NewGuid(),
            VerificationId = verification.Id,
            VerifiedAt = now,
            UpdatedAt = now,
            Status = VerifiedResultStatus.Valid,
        };
        db.VerifiedWorkResults.Add(verifiedResult);

        var primaryKpiId = work.WorkType?.WorkTypeKpis.FirstOrDefault()?.KpiDefinitionId;
        if (!primaryKpiId.HasValue || !verifiedValue.HasValue)
        {
            return;
        }

        var handover = await db.TaskHandovers
            .AsNoTracking()
            .Where(h => h.WorkId == work.Id && h.Status == HandoverStatus.Accepted)
            .OrderByDescending(h => h.AcceptedAt ?? h.HandoverAt)
            .FirstOrDefaultAsync(cancellationToken);

        var contributions = handover is null
            ? [(submitterEmployeeId, verifiedValue.Value)]
            : SplitHandoverContributions(handover, verifiedValue.Value);

        foreach (var (employeeId, value) in contributions)
        {
            await AddResultItemAsync(db, logger, time, verifiedResult.Id, primaryKpiId.Value, employeeId, value, now, cancellationToken);
        }
    }

    private static (Guid EmployeeId, decimal Value)[] SplitHandoverContributions(TaskHandover handover, decimal totalPoints)
    {
        var (referrerPoints, processorPoints) = ReferralPointSplit.Split(totalPoints, handover.ReferrerSharePercent);
        return
        [
            (handover.FromEmployeeId, referrerPoints),
            (handover.ToEmployeeId, processorPoints),
        ];
    }

    private static async Task AddResultItemAsync(
        AppDbContext db,
        ILogger logger,
        TimeProvider time,
        Guid verifiedWorkResultId,
        Guid kpiDefinitionId,
        Guid employeeId,
        decimal value,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var progress = await ResolveProgressAsync(
            db,
            kpiDefinitionId,
            employeeId,
            time.GetBusinessToday(),
            value,
            now,
            cancellationToken);

        // A verified result item must feed an employee target's progress; without an active target there is nothing to attribute it to.
        if (progress is null)
        {
            LogContributionSkipped(logger, employeeId, kpiDefinitionId, null);
            return;
        }

        db.WorkResultItems.Add(new WorkResultItem
        {
            Id = Guid.NewGuid(),
            VerifiedWorkResultId = verifiedWorkResultId,
            ActualProgressId = progress.Id,
            EmployeeId = employeeId,
            Value = value,
            UpdatedAt = now,
        });
    }

    private static async Task<ActualProgress?> ResolveProgressAsync(
        AppDbContext db,
        Guid kpiDefinitionId,
        Guid employeeId,
        DateOnly progressDate,
        decimal value,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var target = await db.TargetAssignments
            .Where(t => t.KpiDefinitionId == kpiDefinitionId
                && t.EmployeeId == employeeId
                && t.Status == TargetAssignmentStatus.Active
                && t.EvaluationPeriod.StartDate <= progressDate
                && t.EvaluationPeriod.EndDate >= progressDate)
            .OrderBy(t => t.EvaluationPeriod.EndDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (target is null)
        {
            return null;
        }

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
