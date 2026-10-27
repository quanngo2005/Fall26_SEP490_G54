using G54.BLL.Dtos.Handover;

namespace G54.BLL.Services;

public interface IHandoverService
{
    /// <summary>Creates a PENDING handover request for a work item the caller is assigned to.</summary>
    Task<HandoverDetailDto> CreateHandoverAsync(
        Guid workId,
        Guid userId,
        CreateHandoverRequestDto request,
        CancellationToken cancellationToken = default);
}
