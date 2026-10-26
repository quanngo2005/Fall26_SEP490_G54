using G54.BLL.Common.Models;
using G54.BLL.Dtos.Work;

namespace G54.BLL.Services;

public interface IWorkService
{
    Task<WorkDetailDto> CreateWorkAsync(
        CreateWorkRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Filtered, paged work list limited to what the caller's <c>work:read</c> scope allows
    /// (Own: their own works; Dept/Branch: works of employees in their department subtree / branch; All: everything).
    /// </summary>
    Task<PagedResult<WorkListItemDto>> GetWorksAsync(
        Guid accountId,
        WorkFilterDto filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Null when the work is unknown or outside the caller's <c>work:read</c> scope (assignee, assignee's direct manager,
    /// or the department scope over the assignee's department).
    /// </summary>
    /// <exception cref="G54.BLL.Common.Exceptions.ForbiddenException">The caller holds no work:read scope.</exception>
    Task<WorkDetailDto?> GetByIdAsync(
        Guid id,
        Guid accountId,
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
