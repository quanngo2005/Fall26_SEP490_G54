namespace G54.BLL.Common.Workflow;

/// <summary>
/// Actions that can change the status of an existing result submission. See <see cref="SubmissionStateMachine"/>.
/// </summary>
public enum SubmissionAction
{
    /// <summary>The submitter takes back a report that has not been verified yet.</summary>
    Withdraw,

    /// <summary>The verifier approves the submitted result.</summary>
    Approve,

    /// <summary>The verifier asks the submitter to rework or add information.</summary>
    RequestRevision,

    /// <summary>The verifier rejects the submitted result.</summary>
    Reject,
}
