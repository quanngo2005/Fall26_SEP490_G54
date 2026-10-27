using System.Linq.Expressions;
using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>Rules about an appeal's own status, shared so no service re-spells which statuses are "in progress".</summary>
public static class AppealRules
{
    /// <summary>An appeal still awaiting a decision (OPEN or UNDER_REVIEW); WITHDRAWN and decided appeals are not.</summary>
    public static Expression<Func<Appeal, bool>> InProgress { get; } =
        a => a.Status == AppealStatus.Open || a.Status == AppealStatus.UnderReview;
}
