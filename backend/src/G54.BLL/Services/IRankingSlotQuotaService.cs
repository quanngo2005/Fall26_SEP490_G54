using G54.BLL.Dtos.SystemParameter;

namespace G54.BLL.Services;

public interface IRankingSlotQuotaService
{
    Task<RankingSlotQuotasDto> GetAsync(Guid departmentId, CancellationToken ct = default);

    Task<RankingSlotQuotasDto> UpdateAsync(UpdateRankingSlotQuotasDto request, CancellationToken ct = default);
}
