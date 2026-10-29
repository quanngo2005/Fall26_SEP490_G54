using G54.BLL.Common.Models;
using G54.BLL.Dtos.JobPosition;

namespace G54.BLL.Services;

public interface IJobPositionService
{
    Task<PagedResult<JobPositionSummaryDto>> GetJobPositionsAsync(
        JobPositionFilterDto filter,
        CancellationToken cancellationToken = default);

    Task<JobPositionDetailDto?> GetJobPositionByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<JobPositionDetailDto> AssignWorkTypeAsync(
        Guid id,
        AssignJobPositionWorkTypeDto request,
        CancellationToken cancellationToken = default);

    Task<JobPositionDetailDto> RemoveWorkTypeAsync(
        Guid id,
        Guid workTypeId,
        CancellationToken cancellationToken = default);
}

