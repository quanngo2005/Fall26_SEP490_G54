using G54.BLL.Dtos.RiskEvent;

namespace G54.BLL.Services;

public interface IRiskEventService
{
    /// <summary>Records a new <c>OPEN</c> risk event against an employee.</summary>
    Task<RiskEventDto> CreateAsync(CreateRiskEventRequestDto request, Guid userId, CancellationToken cancellationToken = default);

    Task<RiskEventDto> GetByIdAsync(Guid riskEventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms an <c>OPEN</c> risk event and writes its negative <c>performance_contribution</c> in one save.
    /// </summary>
    Task<RiskEventConfirmationDto> ConfirmAsync(Guid riskEventId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary><c>OPEN</c> → <c>DISMISSED</c>: the event was not valid, so it no longer counts as a penalty.</summary>
    Task<RiskEventDto> DismissAsync(Guid riskEventId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary><c>CONFIRMED</c> → <c>RESOLVED</c>: the confirmed event is closed; its contribution stays.</summary>
    Task<RiskEventDto> ResolveAsync(Guid riskEventId, Guid userId, CancellationToken cancellationToken = default);
}
