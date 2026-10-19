using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Workflow;
using G54.DAL.Entities;

namespace G54.UnitTests;

public sealed class WorkStateMachineTests
{
    public static TheoryData<WorkStatus, WorkTrigger, WorkStatus> ValidTransitions => new()
    {
        { WorkStatus.New, WorkTrigger.Start, WorkStatus.InProgress },
        { WorkStatus.New, WorkTrigger.Submit, WorkStatus.Submitted },
        { WorkStatus.New, WorkTrigger.Handover, WorkStatus.HandedOver },
        { WorkStatus.New, WorkTrigger.Cancel, WorkStatus.Cancelled },
        { WorkStatus.InProgress, WorkTrigger.Submit, WorkStatus.Submitted },
        { WorkStatus.InProgress, WorkTrigger.Handover, WorkStatus.HandedOver },
        { WorkStatus.InProgress, WorkTrigger.Cancel, WorkStatus.Cancelled },
        { WorkStatus.HandedOver, WorkTrigger.AcceptHandover, WorkStatus.InProgress },
        { WorkStatus.HandedOver, WorkTrigger.Submit, WorkStatus.Submitted },
        { WorkStatus.HandedOver, WorkTrigger.Cancel, WorkStatus.Cancelled },
        { WorkStatus.Submitted, WorkTrigger.Withdraw, WorkStatus.InProgress },
        { WorkStatus.Submitted, WorkTrigger.RequestRevision, WorkStatus.InProgress },
        { WorkStatus.Submitted, WorkTrigger.Approve, WorkStatus.Verified },
        { WorkStatus.Submitted, WorkTrigger.Reject, WorkStatus.Rejected },
        { WorkStatus.Rejected, WorkTrigger.Rework, WorkStatus.InProgress },
        { WorkStatus.Rejected, WorkTrigger.Submit, WorkStatus.Submitted },
        { WorkStatus.Rejected, WorkTrigger.Handover, WorkStatus.HandedOver },
        { WorkStatus.Rejected, WorkTrigger.Cancel, WorkStatus.Cancelled },
        { WorkStatus.Verified, WorkTrigger.Complete, WorkStatus.Done },
    };

    public static TheoryData<WorkStatus, WorkTrigger> InvalidTransitions => new()
    {
        { WorkStatus.New, WorkTrigger.Approve },
        { WorkStatus.New, WorkTrigger.Complete },
        { WorkStatus.InProgress, WorkTrigger.Approve },
        { WorkStatus.InProgress, WorkTrigger.Start },
        { WorkStatus.Submitted, WorkTrigger.Submit },
        { WorkStatus.Submitted, WorkTrigger.Cancel },
        { WorkStatus.Submitted, WorkTrigger.Complete },
        { WorkStatus.Verified, WorkTrigger.Submit },
        { WorkStatus.Verified, WorkTrigger.Reject },
        { WorkStatus.Verified, WorkTrigger.Cancel },
        { WorkStatus.Done, WorkTrigger.Submit },
        { WorkStatus.Done, WorkTrigger.Cancel },
        { WorkStatus.Cancelled, WorkTrigger.Start },
        { WorkStatus.Cancelled, WorkTrigger.Submit },
    };

    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void Fire_ValidTransition_ReturnsTargetStatus(WorkStatus from, WorkTrigger trigger, WorkStatus expected)
    {
        Assert.True(WorkStateMachine.CanFire(from, trigger));
        Assert.True(WorkStateMachine.TryFire(from, trigger, out var to));
        Assert.Equal(expected, to);
        Assert.Equal(expected, WorkStateMachine.Fire(from, trigger));
    }

    [Theory]
    [MemberData(nameof(InvalidTransitions))]
    public void Fire_InvalidTransition_ThrowsConflictWithWorkInvalidTransition(WorkStatus from, WorkTrigger trigger)
    {
        Assert.False(WorkStateMachine.CanFire(from, trigger));
        Assert.False(WorkStateMachine.TryFire(from, trigger, out _));

        var ex = Assert.Throws<ConflictException>(() => WorkStateMachine.Fire(from, trigger));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(ErrorCodes.WorkInvalidTransition, ex.ErrorCode);
    }

    [Fact]
    public void Fire_HappyPath_NewToDone()
    {
        var status = WorkStatus.New;

        status = WorkStateMachine.Fire(status, WorkTrigger.Start);
        Assert.Equal(WorkStatus.InProgress, status);

        status = WorkStateMachine.Fire(status, WorkTrigger.Submit);
        Assert.Equal(WorkStatus.Submitted, status);

        status = WorkStateMachine.Fire(status, WorkTrigger.Approve);
        Assert.Equal(WorkStatus.Verified, status);

        status = WorkStateMachine.Fire(status, WorkTrigger.Complete);
        Assert.Equal(WorkStatus.Done, status);
    }

    [Theory]
    [InlineData(WorkStatus.Done)]
    [InlineData(WorkStatus.Cancelled)]
    public void GetAvailableTriggers_TerminalStatus_HasNoTriggers(WorkStatus status)
    {
        Assert.Empty(WorkStateMachine.GetAvailableTriggers(status));
    }

    [Theory]
    [InlineData(WorkStatus.New)]
    [InlineData(WorkStatus.InProgress)]
    [InlineData(WorkStatus.HandedOver)]
    [InlineData(WorkStatus.Submitted)]
    [InlineData(WorkStatus.Verified)]
    [InlineData(WorkStatus.Rejected)]
    public void GetAvailableTriggers_NonTerminalStatus_HasAtLeastOneExit(WorkStatus status)
    {
        Assert.NotEmpty(WorkStateMachine.GetAvailableTriggers(status));
    }

    [Fact]
    public void GetAvailableTriggers_Submitted_ListsVerifierAndWithdrawActions()
    {
        var triggers = WorkStateMachine.GetAvailableTriggers(WorkStatus.Submitted);

        Assert.Equal(
            new[] { WorkTrigger.Withdraw, WorkTrigger.RequestRevision, WorkTrigger.Approve, WorkTrigger.Reject },
            triggers);
    }

    private static readonly DateOnly Today = new(2026, 10, 16);

    [Theory]
    [InlineData(WorkStatus.New)]
    [InlineData(WorkStatus.InProgress)]
    [InlineData(WorkStatus.HandedOver)]
    [InlineData(WorkStatus.Rejected)]
    public void IsOverdue_UndeliveredWorkPastDeadline_IsTrue(WorkStatus status)
    {
        Assert.True(WorkStateMachine.IsOverdue(status, Today.AddDays(-1), Today));
    }

    [Theory]
    [InlineData(WorkStatus.Submitted)]
    [InlineData(WorkStatus.Verified)]
    [InlineData(WorkStatus.Done)]
    [InlineData(WorkStatus.Cancelled)]
    public void IsOverdue_DeliveredOrClosedWork_IsFalse(WorkStatus status)
    {
        Assert.False(WorkStateMachine.IsOverdue(status, Today.AddDays(-30), Today));
    }

    [Fact]
    public void IsOverdue_DeadlineTodayOrLaterOrMissing_IsFalse()
    {
        Assert.False(WorkStateMachine.IsOverdue(WorkStatus.InProgress, Today, Today));
        Assert.False(WorkStateMachine.IsOverdue(WorkStatus.InProgress, Today.AddDays(1), Today));
        Assert.False(WorkStateMachine.IsOverdue(WorkStatus.InProgress, null, Today));
    }

    [Fact]
    public void Overdue_WorkCanStillBeSubmitted()
    {
        Assert.True(WorkStateMachine.IsOverdue(WorkStatus.InProgress, Today.AddDays(-3), Today));
        Assert.True(WorkStateMachine.CanFire(WorkStatus.InProgress, WorkTrigger.Submit));
    }
}
