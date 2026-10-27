using System.Linq.Expressions;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// BR-31: while an employee's rejected result is under appeal, the score of the evaluation period the result belongs
/// to is "Disputed_Pending". That is stored as <see cref="PerformanceEvaluationStatus.Disputed"/> plus the status it
/// had before (<see cref="PerformanceEvaluation.PreDisputeStatus"/>), so the score can be restored once the last
/// dispute is withdrawn. A disputed score is left out of every ranking and average.
/// This is the single place that knows what "disputed" and "rankable" mean; services must not re-spell either.
/// <see cref="MarkDisputedPendingAsync"/> and <see cref="ReleaseDisputedPendingAsync"/> only change tracked entities;
/// the caller saves them in the same unit of work as the appeal.
/// </summary>
public static class EvaluationDisputeRules
{
    /// <summary>Statuses a score can be disputed from. Drafts carry no score and FINAL scores are locked.</summary>
    private static readonly PerformanceEvaluationStatus[] DisputableStatuses =
    [
        PerformanceEvaluationStatus.Calculated,
        PerformanceEvaluationStatus.InReview,
        PerformanceEvaluationStatus.Approved,
    ];

    /// <summary>
    /// An evaluation that may appear in a ranking, an average or a count: it has a score, is not a draft and is not
    /// disputed. Translatable to SQL, so queries filter with it instead of repeating the status checks.
    /// </summary>
    public static Expression<Func<PerformanceEvaluation, bool>> HasRankableScore { get; } =
        pe => pe.TotalScore != null
            && pe.Status != PerformanceEvaluationStatus.Draft
            && pe.Status != PerformanceEvaluationStatus.Disputed;

    public static bool IsDisputedPending(this PerformanceEvaluation evaluation) =>
        evaluation.Status == PerformanceEvaluationStatus.Disputed;

    /// <summary>The employees whose evaluation is disputed; their count is the disputed-pending count.</summary>
    public static HashSet<Guid> DisputedEmployeeIds(IEnumerable<PerformanceEvaluation> evaluations) =>
        evaluations.Where(IsDisputedPending).Select(e => e.EmployeeId).ToHashSet();

    /// <summary>The conflict returned when an operation needs scores that are not disputed.</summary>
    public static ConflictException PendingConflict(string message) =>
        new(message, ErrorCodes.EvaluationDisputePending);

    /// <summary>
    /// Marks every disputable evaluation of the employee whose period covers the submission date as
    /// <c>DISPUTED</c> (Disputed_Pending).
    /// </summary>
    public static async Task MarkDisputedPendingAsync(
        AppDbContext db,
        Guid employeeId,
        DateTimeOffset submittedAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var evaluations = await LoadEvaluationsAsync(db, employeeId, submittedAt, cancellationToken);
        foreach (var evaluation in evaluations)
        {
            if (!DisputableStatuses.Contains(evaluation.Status)
                || evaluation.EvaluationPeriod.Status is PeriodStatus.Locked or PeriodStatus.Closed)
            {
                continue;
            }

            evaluation.PreDisputeStatus = evaluation.Status;
            evaluation.Status = PerformanceEvaluationStatus.Disputed;
            evaluation.DisputedAt = now;
            evaluation.UpdatedAt = now;
        }
    }

    /// <summary>
    /// Restores the pre-dispute status of the employee's disputed evaluations that no longer have another appeal
    /// in progress. <paramref name="appealId"/> is the appeal being closed and is ignored when looking for others.
    /// </summary>
    public static async Task ReleaseDisputedPendingAsync(
        AppDbContext db,
        Guid appealId,
        Guid employeeId,
        DateTimeOffset submittedAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var evaluations = await LoadEvaluationsAsync(db, employeeId, submittedAt, cancellationToken);
        foreach (var evaluation in evaluations.Where(IsDisputedPending))
        {
            var period = evaluation.EvaluationPeriod;
            var from = period.StartDate.ToBusinessDayStart();
            var to = period.EndDate.AddDays(1).ToBusinessDayStart();

            var anotherInProgress = await db.Appeals.AnyAsync(
                a => a.Id != appealId
                    && a.EmployeeId == employeeId
                    && a.ResultSubmissionId != null
                    && (a.Status == AppealStatus.Open || a.Status == AppealStatus.UnderReview)
                    && a.ResultSubmission!.SubmittedAt >= from
                    && a.ResultSubmission.SubmittedAt < to,
                cancellationToken);
            if (anotherInProgress)
            {
                continue;
            }

            evaluation.Status = evaluation.PreDisputeStatus ?? PerformanceEvaluationStatus.Calculated;
            evaluation.PreDisputeStatus = null;
            evaluation.DisputedAt = null;
            evaluation.UpdatedAt = now;
        }
    }

    private static async Task<List<PerformanceEvaluation>> LoadEvaluationsAsync(
        AppDbContext db,
        Guid employeeId,
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken)
    {
        var date = submittedAt.ToBusinessDate();
        return await db.PerformanceEvaluations
            .Include(e => e.EvaluationPeriod)
            .Where(e => e.EmployeeId == employeeId
                && e.EvaluationPeriod.StartDate <= date
                && e.EvaluationPeriod.EndDate >= date)
            .ToListAsync(cancellationToken);
    }
}
