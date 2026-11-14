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
    /// <summary>Null when the work does not exist or lies outside the caller's <c>work:read</c> scope.</summary>
    Task<IReadOnlyList<WorkActivityDto>?> GetActivitiesAsync(
        Guid id,
        Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>The assignee ticks or unticks one checklist item of an open work.</summary>
    Task<WorkDetailDto> SetChecklistItemAsync(
        Guid id,
        Guid itemId,
        Guid userId,
        WorkChecklistItemStateDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>The assignee's manager changes the deadline, priority and checklist of an open work and may leave a note.</summary>
    Task<WorkDetailDto> UpdatePlanAsync(
        Guid id,
        Guid userId,
        UpdateWorkPlanDto dto,
        CancellationToken cancellationToken = default);

    Task<int> MarkOverdueAsync(
        DateOnly today,
        CancellationToken cancellationToken = default);
}
