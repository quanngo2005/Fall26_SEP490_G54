using G54.BLL.Dtos.Handover;

namespace G54.BLL.Services;

public interface IHandoverService
{
    /// <summary>Active colleagues in the caller's department (excluding the caller) who can receive a handover.</summary>
    Task<IReadOnlyList<HandoverRecipientDto>> GetRecipientsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hands a work over: the work moves to the recipient at once and becomes HANDED_OVER. The caller is the assignee
    /// or the assignee's direct manager.
    /// </summary>
    Task<HandoverDetailDto> CreateHandoverAsync(
        Guid workId,
        Guid userId,
        CreateHandoverRequestDto request,
        CancellationToken cancellationToken = default);
}
