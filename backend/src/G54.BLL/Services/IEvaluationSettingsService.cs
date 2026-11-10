using G54.BLL.Dtos.SystemParameter;

namespace G54.BLL.Services;

/// <summary>Evaluation settings of SCR-ADM-04: the comparison baseline and the tier criteria matrix.</summary>
public interface IEvaluationSettingsService
{
    Task<EvaluationComparisonDto> GetComparisonAsync(CancellationToken ct = default);

    Task<EvaluationComparisonDto> UpdateComparisonAsync(EvaluationComparisonDto request, CancellationToken ct = default);

    Task<TierCriteriaMatrixDto> GetTierCriteriaAsync(CancellationToken ct = default);

    Task<TierCriteriaMatrixDto> UpdateTierCriteriaAsync(UpdateTierCriteriaDto request, CancellationToken ct = default);
}
