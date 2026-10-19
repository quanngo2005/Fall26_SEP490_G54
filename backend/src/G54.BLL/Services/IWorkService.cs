using G54.BLL.Dtos.Work;

namespace G54.BLL.Services;

public interface IWorkService
{
    Task<WorkDetailDto> CreateWorkAsync(
        CreateWorkRequestDto request,
        CancellationToken cancellationToken = default);

    Task<WorkDetailDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<WorkDetailDto> TransitionWorkAsync(
        Guid id,
        TransitionWorkRequestDto request,
        CancellationToken cancellationToken = default);
}
