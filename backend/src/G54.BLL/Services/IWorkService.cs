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

    /// <summary>
    /// Manually moves a work item to InProgress (assignee), or Done / Cancelled (assignee's direct manager).
    /// </summary>
    Task<WorkDetailDto> ChangeStatusAsync(
        Guid id,
        Guid userId,
        WorkStatusDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves New / InProgress / HandedOver work whose planned end date is before <paramref name="today"/>
    /// to Overdue. Returns the number of work items updated.
    /// </summary>
    Task<int> MarkOverdueAsync(
        DateOnly today,
        CancellationToken cancellationToken = default);
}
