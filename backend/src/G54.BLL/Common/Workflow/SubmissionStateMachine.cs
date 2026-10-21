using G54.DAL.Entities;

namespace G54.BLL.Common.Workflow;

/// <summary>
/// Single source of truth for the status transitions of an existing result submission.
///
/// <code>
/// SUBMITTED ──Withdraw──────────► WITHDRAWN
/// SUBMITTED / UNDER_REVIEW ──Approve─────────► APPROVED
/// SUBMITTED / UNDER_REVIEW ──RequestRevision─► UNDER_REVIEW
/// SUBMITTED / UNDER_REVIEW ──Reject──────────► REJECTED
/// </code>
///
/// A new submission is created as SUBMITTED, so creation is not a transition. APPROVED, REJECTED,
/// WITHDRAWN and DRAFT have no outgoing transition. The machine only answers which status an action leads to;
/// callers decide which error to raise when the action is not allowed.
/// </summary>
public static class SubmissionStateMachine
{
    private static readonly Dictionary<(SubmissionStatus From, SubmissionAction Action), SubmissionStatus> Transitions = BuildTransitions();

    /// <summary>
    /// Returns true and the resulting status when <paramref name="action"/> is allowed from <paramref name="current"/>;
    /// returns false when the current status does not allow the action.
    /// </summary>
    public static bool TryGetNextStatus(SubmissionStatus current, SubmissionAction action, out SubmissionStatus next)
        => Transitions.TryGetValue((current, action), out next);

    private static Dictionary<(SubmissionStatus From, SubmissionAction Action), SubmissionStatus> BuildTransitions()
    {
        var table = new Dictionary<(SubmissionStatus From, SubmissionAction Action), SubmissionStatus>();

        Allow(table, SubmissionAction.Withdraw, SubmissionStatus.Withdrawn, SubmissionStatus.Submitted);
        Allow(table, SubmissionAction.Approve, SubmissionStatus.Approved, SubmissionStatus.Submitted, SubmissionStatus.UnderReview);
        Allow(table, SubmissionAction.RequestRevision, SubmissionStatus.UnderReview, SubmissionStatus.Submitted, SubmissionStatus.UnderReview);
        Allow(table, SubmissionAction.Reject, SubmissionStatus.Rejected, SubmissionStatus.Submitted, SubmissionStatus.UnderReview);

        return table;
    }

    private static void Allow(
        Dictionary<(SubmissionStatus From, SubmissionAction Action), SubmissionStatus> table,
        SubmissionAction action,
        SubmissionStatus next,
        params SubmissionStatus[] from)
    {
        foreach (var status in from)
        {
            table[(status, action)] = next;
        }
    }
}
