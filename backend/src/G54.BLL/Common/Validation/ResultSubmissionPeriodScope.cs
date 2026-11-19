using System.Linq.Expressions;
using G54.BLL.Common.Extensions;
using G54.DAL.Entities;

namespace G54.BLL.Common.Validation;

/// <summary>
/// Which evaluation period a result submission counts against for period-close purposes (pending counts, pre-close
/// check, period CloseAsync, appeal-to-period linking). A submission still owned by its originating period is
/// matched by <c>SubmittedAt</c> falling inside the period's business-day window (UTC+7, not UTC midnight, which
/// was the source of a 7-hour boundary bug). Once <c>FinalizeAndLockPeriodAsync</c> carries a pending submission
/// over to the next period (<see cref="ResultSubmission.CarriedOverToPeriodId"/>), it belongs to that period
/// instead, regardless of when it was originally submitted.
/// </summary>
internal static class ResultSubmissionPeriodScope
{
    /// <summary>The business-day [from, to) bounds of a period, in UTC, for a <c>SubmittedAt</c> comparison.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) Bounds(DateOnly start, DateOnly end) =>
        (StartOfBusinessDay(start), StartOfBusinessDay(end.AddDays(1)));

    /// <summary>Predicate over <see cref="ResultSubmission"/> directly (pending counts, pre-close check, CloseAsync).</summary>
    public static Expression<Func<ResultSubmission, bool>> InPeriod(Guid periodId, DateOnly start, DateOnly end)
    {
        var (from, to) = Bounds(start, end);
        return r => r.CarriedOverToPeriodId == periodId
            || (r.CarriedOverToPeriodId == null && r.SubmittedAt >= from && r.SubmittedAt < to);
    }

    /// <summary>
    /// Ids of the submissions that count against the period, for appeal filters: used inside an appeal predicate as
    /// <c>ids.Contains(a.ResultSubmissionId.Value)</c>, EF inlines it as a subquery, so the rule lives only in <see cref="InPeriod"/>.
    /// </summary>
    public static IQueryable<Guid> IdsInPeriod(IQueryable<ResultSubmission> submissions, Guid periodId, DateOnly start, DateOnly end) =>
        submissions.Where(InPeriod(periodId, start, end)).Select(r => r.Id);

    public static DateTimeOffset StartOfBusinessDay(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeProviderExtensions.BusinessUtcOffset).ToUniversalTime();
}
