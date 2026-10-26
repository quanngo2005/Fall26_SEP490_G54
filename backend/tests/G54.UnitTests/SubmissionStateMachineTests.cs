using G54.BLL.Common.Workflow;
using G54.DAL.Entities;

namespace G54.UnitTests;

public sealed class SubmissionStateMachineTests
{
    private const SubmissionStatus Draft = SubmissionStatus.Draft;
    private const SubmissionStatus Submitted = SubmissionStatus.Submitted;
    private const SubmissionStatus UnderReview = SubmissionStatus.UnderReview;
    private const SubmissionStatus Approved = SubmissionStatus.Approved;
    private const SubmissionStatus Rejected = SubmissionStatus.Rejected;
    private const SubmissionStatus Withdrawn = SubmissionStatus.Withdrawn;

    /// <summary>Every allowed transition. Any (status, action) pair that is not listed must be refused.</summary>
    private static readonly (SubmissionStatus Current, SubmissionAction Action, SubmissionStatus Next)[] Allowed =
    [
        (Submitted, SubmissionAction.Withdraw, Withdrawn),
        (Submitted, SubmissionAction.Approve, Approved),
        (UnderReview, SubmissionAction.Approve, Approved),
        (Submitted, SubmissionAction.RequestRevision, UnderReview),
        (UnderReview, SubmissionAction.RequestRevision, UnderReview),
        (Submitted, SubmissionAction.Reject, Rejected),
        (UnderReview, SubmissionAction.Reject, Rejected),
    ];

    public static TheoryData<SubmissionStatus, SubmissionAction, SubmissionStatus> AllowedTransitions()
    {
        var data = new TheoryData<SubmissionStatus, SubmissionAction, SubmissionStatus>();
        foreach (var (current, action, next) in Allowed)
        {
            data.Add(current, action, next);
        }

        return data;
    }

    public static TheoryData<SubmissionStatus, SubmissionAction> RefusedTransitions()
    {
        var data = new TheoryData<SubmissionStatus, SubmissionAction>();
        foreach (var status in Enum.GetValues<SubmissionStatus>())
        {
            foreach (var action in Enum.GetValues<SubmissionAction>())
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
        SubmissionStatus current,
        SubmissionAction action,
        SubmissionStatus expectedNext)
    {
        var allowed = SubmissionStateMachine.TryGetNextStatus(current, action, out var next);

        Assert.True(allowed);
        Assert.Equal(expectedNext, next);
    }

    [Theory]
    [MemberData(nameof(RefusedTransitions))]
    public void TryGetNextStatus_WhenActionIsNotAllowed_ReturnsFalse(SubmissionStatus current, SubmissionAction action)
    {
        var allowed = SubmissionStateMachine.TryGetNextStatus(current, action, out _);

        Assert.False(allowed);
    }

    [Fact]
    public void TheMatrixCoversEveryStatusAndAction()
    {
        var statuses = Enum.GetValues<SubmissionStatus>().Length;
        var actions = Enum.GetValues<SubmissionAction>().Length;

        Assert.Equal(statuses * actions, Allowed.Length + RefusedTransitions().Count);
    }

    [Theory]
    [InlineData(Draft)]
    [InlineData(Approved)]
    [InlineData(Rejected)]
    [InlineData(Withdrawn)]
    public void StatusesWithoutOutgoingTransitions_RefuseEveryAction(SubmissionStatus current)
    {
        foreach (var action in Enum.GetValues<SubmissionAction>())
        {
            Assert.False(SubmissionStateMachine.TryGetNextStatus(current, action, out _));
        }
    }

    [Fact]
    public void OnlyASubmittedReportCanBeWithdrawn()
    {
        foreach (var status in Enum.GetValues<SubmissionStatus>())
        {
            var allowed = SubmissionStateMachine.TryGetNextStatus(status, SubmissionAction.Withdraw, out _);

            Assert.Equal(status == Submitted, allowed);
        }
    }

    [Fact]
    public void ARevisionRequestKeepsTheReportUnderReview_SoItCanBeVerifiedAgain()
    {
        Assert.True(SubmissionStateMachine.TryGetNextStatus(Submitted, SubmissionAction.RequestRevision, out var afterFirst));
        Assert.Equal(UnderReview, afterFirst);

        Assert.True(SubmissionStateMachine.TryGetNextStatus(afterFirst, SubmissionAction.Approve, out var afterSecond));
        Assert.Equal(Approved, afterSecond);
    }
}
