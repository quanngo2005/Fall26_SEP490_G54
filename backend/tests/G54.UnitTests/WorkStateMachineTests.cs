using G54.BLL.Common.Workflow;
using G54.DAL.Entities;

namespace G54.UnitTests;

public sealed class WorkStateMachineTests
{
    private const WorkStatus New = WorkStatus.New;
    private const WorkStatus InProgress = WorkStatus.InProgress;
    private const WorkStatus HandedOver = WorkStatus.HandedOver;
    private const WorkStatus Submitted = WorkStatus.Submitted;
    private const WorkStatus Verified = WorkStatus.Verified;
    private const WorkStatus Rejected = WorkStatus.Rejected;
    private const WorkStatus Overdue = WorkStatus.Overdue;
    private const WorkStatus Done = WorkStatus.Done;
    private const WorkStatus Cancelled = WorkStatus.Cancelled;

    /// <summary>Every allowed transition. Any (status, action) pair that is not listed must be refused.</summary>
    private static readonly (WorkStatus Current, WorkAction Action, WorkStatus Next)[] Allowed =
    [
        (New, WorkAction.Start, InProgress),
        (HandedOver, WorkAction.Start, InProgress),
        (Rejected, WorkAction.Start, InProgress),
        (New, WorkAction.Submit, Submitted),
        (InProgress, WorkAction.Submit, Submitted),
        (Rejected, WorkAction.Submit, Submitted),
        (Overdue, WorkAction.Submit, Submitted),
        (Submitted, WorkAction.Reopen, InProgress),
        (InProgress, WorkAction.Reopen, InProgress),
        (Submitted, WorkAction.Verify, Verified),
        (InProgress, WorkAction.Verify, Verified),
        (Submitted, WorkAction.Reject, Rejected),
        (InProgress, WorkAction.Reject, Rejected),
        (Verified, WorkAction.Complete, Done),
        (New, WorkAction.Cancel, Cancelled),
        (InProgress, WorkAction.Cancel, Cancelled),
        (HandedOver, WorkAction.Cancel, Cancelled),
        (Rejected, WorkAction.Cancel, Cancelled),
        (Overdue, WorkAction.Cancel, Cancelled),
        (New, WorkAction.Handover, HandedOver),
        (InProgress, WorkAction.Handover, HandedOver),
        (New, WorkAction.MarkOverdue, Overdue),
        (InProgress, WorkAction.MarkOverdue, Overdue),
        (HandedOver, WorkAction.MarkOverdue, Overdue),
    ];

    public static TheoryData<WorkStatus, WorkAction, WorkStatus> AllowedTransitions()
    {
        var data = new TheoryData<WorkStatus, WorkAction, WorkStatus>();
        foreach (var (current, action, next) in Allowed)
        {
            data.Add(current, action, next);
        }

        return data;
    }

    public static TheoryData<WorkStatus, WorkAction> RefusedTransitions()
    {
        var data = new TheoryData<WorkStatus, WorkAction>();
        foreach (var status in Enum.GetValues<WorkStatus>())
        {
            foreach (var action in Enum.GetValues<WorkAction>())
            {
                if (!Allowed.Any(a => a.Current == status && a.Action == action))
                {
                    data.Add(status, action);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public void TryGetNextStatus_WhenActionIsAllowed_ReturnsTheNextStatus(
        WorkStatus current,
        WorkAction action,
        WorkStatus expectedNext)
    {
        var allowed = WorkStateMachine.TryGetNextStatus(current, action, out var next);

        Assert.True(allowed);
        Assert.Equal(expectedNext, next);
    }

    [Theory]
    [MemberData(nameof(RefusedTransitions))]
    public void TryGetNextStatus_WhenActionIsNotAllowed_ReturnsFalse(WorkStatus current, WorkAction action)
    {
        var allowed = WorkStateMachine.TryGetNextStatus(current, action, out _);

        Assert.False(allowed);
    }

    [Fact]
    public void TheMatrixCoversEveryStatusAndAction()
    {
        var statuses = Enum.GetValues<WorkStatus>().Length;
        var actions = Enum.GetValues<WorkAction>().Length;

        Assert.Equal(statuses * actions, Allowed.Length + RefusedTransitions().Count);
    }

    [Theory]
    [InlineData(Done)]
    [InlineData(Cancelled)]
    public void StatusesWithoutOutgoingTransitions_RefuseEveryAction(WorkStatus current)
    {
        foreach (var action in Enum.GetValues<WorkAction>())
        {
            Assert.False(WorkStateMachine.TryGetNextStatus(current, action, out _));
        }

        Assert.Empty(WorkStateMachine.GetNextStatuses(current));
    }

    [Fact]
    public void OnlyAnotherRevisionRequestLeavesAStatusForItself()
    {
        // A second revision request while the work is already IN_PROGRESS is the only self-loop.
        foreach (var (current, action, next) in Allowed.Where(a => a.Current == a.Next))
        {
            Assert.Equal(InProgress, current);
            Assert.Equal(WorkAction.Reopen, action);
            Assert.Equal(InProgress, next);
        }
    }
    [Fact]
    public void OnlyVerifiedWorkCanBeCompleted()
    {
        foreach (var status in Enum.GetValues<WorkStatus>())
        {
            var allowed = WorkStateMachine.TryGetNextStatus(status, WorkAction.Complete, out _);

            Assert.Equal(status == Verified, allowed);
        }
    }

    [Fact]
    public void OnlySubmittedOrReworkedWorkCanBeVerifiedRejectedOrReopened()
    {
        foreach (var action in new[] { WorkAction.Verify, WorkAction.Reject, WorkAction.Reopen })
        {
            foreach (var status in Enum.GetValues<WorkStatus>())
            {
                var allowed = WorkStateMachine.TryGetNextStatus(status, action, out _);

                Assert.Equal(status is Submitted or InProgress, allowed);
            }
        }
    }

    [Fact]
    public void ARevisionRequest_SoTheVerifierCanDecideAgainWhileTheWorkIsInProgress()
    {
        // Verifier asks for rework: work goes back to IN_PROGRESS, its submission stays UNDER_REVIEW
        // (see SubmissionStateMachine). The verifier must still be able to approve that submission.
        Assert.True(WorkStateMachine.TryGetNextStatus(Submitted, WorkAction.Reopen, out var afterRevision));
        Assert.Equal(InProgress, afterRevision);

        Assert.True(WorkStateMachine.TryGetNextStatus(afterRevision, WorkAction.Verify, out var afterApproval));
        Assert.Equal(Verified, afterApproval);

        Assert.True(WorkStateMachine.TryGetNextStatus(afterRevision, WorkAction.Reject, out var afterRejection));
        Assert.Equal(Rejected, afterRejection);
    }

    [Fact]
    public void OverdueWorkLeavesOverdueOnlyByBeingSubmittedOrCancelled()
    {
        // Starting it again would be undone by the overdue job at the next tick (the planned end date is still past),
        // so Overdue is left only by submitting the result or cancelling the work.
        Assert.Equal(
            new[] { Cancelled, Submitted },
            WorkStateMachine.GetNextStatuses(Overdue).OrderBy(status => status.ToString()).ToArray());
        Assert.False(WorkStateMachine.TryGetNextStatus(Overdue, WorkAction.Start, out _));
    }

    [Fact]
    public void OverdueWorkCanStillBeSubmitted()
    {
        Assert.True(WorkStateMachine.TryGetNextStatus(Overdue, WorkAction.Submit, out var next));
        Assert.Equal(Submitted, next);
    }

    [Fact]
    public void GetNextStatuses_ListsEveryReachableStatusOnce()
    {
        var fromNew = WorkStateMachine.GetNextStatuses(New);

        Assert.Equal(
            new[] { Cancelled, HandedOver, InProgress, Overdue, Submitted },
            fromNew.OrderBy(status => status.ToString()).ToArray());
    }
}
