namespace G54.BLL.Common.Workflow;

/// <summary>
/// Business events that can move a Work between lifecycle states. See <see cref="WorkStateMachine"/>.
/// </summary>
public enum WorkTrigger
{
    /// <summary>Assignee starts working on a NEW work.</summary>
    Start,

    /// <summary>Work is handed over to another employee.</summary>
    Handover,

    /// <summary>Receiver accepts a handed over work and continues it.</summary>
    AcceptHandover,

    /// <summary>Assignee submits a result report for verification.</summary>
    Submit,

    /// <summary>Submitter withdraws a submitted report before it is verified.</summary>
    Withdraw,

    /// <summary>Verifier asks for more information / rework on a submitted report.</summary>
    RequestRevision,

    /// <summary>Verifier approves the submitted result.</summary>
    Approve,

    /// <summary>Verifier rejects the submitted result.</summary>
    Reject,

    /// <summary>Assignee resumes a rejected work.</summary>
    Rework,

    /// <summary>A verified work is closed.</summary>
    Complete,

    /// <summary>Work is cancelled before it is verified.</summary>
    Cancel,
}
