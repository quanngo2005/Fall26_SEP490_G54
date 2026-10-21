using G54.BLL.Dtos.Performance;

namespace G54.BLL.Services;

public interface IPerformanceEvaluationService
{
    Task<PerformanceEvaluationDto> CalculateAsync(
        Guid accountId,
        Guid evaluationId,
        CancellationToken cancellationToken = default);
}
