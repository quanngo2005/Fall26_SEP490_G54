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

    private const string PositionResourceName = "Vị trí công việc";
    private const string WorkTypeAlreadyAssignedMessage = "Loại công việc đã được gán cho vị trí công việc này.";
    private const string WorkTypeNotAssignedMessage = "Loại công việc chưa được gán cho vị trí công việc này.";

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

    public async Task<IReadOnlyList<JobPositionWorkTypeDto>> GetWorkTypesAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await EnsurePositionExistsAsync(id, cancellationToken);
        return await LoadWorkTypesAsync(id, time.GetBusinessToday(), cancellationToken);
    }

    public async Task<JobPositionDetailDto> AssignWorkTypeAsync(
        Guid id,
        AssignJobPositionWorkTypeDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.WorkTypeId == Guid.Empty)
        {
            throw new ValidationException("Loại công việc là bắt buộc.");
        }

        await EnsurePositionExistsAsync(id, cancellationToken);

        var workTypeIsActive = await dbContext.WorkTypes
            .AsNoTracking()
            .AnyAsync(workType => workType.Id == request.WorkTypeId && workType.Status == RecordStatus.Active, cancellationToken);
        if (!workTypeIsActive)
        {
            throw new ValidationException("Loại công việc không tồn tại hoặc không ở trạng thái hoạt động.");
        }

        var alreadyLinked = await dbContext.JobPositionWorkTypes
            .AsNoTracking()
            .AnyAsync(link => link.JobPositionId == id && link.WorkTypeId == request.WorkTypeId, cancellationToken);
        if (alreadyLinked)
        {
            throw new ConflictException(WorkTypeAlreadyAssignedMessage);
        }

        dbContext.JobPositionWorkTypes.Add(new JobPositionWorkType
        {
            Id = Guid.NewGuid(),
            JobPositionId = id,
            WorkTypeId = request.WorkTypeId,
            UpdatedAt = time.GetUtcNow(),
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PostgresConflict.IsUniqueViolation(ex))
        {
            throw new ConflictException(WorkTypeAlreadyAssignedMessage);
        }

        return await GetRequiredJobPositionAsync(id, cancellationToken);
    }

    public async Task<JobPositionDetailDto> RemoveWorkTypeAsync(
        Guid id,
        Guid workTypeId,
        CancellationToken cancellationToken = default)
    {
        await EnsurePositionExistsAsync(id, cancellationToken);

        var link = await dbContext.JobPositionWorkTypes
            .SingleOrDefaultAsync(candidate => candidate.JobPositionId == id && candidate.WorkTypeId == workTypeId, cancellationToken)
            ?? throw new NotFoundException(WorkTypeNotAssignedMessage);

        dbContext.JobPositionWorkTypes.Remove(link);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The link was removed by another request between the read and the delete.
            throw new NotFoundException(WorkTypeNotAssignedMessage);
        }

        return await GetRequiredJobPositionAsync(id, cancellationToken);
    }

    private async Task EnsurePositionExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        var exists = await dbContext.JobPositions
            .AsNoTracking()
            .AnyAsync(position => position.Id == id && position.Code != DevelopmentBootstrapCode, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException(PositionResourceName, id);
        }
    }

    private async Task<JobPositionDetailDto> GetRequiredJobPositionAsync(Guid id, CancellationToken cancellationToken) =>
        await GetJobPositionByIdAsync(id, cancellationToken) ?? throw new NotFoundException(PositionResourceName, id);

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
                // The profile is a weight tree: only its root groups are listed. A group node has
                // no code of its own, so GroupCode stays empty until this view shows the tree.
                Items = p.EvaluationProfileItems
                    .Where(item => item.ParentItemId == null)
                    .OrderBy(item => item.DisplayOrder)
                    .Select(item => new
                    {
                        item.Id,
                        GroupName = item.Name,
                        GroupCode = string.Empty,
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
                item.Id, item.GroupName ?? string.Empty, item.GroupCode, item.WeightPercent, item.DisplayOrder))
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
