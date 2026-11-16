using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Validation;
using G54.BLL.Dtos.Submission;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// Records the verified result and KPI contribution of an approved submission. Shared by the verification decision and
/// by an ACCEPT appeal decision. ADJUST requests revision and does not record a verified result.
/// Every metric the employee submitted contributes to its own KPI (a work type can map several KPIs).
/// A metric whose KPI has an in-effect mapping is rejected (409 <c>KPI_TARGET_NOT_ASSIGNED</c>) when the processor has no
/// active target; a handover referrer without one only loses their share (the processor keeps theirs).
/// </summary>
internal static class VerifiedResultRecorder
{
    /// <param name="work">The submission's work, with <c>WorkType.WorkTypeKpis.KpiDefinition</c> loaded.</param>
    /// <param name="metrics">The per-KPI values the employee submitted with the result.</param>
    public static async Task RecordAsync(
        AppDbContext db,
        TimeProvider time,
        Verification verification,
        Work work,
        Guid submitterEmployeeId,
        IReadOnlyList<SubmissionMetricInputDto> metrics,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // A work item (Task ID) may contribute KPI to the system only once; a result QA sent back for rework no longer counts.
        var alreadyContributed = await db.WorkResultItems.AnyAsync(
            i => i.VerifiedWorkResult.Verification!.ResultSubmission.WorkId == work.Id
                && i.VerifiedWorkResult.Status == VerifiedResultStatus.Valid,
            cancellationToken);
        if (alreadyContributed)
        {
            throw new BusinessRuleException(ErrorCodes.KpiAlreadyContributed, statusCode: StatusCodes.Status409Conflict);
        }

        var progressDate = time.GetBusinessToday();
        var lines = new List<ContributionLine>();
        if (metrics.Count > 0)
        {
            var handover = await db.TaskHandovers
                .AsNoTracking()
                .Where(h => h.WorkId == work.Id && h.Status == HandoverStatus.Accepted)
                .OrderByDescending(h => h.AcceptedAt ?? h.HandoverAt)
                .FirstOrDefaultAsync(cancellationToken);

            foreach (var metric in metrics.DistinctBy(m => m.KpiDefinitionId))
            {
                var mapping = SelectInEffectMapping(work.WorkType, progressDate, metric.KpiDefinitionId);
                var aggregation = mapping?.KpiDefinition.AggregationType;
                if (mapping is null || aggregation == KpiAggregationType.NoAggregation)
                {
                    continue;
                }

                var denominator = DenominatorOf(aggregation!.Value, metric);
                var processorId = submitterEmployeeId;
                var processorValue = metric.Value;
                if (handover is not null)
                {
                    var (referrerPoints, processorPoints) = ReferralPointSplit.Split(metric.Value, handover.ReferrerSharePercent);
                    processorId = handover.ToEmployeeId;
                    processorValue = processorPoints;

                    // Same rule as the import reconcile: a referrer without an active target for this KPI (a Teller
                    // referring a term deposit) loses only their share; the processor is still credited.
                    var referrerTarget = await FindActiveTargetAsync(db, mapping.KpiDefinitionId, handover.FromEmployeeId, progressDate, cancellationToken);
                    if (referrerTarget is not null)
                    {
                        PeriodLockGuard.EnsureWritable(referrerTarget.EvaluationPeriod);
                        lines.Add(new ContributionLine(referrerTarget, handover.FromEmployeeId, referrerPoints, denominator, mapping.Multiplier));
                    }
                }

                // Resolved before anything is written, so a processor without a target rejects the whole approval.
                var target = await FindActiveTargetAsync(db, mapping.KpiDefinitionId, processorId, progressDate, cancellationToken)
                    ?? throw await TargetNotAssignedAsync(db, processorId, cancellationToken);

                // Progress is never added to a locked period. Reversal entries (BR-16) are not implemented yet.
                PeriodLockGuard.EnsureWritable(target.EvaluationPeriod);
                lines.Add(new ContributionLine(target, processorId, processorValue, denominator, mapping.Multiplier));
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

        foreach (var line in lines)
        {
            await AddResultItemAsync(db, verifiedResult.Id, line, progressDate, now, cancellationToken);
        }
    }

    /// <summary>
    /// Credits every KPI mapped to the work type for one matched imported transaction (docs/api/WORK_TRANSACTION_CONTRACT.md).
    /// COUNT mappings credit 1, AMOUNT mappings credit the file's amount. With a <paramref name="referral"/> the value is
    /// split between the referrer and the assignee (the processor). Throws before writing anything when a KPI cannot be
    /// credited to the assignee; a referrer without an active target for a KPI only loses their share of that KPI.
    /// </summary>
    /// <param name="work">The work, with <c>WorkType.WorkTypeKpis.KpiDefinition</c> loaded.</param>
    /// <returns><c>false</c> when the referrer's share of at least one KPI was not credited (no active target).</returns>
    public static async Task<bool> RecordImportedAsync(
        AppDbContext db,
        WorkTransaction workTransaction,
        Work work,
        ImportedTransaction imported,
        ImportedReferral? referral,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var progressDate = imported.TransactionDate;
        var inEffect = InEffectMappings(work.WorkType, progressDate)
            .GroupBy(k => k.KpiDefinitionId)
            .Select(g => g.OrderByDescending(k => k.EffectiveFrom).ThenBy(k => k.Id).First())
            .ToList();

        // BR-09: an imported transaction is a quantitative measure. KPIs fed by another file (service ratings) are not
        // credited from it; when the work type maps only such KPIs the automatic mapping is blocked (MSG13) and left
        // to the manager.
        var mappings = inEffect.Where(k => k.KpiDefinition.DataSource == KpiDataSource.Transaction).ToList();
        if (inEffect.Count > 0 && mappings.Count == 0)
        {
            throw new BusinessRuleException(ErrorCodes.MetricTypeMismatch, statusCode: StatusCodes.Status409Conflict);
        }

        var lines = new List<ContributionLine>();
        var referrerCredited = true;
        foreach (var mapping in mappings)
        {
            var aggregation = mapping.KpiDefinition.AggregationType;
            if (aggregation == KpiAggregationType.NoAggregation)
            {
                throw new BusinessRuleException(ErrorCodes.KpiAggregationNotSupported, statusCode: StatusCodes.Status409Conflict);
            }

            var raw = mapping.ValueSource == KpiValueSource.Amount ? imported.Amount : 1m;
            var denominator = aggregation switch
            {
                KpiAggregationType.Ratio => imported.Denominator is > 0m
                    ? imported.Denominator
                    : throw new ValidationException("KPI dạng tỷ lệ (RATIO) yêu cầu mẫu số (denominator) lớn hơn 0."),
                KpiAggregationType.Avg => 1m,
                _ => null,
            };
            var processorValue = raw;
            if (referral is not null)
            {
                var (referrerPoints, processorPoints) = ReferralPointSplit.Split(raw, referral.SharePercent);
                processorValue = processorPoints;
                var referrerTarget = await FindActiveTargetAsync(db, mapping.KpiDefinitionId, referral.ReferrerEmployeeId, progressDate, cancellationToken);
                if (referrerTarget is null)
                {
                    // The referrer often has no target for the product's KPI (a Teller referring a term deposit): the
                    // processor keeps their share and the referrer's share is not credited, instead of blocking both.
                    referrerCredited = false;
                }
                else
                {
                    PeriodLockGuard.EnsureWritable(referrerTarget.EvaluationPeriod);
                    lines.Add(new ContributionLine(referrerTarget, referral.ReferrerEmployeeId, referrerPoints, denominator, mapping.Multiplier));
                }
            }

            var target = await FindActiveTargetAsync(db, mapping.KpiDefinitionId, work.AssigneeEmployeeId, progressDate, cancellationToken)
                ?? throw await TargetNotAssignedAsync(db, work.AssigneeEmployeeId, cancellationToken);
            PeriodLockGuard.EnsureWritable(target.EvaluationPeriod);
            lines.Add(new ContributionLine(target, work.AssigneeEmployeeId, processorValue, denominator, mapping.Multiplier));
        }

        var verifiedResult = new VerifiedWorkResult
        {
            Id = Guid.NewGuid(),
            WorkTransactionId = workTransaction.Id,
            VerifiedAt = now,
            UpdatedAt = now,
            Status = VerifiedResultStatus.Valid,
        };
        db.VerifiedWorkResults.Add(verifiedResult);
        foreach (var line in lines)
        {
            await AddResultItemAsync(db, verifiedResult.Id, line, progressDate, now, cancellationToken);
        }

        return referrerCredited;
    }

    /// <summary>
    /// The WorkTypeKpi mapping in effect on <paramref name="date"/>
    /// (<c>EffectiveFrom &lt;= date &amp;&amp; (EffectiveTo == null || EffectiveTo &gt;= date)</c>): a required KPI first,
    /// then latest <c>EffectiveFrom</c>, then <c>Id</c> so the pick never depends on the order the database returns
    /// the mappings in. <c>null</c> when the work type has no mapping in effect (e.g. expired or not started), in
    /// which case the approval records no KPI contribution.
    /// </summary>
    internal static WorkTypeKpi? SelectInEffectMapping(WorkType? workType, DateOnly date) =>
        InEffectMappings(workType, date)
            .OrderByDescending(k => k.IsRequired)
            .ThenByDescending(k => k.EffectiveFrom)
            .ThenBy(k => k.Id)
            .FirstOrDefault();

    /// <summary>The mapping of one KPI in effect on <paramref name="date"/>: the latest <c>EffectiveFrom</c>, then <c>Id</c>.</summary>
    internal static WorkTypeKpi? SelectInEffectMapping(WorkType? workType, DateOnly date, Guid kpiDefinitionId) =>
        InEffectMappings(workType, date)
            .Where(k => k.KpiDefinitionId == kpiDefinitionId)
            .OrderByDescending(k => k.EffectiveFrom)
            .ThenBy(k => k.Id)
            .FirstOrDefault();

    private static IEnumerable<WorkTypeKpi> InEffectMappings(WorkType? workType, DateOnly date) =>
        workType?.WorkTypeKpis.Where(k => k.EffectiveFrom <= date && (k.EffectiveTo == null || k.EffectiveTo >= date))
        ?? [];

    // RATIO adds the submitted denominator, AVG counts one entry per contribution, SUM has none.
    private static decimal? DenominatorOf(KpiAggregationType aggregation, SubmissionMetricInputDto metric) =>
        aggregation switch
        {
            KpiAggregationType.Ratio => metric.Denominator is > 0m
                ? metric.Denominator
                : throw new ValidationException("KPI dạng tỷ lệ (RATIO) yêu cầu mẫu số (denominator) lớn hơn 0."),
            KpiAggregationType.Avg => 1m,
            _ => null,
        };

    // A verified result item must feed an employee target's progress, so approval needs an active target for each
    // recipient. A late result (its transaction date before the newer version's effective date, BR-15) is attributed
    // to the version in effect on that date: the ACTIVE version if its EffectiveFrom is already past, otherwise the
    // SUPERSEDED version whose [EffectiveFrom, EffectiveTo] window contains the date. A SUPERSEDED row with
    // EffectiveTo == null was replaced before BR-15 existed and is never a candidate.
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
                && t.EvaluationPeriod.StartDate <= progressDate
                && t.EvaluationPeriod.EndDate >= progressDate
                && ((t.Status == TargetAssignmentStatus.Active && t.EffectiveFrom <= progressDate)
                    || (t.Status == TargetAssignmentStatus.Superseded && t.EffectiveTo != null
                        && t.EffectiveFrom <= progressDate && t.EffectiveTo >= progressDate)))
            .OrderBy(t => t.EvaluationPeriod.EndDate)
            .ThenByDescending(t => t.Version)
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
        Guid verifiedWorkResultId,
        ContributionLine line,
        DateOnly progressDate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // The ledger keeps the real amount; the multiplier only converts it to points when the KPI is scored.
        var weightedValue = Math.Round(line.RawValue * line.Multiplier, 4, MidpointRounding.ToEven);
        var progress = await AddToProgressAsync(
            db, line.Target, progressDate, line.RawValue, line.Denominator, now, cancellationToken);

        db.WorkResultItems.Add(new WorkResultItem
        {
            Id = Guid.NewGuid(),
            VerifiedWorkResultId = verifiedWorkResultId,
            ActualProgressId = progress.Id,
            EmployeeId = line.EmployeeId,
            RawValue = line.RawValue,
            MultiplierApplied = line.Multiplier,
            Value = weightedValue,
            Denominator = line.Denominator,
            UpdatedAt = now,
        });
    }

    private static async Task<ActualProgress> AddToProgressAsync(
        AppDbContext db,
        TargetAssignment target,
        DateOnly progressDate,
        decimal value,
        decimal? denominator,
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

        // A day's row that QA rework emptied was marked INVALID by the database (invalidate_source_actual_progress);
        // a new verified contribution makes it live again, starting from nothing.
        if (progress.Status == ActualProgressStatus.Invalid)
        {
            progress.Status = ActualProgressStatus.Valid;
            progress.Value = 0;
            progress.Denominator = null;
        }

        progress.Value += value;
        if (denominator.HasValue)
        {
            progress.Denominator = (progress.Denominator ?? 0m) + denominator.Value;
        }

        progress.UpdatedAt = now;
        return progress;
    }

    /// <summary>Who referred the customer of an imported transaction and their share (percent) of every KPI value.</summary>
    internal sealed record ImportedReferral(Guid ReferrerEmployeeId, decimal SharePercent);

    private sealed record ContributionLine(
        TargetAssignment Target,
        Guid EmployeeId,
        decimal RawValue,
        decimal? Denominator,
        decimal Multiplier);
}
