namespace G54.BLL.Common.Workflow;

/// <summary>
/// Actions that can change the status of a work item. See <see cref="WorkStateMachine"/>.
/// </summary>
public enum WorkAction
{
    /// <summary>The assignee starts the work, or resumes work that was sent back or is overdue.</summary>
    Start,

    /// <summary>The assignee submits a result for verification.</summary>
    Submit,

    /// <summary>The assignee takes back a submitted result, or the verifier asks for more work.</summary>
    Reopen,

    /// <summary>The verifier approves the submitted result.</summary>
    Verify,

    /// <summary>The verifier rejects the submitted result.</summary>
    Reject,

    /// <summary>The assignee's direct manager closes verified work.</summary>
    Complete,

    /// <summary>The assignee's direct manager cancels the work.</summary>
    Cancel,

    /// <summary>The work is handed over to another employee.</summary>
    Handover,

    /// <summary>The planned end date has passed (raised by the overdue job).</summary>
    MarkOverdue,
}
