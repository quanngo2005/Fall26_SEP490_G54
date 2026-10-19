using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.DAL.Entities;

namespace G54.BLL.Common.Workflow;

/// <summary>
/// Single source of truth for Work lifecycle transitions.
///
/// <code>
/// NEW ──Start──────────► IN_PROGRESS ──Submit──► SUBMITTED ──Approve──► VERIFIED ──Complete──► DONE
///  │                        ▲   ▲                 │  │  │
///  │                        │   └─Withdraw/───────┘  │  └─Reject──► REJECTED ──Rework──► IN_PROGRESS
///  │                        │     RequestRevision    │
///  └─Submit (auto-start)────┼────────────────────────┘
///  NEW/IN_PROGRESS/REJECTED ──Handover──► HANDED_OVER ──AcceptHandover──► IN_PROGRESS
///  NEW/IN_PROGRESS/HANDED_OVER/REJECTED ──Cancel──► CANCELLED
/// </code>
///
/// OVERDUE is not a stored state: it is derived by <see cref="IsOverdue"/> from the planned end date for
/// works that have not been delivered yet, so an overdue work can still be submitted.
/// </summary>
public static class WorkStateMachine
{
    private static readonly Dictionary<(WorkStatus From, WorkTrigger Trigger), WorkStatus> Transitions = BuildTransitions();

    public static bool CanFire(WorkStatus from, WorkTrigger trigger)
        => Transitions.ContainsKey((from, trigger));

    public static bool TryFire(WorkStatus from, WorkTrigger trigger, out WorkStatus to)
        => Transitions.TryGetValue((from, trigger), out to);

    /// <summary>
    /// Returns the next status or throws a 409 <see cref="ConflictException"/> when the trigger is not allowed.
    /// </summary>
    public static WorkStatus Fire(WorkStatus from, WorkTrigger trigger)
    {
        if (TryFire(from, trigger, out var to))
        {
            return to;
        }

        throw new ConflictException(
            $"Công việc đang ở trạng thái {from.ToWire()}, không thể thực hiện thao tác {trigger}.",
            ErrorCodes.WorkInvalidTransition);
    }

    public static IReadOnlyList<WorkTrigger> GetAvailableTriggers(WorkStatus from)
        => Transitions.Keys
            .Where(key => key.From == from)
            .Select(key => key.Trigger)
            .Order()
            .ToArray();

    /// <summary>
    /// A work is overdue when its planned end date has passed and the assignee has not delivered a result yet.
    /// </summary>
    public static bool IsOverdue(WorkStatus status, DateOnly? plannedEndDate, DateOnly today)
        => plannedEndDate.HasValue
            && plannedEndDate.Value < today
            && status is WorkStatus.New or WorkStatus.InProgress or WorkStatus.HandedOver or WorkStatus.Rejected;

    private static Dictionary<(WorkStatus From, WorkTrigger Trigger), WorkStatus> BuildTransitions()
    {
        var table = new Dictionary<(WorkStatus From, WorkTrigger Trigger), WorkStatus>();

        Allow(table, WorkTrigger.Start, WorkStatus.InProgress, WorkStatus.New);
        Allow(table, WorkTrigger.Handover, WorkStatus.HandedOver, WorkStatus.New, WorkStatus.InProgress, WorkStatus.Rejected);
        Allow(table, WorkTrigger.AcceptHandover, WorkStatus.InProgress, WorkStatus.HandedOver);
        Allow(table, WorkTrigger.Submit, WorkStatus.Submitted, WorkStatus.New, WorkStatus.InProgress, WorkStatus.HandedOver, WorkStatus.Rejected);
        Allow(table, WorkTrigger.Withdraw, WorkStatus.InProgress, WorkStatus.Submitted);
        Allow(table, WorkTrigger.RequestRevision, WorkStatus.InProgress, WorkStatus.Submitted);
        Allow(table, WorkTrigger.Approve, WorkStatus.Verified, WorkStatus.Submitted);
        Allow(table, WorkTrigger.Reject, WorkStatus.Rejected, WorkStatus.Submitted);
        Allow(table, WorkTrigger.Rework, WorkStatus.InProgress, WorkStatus.Rejected);
        Allow(table, WorkTrigger.Complete, WorkStatus.Done, WorkStatus.Verified);
        Allow(table, WorkTrigger.Cancel, WorkStatus.Cancelled, WorkStatus.New, WorkStatus.InProgress, WorkStatus.HandedOver, WorkStatus.Rejected);

        return table;
    }

    private static void Allow(
        Dictionary<(WorkStatus From, WorkTrigger Trigger), WorkStatus> table,
        WorkTrigger trigger,
        WorkStatus to,
        params WorkStatus[] from)
    {
        foreach (var status in from)
        {
            table[(status, trigger)] = to;
        }
    }
}
