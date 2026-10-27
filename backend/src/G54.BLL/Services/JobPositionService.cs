using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.JobPosition;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// Read-only view of job positions (SCR-ORG-02). Every figure and label is read from the database; nothing is
/// synthesised. Contract: docs/api/JOB_POSITION_CONTRACT.md.
/// </summary>
public sealed class JobPositionService(AppDbContext dbContext, TimeProvider time) : IJobPositionService
{
    /// <summary>Position used only to bootstrap a local development admin; it is not a business position.</summary>
    internal const string DevelopmentBootstrapCode = "DEV_ADMIN";

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1862:Use the 'StringComparison' method overloads to perform case-insensitive string comparisons",
        Justification = "EF Core must translate this predicate to SQL; Npgsql cannot translate StringComparison overloads.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version", Justification = "Translated to PostgreSQL lower(), using database collation rather than CLR culture.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "Npgsql translates only the parameterless ToLower() to SQL lower().")]
    public async Task<PagedResult<JobPositionSummaryDto>> GetJobPositionsAsync(
        JobPositionFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var status = EnumApiFormat.ParseEnum<RecordStatus>(filter.Status, "status");
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var today = time.GetBusinessToday();

        var query = dbContext.JobPositions
            .AsNoTracking()
            .Where(position => position.Code != DevelopmentBootstrapCode);

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim().ToLowerInvariant();
            query = query.Where(position =>
                position.Code.ToLower().Contains(keyword) || position.Title.ToLower().Contains(keyword));
        }

        if (status.HasValue)
        {
            query = query.Where(position => position.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(position => position.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(position => new
            {
                position.Id,
                position.Code,
                position.Title,
                position.Description,
                position.LastSyncAt,
                position.Status,
                EmployeeCount = position.Employees.Count(e => e.Status == EmployeeStatus.Active),
                ActiveProfile = position.EvaluationProfiles
                    .Where(p => p.Status == EvaluationProfileStatus.Active
                        && p.EffectiveFrom <= today
                        && (p.EffectiveTo == null || p.EffectiveTo >= today))
                    .OrderByDescending(p => p.EffectiveFrom)
                    .ThenByDescending(p => p.Version)
                    .Select(p => new { p.Id, p.Name })
                    .FirstOrDefault(),
                WorkTypeCount = position.JobPositionWorkTypes.Count(link => link.WorkType.Status == RecordStatus.Active),
                KpiNames = position.JobPositionWorkTypes
                    .Where(link => link.WorkType.Status == RecordStatus.Active)
                    .SelectMany(link => link.WorkType.WorkTypeKpis)
                    .Where(kpi => kpi.EffectiveFrom <= today && (kpi.EffectiveTo == null || kpi.EffectiveTo >= today))
                    .Select(kpi => kpi.KpiDefinition.Name)
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new JobPositionSummaryDto(
                row.Id,
                row.Code,
                row.Title,
                row.Description,
                row.EmployeeCount,
                row.ActiveProfile?.Id,
                row.ActiveProfile?.Name,
                row.WorkTypeCount,
                row.KpiNames,
                row.LastSyncAt,
                row.Status.ToEnumString()))
            .ToList();

        return new PagedResult<JobPositionSummaryDto>(items, totalCount, page, pageSize);
    }

    public async Task<JobPositionDetailDto?> GetJobPositionByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var today = time.GetBusinessToday();

        var position = await dbContext.JobPositions
            .AsNoTracking()
            .AsSplitQuery()
            .Where(candidate => candidate.Id == id && candidate.Code != DevelopmentBootstrapCode)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Code,
                candidate.Title,
                candidate.Description,
                candidate.LastSyncAt,
                candidate.Status,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (position is null)
        {
            return null;
        }

        var employeeCount = await dbContext.Employees
            .AsNoTracking()
            .CountAsync(e => e.JobPositionId == id && e.Status == EmployeeStatus.Active, cancellationToken);

        var profile = await LoadActiveProfileAsync(id, today, cancellationToken);
        var workTypes = await LoadWorkTypesAsync(id, today, cancellationToken);

        return new JobPositionDetailDto(
            position.Id,
            position.Code,
            position.Title,
            position.Description,
            employeeCount,
            position.LastSyncAt,
            position.Status.ToEnumString(),
            profile,
            workTypes);
    }

    private async Task<JobPositionEvaluationProfileDto?> LoadActiveProfileAsync(
        Guid positionId,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var profile = await dbContext.EvaluationProfiles
            .AsNoTracking()
            .Where(p => p.JobPositionId == positionId
                && p.Status == EvaluationProfileStatus.Active
                && p.EffectiveFrom <= today
                && (p.EffectiveTo == null || p.EffectiveTo >= today))
            .OrderByDescending(p => p.EffectiveFrom)
            .ThenByDescending(p => p.Version)
            .Select(p => new
            {
                p.Id,
                p.Code,
                p.Name,
                p.Description,
                p.Version,
                p.EffectiveFrom,
                p.Status,
                Items = p.EvaluationProfileItems
                    .OrderBy(item => item.DisplayOrder)
                    .Select(item => new
                    {
                        item.Id,
                        GroupName = item.KpiGroup.Name,
                        GroupCode = item.KpiGroup.Code,
                        item.WeightPercent,
                        item.DisplayOrder,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return null;
        }

        var items = profile.Items
            .Select(item => new JobPositionEvaluationProfileItemDto(
                item.Id, item.GroupName, item.GroupCode, item.WeightPercent, item.DisplayOrder))
            .ToList();

        return new JobPositionEvaluationProfileDto(
            profile.Id,
            profile.Code,
            profile.Name,
            profile.Description,
            profile.Version,
            profile.EffectiveFrom,
            profile.Status.ToEnumString(),
            items.Sum(item => item.WeightPercent),
            items);
    }

    private async Task<IReadOnlyList<JobPositionWorkTypeDto>> LoadWorkTypesAsync(
        Guid positionId,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var workTypes = await dbContext.JobPositionWorkTypes
            .AsNoTracking()
            .Where(link => link.JobPositionId == positionId && link.WorkType.Status == RecordStatus.Active)
            .OrderBy(link => link.WorkType.Code)
            .Select(link => new
            {
                link.WorkType.Id,
                link.WorkType.Code,
                link.WorkType.Name,
                link.WorkType.Description,
                Kpis = link.WorkType.WorkTypeKpis
                    .Where(kpi => kpi.EffectiveFrom <= today && (kpi.EffectiveTo == null || kpi.EffectiveTo >= today))
                    .OrderBy(kpi => kpi.KpiDefinition.Code)
                    .Select(kpi => new
                    {
                        kpi.KpiDefinition.Id,
                        kpi.KpiDefinition.Code,
                        kpi.KpiDefinition.Name,
                        kpi.IsRequired,
                        kpi.KpiDefinition.Unit,
                    })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return workTypes
            .Select(workType => new JobPositionWorkTypeDto(
                workType.Id,
                workType.Code,
                workType.Name,
                workType.Description,
                workType.Kpis.Count,
                workType.Kpis
                    .Select(kpi => new JobPositionKpiDto(kpi.Id, kpi.Code, kpi.Name, kpi.IsRequired, kpi.Unit))
                    .ToList()))
            .ToList();
    }
}
