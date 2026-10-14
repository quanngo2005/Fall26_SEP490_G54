using G54.BLL.Common.Models;
using G54.BLL.Dtos.WorkType;

namespace G54.BLL.Services;

public interface IWorkTypeService
{
    Task<List<KpiOptionDto>> GetActiveKpisAsync(CancellationToken cancellationToken = default);
    Task<List<WorkflowOptionDto>> GetWorkflowsAsync(CancellationToken cancellationToken = default);
    Task<WorkTypeDetailDto> CreateWorkTypeAsync(CreateWorkTypeDto dto, CancellationToken cancellationToken = default);
    Task<PagedResult<WorkTypeDetailDto>> GetWorkTypesAsync(WorkTypeFilterDto filter, CancellationToken cancellationToken = default);
    Task<WorkTypeDetailDto?> GetWorkTypeByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkTypeDetailDto> UpdateWorkTypeAsync(Guid id, UpdateWorkTypeDto dto, CancellationToken cancellationToken = default);
    Task ChangeStatusAsync(Guid id, WorkTypeStatusDto dto, CancellationToken cancellationToken = default);
    Task DeleteWorkTypeAsync(Guid id, CancellationToken cancellationToken = default);
}
