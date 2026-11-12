using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.PerformanceDashboard;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed partial class PerformanceDashboardService
{
    private const string TypeResult = "RESULT";
    private const string TypeRiskPenalty = "RISK_PENALTY";
    private const int MaxPageSize = 100;

    public async Task<EmployeeLedgerDto> GetEmployeeLedgerAsync(
        Guid accountId,
        Guid employeeId,
        Guid? periodId,
        Guid? kpiId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
        {
            throw new ValidationException($"page phải từ 1 và pageSize phải từ 1 đến {MaxPageSize}.");
        }

        await EnsureCanViewEmployeeAsync(accountId, employeeId, cancellationToken);
        var (period, _) = await ResolvePeriodAsync(periodId, cancellationToken);
        if (period is null)
        {
            return new EmployeeLedgerDto(null, new LedgerSummaryDto(0m, 0m, 0m), new PagedResult<LedgerEntryDto>([], 0, page, pageSize));
        }

        var rows = await LoadResultRowsAsync(
            db.WorkResultItems.AsNoTracking()
                .Where(i => i.EmployeeId == employeeId
                    && i.VerifiedWorkResult.Status == VerifiedResultStatus.Valid
                    && i.ActualProgress.Status == ActualProgressStatus.Valid
                    && i.ActualProgress.ProgressDate >= period.StartDate
                    && i.ActualProgress.ProgressDate <= period.EndDate
                    && (kpiId == null || i.ActualProgress.TargetAssignment.KpiDefinitionId == kpiId)),
            cancellationToken);
        var resultPoints = rows.Sum(r => r.Entry.Points);
        var entries = rows.Select(r => r.Entry).ToList();

        var riskPenalty = 0m;
        var scoreEntryPoints = 0m;
        if (kpiId is null)
        {
            var periodStart = period.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var periodEnd = period.EndDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            var risks = (await db.RiskEvents.AsNoTracking()
                    .Where(r => r.EmployeeId == employeeId
                        && r.OccurredAt >= periodStart
                        && r.OccurredAt <= periodEnd
                        && r.Status != RiskEventStatus.Dismissed)
                    .ToListAsync(cancellationToken))
                .Select(ToRiskEntry)
                .ToList();
            var scoreEntries = (await db.ScoreEntries.AsNoTracking()
                    .Where(s => s.EmployeeId == employeeId && s.Evaluation.EvaluationPeriodId == period.Id)
                    .ToListAsync(cancellationToken))
                .Select(ToScoreEntry)
                .ToList();

            riskPenalty = risks.Sum(r => r.Points);
            scoreEntryPoints = scoreEntries.Sum(s => s.Points);
            entries.AddRange(risks);
            entries.AddRange(scoreEntries);
        }

        var pageItems = entries
            .OrderByDescending(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new EmployeeLedgerDto(
            ToDto(period),
            new LedgerSummaryDto(Round(resultPoints), Round(riskPenalty), Round(scoreEntryPoints)),
            new PagedResult<LedgerEntryDto>(pageItems, entries.Count, page, pageSize));
    }

    public async Task<LedgerEntryDetailDto> GetLedgerEntryAsync(
        Guid accountId,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveEntryAsync(entryId, cancellationToken);
        await EnsureCanViewEmployeeAsync(accountId, context.EmployeeId, cancellationToken);

        LedgerWorkDto? work = null;
        if (context.Entry.WorkId is { } workId)
        {
            var w = await db.Works.AsNoTracking()
                .Where(x => x.Id == workId)
                .Select(x => new { x.Id, x.Code, x.Title, WorkTypeCode = x.WorkType == null ? null : x.WorkType.Code, x.Status })
                .SingleOrDefaultAsync(cancellationToken);
            work = w is null ? null : new LedgerWorkDto(w.Id, w.Code, w.Title, w.WorkTypeCode, w.Status.ToEnumString());
        }

        LedgerSubmissionDto? submission = null;
        if (context.VerificationId is { } verificationId)
        {
            var v = await db.Verifications.AsNoTracking()
                .Where(x => x.Id == verificationId)
                .Select(x => new
                {
                    x.ResultSubmission.Id,
                    x.ResultSubmission.SubmissionNo,
                    x.ResultSubmission.Status,
                    x.ResultSubmission.SubmittedAt,
                    x.ResultSubmission.Comment,
                    VerifierName = x.VerifierEmployee.FullName,
                    x.Decision,
                    x.VerifiedAt,
                })
                .SingleOrDefaultAsync(cancellationToken);
            submission = v is null
                ? null
                : new LedgerSubmissionDto(v.Id, v.SubmissionNo, v.Status.ToEnumString(), v.SubmittedAt, v.Comment, v.VerifierName, v.Decision.ToEnumString(), v.VerifiedAt);
        }

        LedgerRiskEventDto? riskEvent = null;
        if (context.RiskEventId is { } riskEventId)
        {
            var r = await db.RiskEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == riskEventId, cancellationToken);
            riskEvent = r is null ? null : new LedgerRiskEventDto(r.Id, r.Code, r.EventType, r.Severity.ToEnumString(), r.Status.ToEnumString(), r.Description);
        }

        LedgerAppealDto? appeal = null;
        if (context.AppealId is { } appealId)
        {
            var a = await db.Appeals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == appealId, cancellationToken);
            appeal = a is null ? null : new LedgerAppealDto(a.Id, a.Status.ToEnumString(), a.Reason);
        }

        var recordedBy = context.RecordedByEmployeeId is { } recorderId
            ? await db.Employees.AsNoTracking()
                .Where(e => e.Id == recorderId)
                .Select(e => e.FullName)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        return new LedgerEntryDetailDto(context.Entry, context.EmployeeId, work, submission, riskEvent, appeal, recordedBy);
    }

    public async Task<LedgerTransactionsDto> GetLedgerEntryTransactionsAsync(
        Guid accountId,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveEntryAsync(entryId, cancellationToken);
        await EnsureCanViewEmployeeAsync(accountId, context.EmployeeId, cancellationToken);

        if (context.Entry.WorkId is not { } workId)
        {
            return new LedgerTransactionsDto(entryId, null, []);
        }

        var items = await db.WorkTransactions.AsNoTracking()
            .Where(t => t.WorkId == workId)
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.TransactionCode)
            .Select(t => new LedgerTransactionDto(
                t.Id,
                t.TransactionCode,
                t.Status,
                t.Reason,
                t.Id == context.WorkTransactionId,
                t.ImportedTransaction == null ? null : t.ImportedTransaction.ProductCode,
                t.ImportedTransaction == null ? null : t.ImportedTransaction.Amount,
                t.ImportedTransaction == null ? null : t.ImportedTransaction.TransactionDate,
                t.ImportedTransaction == null ? null : t.ImportedTransaction.ReferEmployeeCode,
                t.ImportedTransaction == null ? null : t.ImportedTransaction.ImportBatch.FileName,
                t.ImportedTransaction == null ? null : t.ImportedTransaction.ImportBatch.ImportedAt))
            .ToListAsync(cancellationToken);

        return new LedgerTransactionsDto(entryId, workId, items);
    }

    // ----- Entry sources --------------------------------------------------------------------

    private async Task<EntryContext> ResolveEntryAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var result = (await LoadResultRowsAsync(
                db.WorkResultItems.AsNoTracking().Where(i => i.Id == entryId),
                cancellationToken))
            .SingleOrDefault();
        if (result is not null)
        {
            return new EntryContext(result.Entry, result.EmployeeId, result.WorkTransactionId, result.VerificationId, null, null, null);
        }

        var risk = await db.RiskEvents.AsNoTracking().SingleOrDefaultAsync(r => r.Id == entryId, cancellationToken);
        if (risk is not null)
        {
            return new EntryContext(ToRiskEntry(risk), risk.EmployeeId, null, null, risk.Id, null, risk.RecordedBy);
        }

        var score = await db.ScoreEntries.AsNoTracking().SingleOrDefaultAsync(s => s.Id == entryId, cancellationToken)
            ?? throw new NotFoundException("Dòng sổ cái", entryId);
        return new EntryContext(ToScoreEntry(score), score.EmployeeId, null, null, null, score.AppealId, score.RecordedByEmployeeId);
    }

    private async Task<List<ResultRow>> LoadResultRowsAsync(IQueryable<WorkResultItem> items, CancellationToken cancellationToken)
    {
        var rows = await items
            .Select(i => new
            {
                i.Id,
                i.EmployeeId,
                i.Value,
                i.RawValue,
                i.MultiplierApplied,
                i.VerifiedWorkResult.VerifiedAt,
                i.VerifiedWorkResult.WorkTransactionId,
                i.VerifiedWorkResult.VerificationId,
                WorkId = i.VerifiedWorkResult.WorkTransaction != null
                    ? i.VerifiedWorkResult.WorkTransaction.WorkId
                    : i.VerifiedWorkResult.Verification != null
                        ? (Guid?)i.VerifiedWorkResult.Verification.ResultSubmission.WorkId
                        : null,
                KpiId = i.ActualProgress.TargetAssignment.KpiDefinitionId,
                KpiCode = i.ActualProgress.TargetAssignment.KpiDefinition.Code,
            })
            .ToListAsync(cancellationToken);

        var workIds = rows.Where(r => r.WorkId.HasValue).Select(r => r.WorkId!.Value).Distinct().ToList();
        var works = await db.Works.AsNoTracking()
            .Where(w => workIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Code, w.Title })
            .ToDictionaryAsync(w => w.Id, cancellationToken);

        return rows.Select(r =>
        {
            works.TryGetValue(r.WorkId ?? Guid.Empty, out var work);
            return new ResultRow(
                new LedgerEntryDto(
                    r.Id, TypeResult, r.VerifiedAt, r.Value, r.RawValue, r.MultiplierApplied,
                    r.KpiId, r.KpiCode, r.WorkId, work?.Code, work?.Title),
                r.EmployeeId,
                r.WorkTransactionId,
                r.VerificationId);
        }).ToList();
    }

    private static LedgerEntryDto ToRiskEntry(RiskEvent risk) => new(
        risk.Id, TypeRiskPenalty, risk.OccurredAt, -EvaluationService.RiskPenalty(risk),
        null, null, null, null, null, null, risk.Description);

    private static LedgerEntryDto ToScoreEntry(ScoreEntry score) => new(
        score.Id, score.Kind.ToEnumString(), score.CreatedAt, score.Points,
        null, null, null, null, score.WorkId, null, score.Reason);

    private sealed record ResultRow(LedgerEntryDto Entry, Guid EmployeeId, Guid? WorkTransactionId, Guid? VerificationId);

    private sealed record EntryContext(
        LedgerEntryDto Entry,
        Guid EmployeeId,
        Guid? WorkTransactionId,
        Guid? VerificationId,
        Guid? RiskEventId,
        Guid? AppealId,
        Guid? RecordedByEmployeeId);
}
