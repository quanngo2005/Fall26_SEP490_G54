using G54.BLL.Dtos.EvaluationPeriod;

namespace G54.BLL.Services;

public interface IEvaluationPeriodService
{
    /// <summary>Creates a period that starts <c>OPEN</c>.</summary>
    Task<EvaluationPeriodStatusDto> CreateAsync(CreateEvaluationPeriodRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>OPEN</c> → <c>IN_REVIEW</c>. Returns the pre-close check: result submissions still in review (carried to the
    /// next period) and appeals still open (they must be settled before the period is locked, BR-31).
    /// </summary>
    Task<ClosePeriodResultDto> CloseAsync(Guid periodId, CancellationToken cancellationToken = default);
}
