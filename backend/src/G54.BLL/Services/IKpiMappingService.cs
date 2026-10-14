using G54.BLL.Dtos.KpiDefinition;

namespace G54.BLL.Services;

public interface IKpiMappingService
{
    Task<List<ObjectiveKpiDto>> GetObjectiveKpisAsync(Guid objectiveId, CancellationToken cancellationToken = default);
    Task<ObjectiveKpiDto> AddObjectiveKpiAsync(Guid objectiveId, ObjectiveKpiSaveDto dto, CancellationToken cancellationToken = default);
    Task RemoveObjectiveKpiAsync(Guid objectiveId, Guid kpiId, CancellationToken cancellationToken = default);
    Task<List<KpiOptionDto>> GetAvailableKpisAsync(CancellationToken cancellationToken = default);
}
