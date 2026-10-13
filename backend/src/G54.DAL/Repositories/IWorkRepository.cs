using G54.DAL.Common;
using G54.DAL.Entities;

namespace G54.DAL.Repositories;

public interface IWorkRepository : IRepository<Work>
{
    Task<Work?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Work>> GetByAssigneeAsync(
        Guid employeeId,
        WorkStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<PagedResult<Work>> GetKanbanBoardAsync(
        Guid? assigneeEmployeeId,
        Guid? campaignId,
        WorkStatus? status,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);
}
