using System.Linq.Expressions;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.DAL.Entities;

namespace G54.BLL.Common.Extensions;

/// <summary>
/// BR-31: what "disputed" and "rankable" mean for a <see cref="PerformanceEvaluation"/>. A score whose rejected result
/// is under appeal is stored as <see cref="PerformanceEvaluationStatus.Disputed"/> (Disputed_Pending) and is left out
/// of every ranking, average and count.
/// </summary>
public static class PerformanceEvaluationExtensions
{
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
    public static HashSet<Guid> DisputedEmployeeIds(this IEnumerable<PerformanceEvaluation> evaluations) =>
        evaluations.Where(IsDisputedPending).Select(e => e.EmployeeId).ToHashSet();

    /// <summary>The conflict returned when an operation needs scores that are not disputed.</summary>
    public static ConflictException DisputePendingConflict(string message) =>
        new(message, ErrorCodes.EvaluationDisputePending);
}
