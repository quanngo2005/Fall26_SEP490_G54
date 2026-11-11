using G54.BLL.Dtos.Handover;

namespace G54.BLL.Services;

public interface IHandoverService
{
    /// <summary>Active colleagues in the caller's department (excluding the caller) who can receive a handover.</summary>
    Task<IReadOnlyList<HandoverRecipientDto>> GetRecipientsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Handovers in the pool (<c>IN_POOL</c>) waiting for the caller (direct manager of the requester) to assign a recipient.</summary>
    Task<IReadOnlyList<HandoverDetailDto>> GetPoolAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a handover for a work item:
    /// <list type="bullet">
    /// <item>Same role (<c>A -&gt; A1</c>): creates a <c>PENDING</c> peer swap request and notifies the requester's manager and <c>A1</c>.</item>
    /// <item>Different role (<c>A -&gt; B</c>): requires <c>Work.ProgressPercent</c> at or above the configured threshold, then either assigns a random <c>B</c> immediately (<c>RANDOM</c>) or places the handover in the pool (<c>IN_POOL</c>, <c>ASSIGNED</c>) for the manager to assign.</item>
    /// </list>
    /// </summary>
    Task<HandoverDetailDto> CreateHandoverAsync(
        Guid workId,
        Guid userId,
        CreateHandoverRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>The recipient accepts a pending peer handover (<c>A1</c>): the work moves to them and becomes HANDED_OVER.</summary>
    Task<HandoverDetailDto> AcceptHandoverAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The recipient rejects a pending peer handover (<c>A1</c>); the work stays with the requester (<c>A</c>).</summary>
    Task<HandoverDetailDto> RejectHandoverAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The requester's direct manager approves a pending peer handover: the work moves to the recipient at once.</summary>
    Task<HandoverDetailDto> ApproveByManagerAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The requester's direct manager changes/assigns the recipient of a pending or pooled handover and moves the work at once.</summary>
    Task<HandoverDetailDto> ReassignByManagerAsync(
        Guid handoverId,
        Guid userId,
        ReassignHandoverRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>The requester's direct manager rejects a pending or pooled handover; the work stays with the requester.</summary>
    Task<HandoverDetailDto> RejectByManagerAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default);
}
