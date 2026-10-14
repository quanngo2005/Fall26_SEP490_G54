using G54.BLL.Common.Models;
using G54.BLL.Dtos.KpiDefinition;

namespace G54.BLL.Services;

public interface IKpiDefinitionService
{
    Task<PagedResult<KpiDefinitionDto>> GetKpisAsync(KpiFilterDto filter, CancellationToken cancellationToken = default);
    Task<KpiDefinitionDto?> GetKpiByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<KpiGroupOptionDto>> GetGroupOptionsAsync(CancellationToken cancellationToken = default);
    Task<KpiDefinitionDto> CreateKpiAsync(KpiDefinitionSaveDto dto, CancellationToken cancellationToken = default);
    Task<KpiDefinitionDto> UpdateKpiAsync(Guid id, KpiDefinitionSaveDto dto, CancellationToken cancellationToken = default);
    Task ChangeStatusAsync(Guid id, KpiStatusDto dto, CancellationToken cancellationToken = default);
}
