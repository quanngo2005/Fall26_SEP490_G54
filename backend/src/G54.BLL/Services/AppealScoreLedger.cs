using G54.BLL.Common.Exceptions;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

internal static class AppealScoreLedger
{
    public static async Task<PerformanceEvaluation> CurrentAsync(AppDbContext db, Guid employeeId, DateOnly today, CancellationToken ct) =>
        await db.PerformanceEvaluations.Include(e => e.EvaluationPeriod)
            .Where(e => e.EmployeeId == employeeId && e.TotalScore != null
                && (e.Status == PerformanceEvaluationStatus.Calculated || e.Status == PerformanceEvaluationStatus.InReview
                    // BR-31 freezes the filer's score as Disputed; it is still adjustable unless it was already approved.
                    || (e.Status == PerformanceEvaluationStatus.Disputed
                        && (e.PreDisputeStatus == null || e.PreDisputeStatus == PerformanceEvaluationStatus.Calculated
                            || e.PreDisputeStatus == PerformanceEvaluationStatus.InReview)))
                && (e.EvaluationPeriod.Status == PeriodStatus.Open || e.EvaluationPeriod.Status == PeriodStatus.InReview)
                && e.EvaluationPeriod.StartDate <= today && e.EvaluationPeriod.EndDate >= today)
            .OrderByDescending(e => e.EvaluationPeriod.StartDate).ThenBy(e => e.EvaluationPeriod.EndDate).ThenBy(e => e.Id)
            .FirstOrDefaultAsync(ct) ?? throw new ConflictException("Nhân viên chưa có tổng điểm KPI hiện tại có thể điều chỉnh.");

    public static ScoreEntry Add(AppDbContext db, PerformanceEvaluation evaluation, decimal points, ScoreEntryKind kind, ScoreEntrySource source,
        string reason, Guid actor, DateTimeOffset now, Guid? appealId = null, Guid? workId = null, Guid? reverses = null)
    {
        var entry = new ScoreEntry
        {
            Id = Guid.NewGuid(),
            EvaluationId = evaluation.Id,
            EmployeeId = evaluation.EmployeeId,
            AppealId = appealId,
            WorkId = workId,
            ReversesEntryId = reverses,
            RecordedByEmployeeId = actor,
            Points = points,
            Kind = kind,
            Source = source,
            Reason = reason,
            CreatedAt = now,
        };
        db.ScoreEntries.Add(entry);
        evaluation.TotalScore += points;
        evaluation.UpdatedAt = now;
        return entry;
    }

    /// <summary>
    /// Reverses the appeal's hold. The refund lands in the held evaluation while it is still open; once that period is
    /// locked/closed (or the evaluation approved/final) it counts toward the employee's current period instead, so the
    /// closed period keeps its held score.
    /// </summary>
    public static async Task RefundAsync(AppDbContext db, Guid appealId, Guid actor, DateOnly today, DateTimeOffset now, CancellationToken ct)
    {
        var hold = await db.ScoreEntries.AsNoTracking().SingleOrDefaultAsync(e => e.AppealId == appealId && e.Kind == ScoreEntryKind.Hold, ct);
        if (hold is null || await db.ScoreEntries.AnyAsync(e => e.ReversesEntryId == hold.Id, ct))
        {
            return;
        }

        var evaluation = await db.PerformanceEvaluations.Include(e => e.EvaluationPeriod).SingleAsync(e => e.Id == hold.EvaluationId, ct);
        if (evaluation.EvaluationPeriod.Status is PeriodStatus.Locked or PeriodStatus.Closed
            || evaluation.Status is PerformanceEvaluationStatus.Approved or PerformanceEvaluationStatus.Final
            || (evaluation.Status == PerformanceEvaluationStatus.Disputed && evaluation.PreDisputeStatus == PerformanceEvaluationStatus.Approved))
        {
            evaluation = await CurrentAsync(db, hold.EmployeeId, today, ct);
        }

        Add(db, evaluation, -hold.Points, ScoreEntryKind.Refund, ScoreEntrySource.Reversal, "Hoàn khoản tạm giữ khi kết quả khiếu nại được duyệt.", actor, now, appealId, hold.WorkId, hold.Id);
    }

    public static async Task<decimal> NetAsync(AppDbContext db, Guid employeeId, Guid periodId, CancellationToken ct) =>
        await db.ScoreEntries.Where(e => e.EmployeeId == employeeId && e.Evaluation.EvaluationPeriodId == periodId)
            .SumAsync(e => (decimal?)e.Points, ct) ?? 0m;
}
