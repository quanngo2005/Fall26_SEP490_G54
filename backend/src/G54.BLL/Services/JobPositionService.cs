using G54.BLL.Common.Models;
using G54.BLL.Dtos.JobPosition;
using G54.DAL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed class JobPositionService(AppDbContext db, ILogger<JobPositionService> logger) : IJobPositionService
{
    private static readonly Action<ILogger, int, int, Exception?> LogJobPositionsListed =
        LoggerMessage.Define<int, int>(LogLevel.Information, new EventId(1, "JobPositionsListed"), "Listed job positions: Page={Page}, Count={Count}");

    public async Task<PagedResult<JobPositionDetailDto>> GetJobPositionsAsync(
        JobPositionFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var query = db.JobPositions
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim();
            query = query.Where(j =>
                j.Code.Contains(keyword) ||
                j.Title.Contains(keyword));
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(j => j.Status == filter.Status.Value);
        }

        if (filter.EvaluationProfileId.HasValue)
        {
            query = query.Where(j => j.EvaluationProfileId == filter.EvaluationProfileId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(j => j.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new JobPositionDetailDto(
                j.Id,
                j.Code,
                j.Title,
                j.Description,
                j.EvaluationProfileId,
                j.EvaluationProfile != null ? j.EvaluationProfile.Code : null,
                j.EvaluationProfile != null ? j.EvaluationProfile.Name : null,
                j.Status.ToString().ToUpperInvariant(),
                j.Employees.Count,
                j.LastSyncAt,
                j.UpdatedAt))
            .ToListAsync(cancellationToken);

        LogJobPositionsListed(logger, page, items.Count, null);

        return new PagedResult<JobPositionDetailDto>(items, totalCount, page, pageSize);
    }

    public async Task<JobPositionDetailDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await db.JobPositions
            .AsNoTracking()
            .Where(j => j.Id == id)
            .Select(j => new JobPositionDetailDto(
                j.Id,
                j.Code,
                j.Title,
                j.Description,
                j.EvaluationProfileId,
                j.EvaluationProfile != null ? j.EvaluationProfile.Code : null,
                j.EvaluationProfile != null ? j.EvaluationProfile.Name : null,
                j.Status.ToString().ToUpperInvariant(),
                j.Employees.Count,
                j.LastSyncAt,
                j.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
