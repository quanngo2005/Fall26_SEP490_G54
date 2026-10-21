using G54.BLL.Dtos.Work;

namespace G54.BLL.Services;

public interface IWorkService
{
    Task<WorkDetailDto> CreateWorkAsync(
        CreateWorkRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<WorkDetailDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
