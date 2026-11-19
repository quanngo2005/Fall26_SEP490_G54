using G54.BLL.Common.Models;
using G54.BLL.Dtos.Handover;

namespace G54.BLL.Services;

public interface IHandoverService
{
    /// <summary>Lists handover records visible to the caller (SCR-WRK-05).</summary>
    Task<PagedResult<HandoverListItemDto>> ListHandoversAsync(
        Guid userId,
        HandoverListQueryDto query,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a single handover record with work and KPI reconciliation context (SCR-WRK-05-D1).</summary>
    Task<HandoverListItemDto> GetHandoverAsync(
        Guid handoverId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Active colleagues in the caller's department (excluding the caller) who can receive a handover.</summary>
    Task<IReadOnlyList<HandoverRecipientDto>> GetRecipientsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Creates a PENDING handover request for a work item the caller is assigned to.</summary>
    Task<HandoverDetailDto> CreateHandoverAsync(
        Guid workId,
        Guid userId,
        CreateHandoverRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>The recipient accepts a pending handover: the work moves to them and becomes HANDED_OVER.</summary>
    Task<HandoverDetailDto> AcceptHandoverAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The recipient rejects a pending handover; the work stays with the requester.</summary>
    Task<HandoverDetailDto> RejectHandoverAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The requester's direct manager approves a pending handover: the work moves to the recipient at once.</summary>
    Task<HandoverDetailDto> ApproveByManagerAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The requester's direct manager changes the recipient of a pending handover and moves the work at once.</summary>
    Task<HandoverDetailDto> ReassignByManagerAsync(
        Guid handoverId,
        Guid userId,
        ReassignHandoverRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>The requester's direct manager rejects a pending handover; the work stays with the requester.</summary>
    Task<HandoverDetailDto> RejectByManagerAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default);
}
