using G54.BLL.Dtos.RiskEvent;
using Microsoft.AspNetCore.Http;

namespace G54.BLL.Services;

/// <summary>The Risk Events screens (SCR-RSK-01): list, details drawer, attachments and reversal. Contract: docs/api/RISK_EVENT_CONTRACT.md §9.</summary>
public interface IRiskEventScreenService
{
    /// <summary>Events in the caller's <c>risk_event:read</c> scope, newest violation first, with the count of each state.</summary>
    Task<RiskEventListDto> ListAsync(Guid userId, RiskEventFilterDto filter, CancellationToken cancellationToken = default);

    Task<RiskEventDetailsDto> GetDetailsAsync(Guid userId, Guid riskEventId, CancellationToken cancellationToken = default);

    /// <summary>Attaches evidence files to an event that has not been reversed; returns every attachment of the event.</summary>
    Task<List<RiskEventAttachmentDto>> AddEvidenceAsync(Guid userId, Guid riskEventId, IReadOnlyCollection<IFormFile> files, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverses a recorded (<c>CONFIRMED</c> / <c>RESOLVED</c>) event: it becomes <c>DISMISSED</c>, so its penalty is no longer counted,
    /// and keeps who reversed it, when and why. The original contribution row is kept.
    /// </summary>
    Task<RiskEventDetailsDto> ReverseAsync(Guid userId, Guid riskEventId, ReverseRiskEventDto request, CancellationToken cancellationToken = default);
}
