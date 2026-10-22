using G54.DAL.Entities;

namespace G54.BLL.Common.Workflow;

/// <summary>
/// Single source of truth for the status transitions of a work item.
///
/// <code>
/// NEW / HANDED_OVER / REJECTED ──Start──► IN_PROGRESS
/// NEW / IN_PROGRESS / REJECTED / OVERDUE ──Submit──► SUBMITTED
/// SUBMITTED / IN_PROGRESS ──Reopen──► IN_PROGRESS
/// SUBMITTED / IN_PROGRESS ──Verify──► VERIFIED ──Complete──► DONE
/// SUBMITTED / IN_PROGRESS ──Reject──► REJECTED
/// NEW / IN_PROGRESS ──Handover──► HANDED_OVER
/// NEW / IN_PROGRESS / HANDED_OVER ──MarkOverdue──► OVERDUE
/// NEW / IN_PROGRESS / HANDED_OVER / REJECTED / OVERDUE ──Cancel──► CANCELLED
/// </code>
///
/// A revision request sends the work back to IN_PROGRESS while its submission stays UNDER_REVIEW, and the verifier
/// may decide on that submission again, so Verify / Reject / Reopen are also allowed from IN_PROGRESS
/// (see <see cref="SubmissionStateMachine"/>).
///
/// OVERDUE work cannot be started again: the overdue job re-marks any open work past its planned end date, so the
/// work could only flip back. It leaves OVERDUE by being submitted or cancelled.
///
/// DONE and CANCELLED have no outgoing transition. The machine only answers which status an action leads to;
/// callers decide which error to raise when the action is not allowed.
/// </summary>
public static class WorkStateMachine
{
    private static readonly Dictionary<(WorkStatus From, WorkAction Action), WorkStatus> Transitions = BuildTransitions();

    /// <summary>
    /// Returns true and the resulting status when <paramref name="action"/> is allowed from <paramref name="current"/>;
    /// returns false when the current status does not allow the action.
    /// </summary>
    public static bool TryGetNextStatus(WorkStatus current, WorkAction action, out WorkStatus next)
        => Transitions.TryGetValue((current, action), out next);

    /// <summary>The statuses reachable from <paramref name="current"/> by any action.</summary>
    public static IReadOnlyCollection<WorkStatus> GetNextStatuses(WorkStatus current)
        => Transitions
            .Where(entry => entry.Key.From == current)
            .Select(entry => entry.Value)
            .Distinct()
            .ToArray();

    private static Dictionary<(WorkStatus From, WorkAction Action), WorkStatus> BuildTransitions()
    {
        var table = new Dictionary<(WorkStatus From, WorkAction Action), WorkStatus>();

        Allow(table, WorkAction.Start, WorkStatus.InProgress, WorkStatus.New, WorkStatus.HandedOver, WorkStatus.Rejected);
        Allow(table, WorkAction.Submit, WorkStatus.Submitted, WorkStatus.New, WorkStatus.InProgress, WorkStatus.Rejected, WorkStatus.Overdue);
        Allow(table, WorkAction.Reopen, WorkStatus.InProgress, WorkStatus.Submitted, WorkStatus.InProgress);
        Allow(table, WorkAction.Verify, WorkStatus.Verified, WorkStatus.Submitted, WorkStatus.InProgress);
        Allow(table, WorkAction.Reject, WorkStatus.Rejected, WorkStatus.Submitted, WorkStatus.InProgress);
        Allow(table, WorkAction.Complete, WorkStatus.Done, WorkStatus.Verified);
        Allow(table, WorkAction.Cancel, WorkStatus.Cancelled, WorkStatus.New, WorkStatus.InProgress, WorkStatus.HandedOver, WorkStatus.Rejected, WorkStatus.Overdue);
        Allow(table, WorkAction.Handover, WorkStatus.HandedOver, WorkStatus.New, WorkStatus.InProgress);
        Allow(table, WorkAction.MarkOverdue, WorkStatus.Overdue, WorkStatus.New, WorkStatus.InProgress, WorkStatus.HandedOver);

        return table;
    }

    private static void Allow(
        Dictionary<(WorkStatus From, WorkAction Action), WorkStatus> table,
        WorkAction action,
        WorkStatus next,
        params WorkStatus[] from)
    {
        foreach (var status in from)
        {
            table[(status, action)] = next;
        }
    }
}
