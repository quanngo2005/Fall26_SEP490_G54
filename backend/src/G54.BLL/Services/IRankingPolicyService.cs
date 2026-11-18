using G54.BLL.Dtos.SystemParameter;
using G54.DAL.Entities;

namespace G54.BLL.Services;

public interface IRankingPolicyService
{
    Task<RankingPolicyDto> GetPolicyAsync(Guid? periodId = null, CancellationToken ct = default);

    Task<RankingPolicyDto> UpdatePolicyAsync(UpdateRankingPolicyDto request, CancellationToken ct = default);

    Task<SimulateRankingPolicyResultDto> SimulateAsync(SimulateRankingPolicyRequestDto request, CancellationToken ct = default);

    Task<RankingPolicyDefinitionDto> LoadEffectiveDefinitionAsync(EvaluationPeriod? period, CancellationToken ct = default);

    Task<string> BuildSnapshotJsonAsync(EvaluationPeriod period, CancellationToken ct = default);
}
