using G54.DAL.Common;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL.Repositories;

public sealed class WorkRepository(AppDbContext dbContext) : Repository<Work>(dbContext), IWorkRepository
{
    public async Task<Work?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .AsNoTracking()
            .Include(w => w.AssigneeEmployee)
            .Include(w => w.WorkType)
            .Include(w => w.BusinessCampaign)
            .Include(w => w.ResultSubmissions)
            .Include(w => w.TaskHandovers)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Work>> GetByAssigneeAsync(
        Guid employeeId,
        WorkStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.AsNoTracking().Where(w => w.AssigneeEmployeeId == employeeId);
        if (status.HasValue)
        {
            query = query.Where(w => w.Status == status.Value);
        }

        return await query
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<Work>> GetKanbanBoardAsync(
        Guid? assigneeEmployeeId,
        Guid? campaignId,
        WorkStatus? status,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePageIndex = pageIndex <= 0 ? 1 : pageIndex;
        var safePageSize = pageSize <= 0 ? 20 : pageSize;

        var query = DbSet
            .AsNoTracking()
            .Include(w => w.AssigneeEmployee)
            .Include(w => w.WorkType)
            .AsQueryable();

        if (assigneeEmployeeId.HasValue)
        {
            query = query.Where(w => w.AssigneeEmployeeId == assigneeEmployeeId.Value);
        }

        if (campaignId.HasValue)
        {
            query = query.Where(w => w.BusinessCampaignId == campaignId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(w => w.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(w => w.Priority)
            .ThenBy(w => w.PlannedEndDate)
            .Skip((safePageIndex - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Work>
        {
            Items = items,
            PageIndex = safePageIndex,
            PageSize = safePageSize,
            TotalCount = totalCount,
        };
    }

    public async Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await DbSet.AnyAsync(w => w.Code == code, cancellationToken);
    }
}
