using G54.BLL.Common.Models;
using G54.BLL.Dtos.JobPosition;

namespace G54.BLL.Services;

public interface IJobPositionService
{
    Task<PagedResult<JobPositionDetailDto>> GetJobPositionsAsync(
        JobPositionFilterDto filter,
        CancellationToken cancellationToken = default);

    Task<JobPositionDetailDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
