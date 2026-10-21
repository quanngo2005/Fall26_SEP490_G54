using System.Linq.Expressions;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Department;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class DepartmentService(
    AppDbContext db,
    IPermissionService permissions,
    TimeProvider time) : IDepartmentService
{
    private const int DepartmentScopeLevel = 2;
    private const int BranchScopeLevel = 3;
    private const int AllScopeLevel = 4;

    private static readonly Expression<Func<Work, bool>> IsOpenWork = w =>
        w.Status == WorkStatus.New
        || w.Status == WorkStatus.InProgress
        || w.Status == WorkStatus.HandedOver
        || w.Status == WorkStatus.Submitted
        || w.Status == WorkStatus.Rejected;

    public async Task<PagedResult<DepartmentSummaryDto>> GetDepartmentsAsync(
        Guid accountId,
        DepartmentFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var status = ParseEnum<RecordStatus>(filter.Status, "status");
        var level = ParseEnum<DepartmentLevelType>(filter.LevelType, "levelType");

        var org = await LoadOrgAsync(cancellationToken);
        var visible = await ResolveVisibleAsync(accountId, org, cancellationToken);
        var counts = await CountEmployeesByDepartmentAsync(cancellationToken);

        IEnumerable<DeptNode> query = org.Nodes.Values;
        if (visible is not null)
        {
            query = query.Where(n => visible.Contains(n.Id));
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim();
            query = query.Where(n =>
                n.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || n.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        if (status.HasValue)
        {
            query = query.Where(n => n.Status == status.Value);
        }

        if (level.HasValue)
        {
            query = query.Where(n => n.Level == level.Value);
        }

        if (filter.ParentDepartmentId.HasValue)
        {
            query = query.Where(n => n.ParentId == filter.ParentDepartmentId.Value);
        }

        var ordered = query.OrderBy(n => n.Code, StringComparer.Ordinal).ToList();
        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new DepartmentSummaryDto(
                n.Id,
                n.Code,
                n.Name,
                n.Level.ToEnumString(),
                n.Status.ToEnumString(),
                n.ParentId,
                n.ParentId.HasValue && org.Nodes.TryGetValue(n.ParentId.Value, out var parent) ? parent.Name : null,
                org.Subtree(n.Id).Sum(id => counts.GetValueOrDefault(id)),
                org.ChildrenOf(n.Id).Count))
            .ToList();

        return new PagedResult<DepartmentSummaryDto>(items, ordered.Count, page, pageSize);
    }

    public async Task<DepartmentDetailDto> GetDepartmentAsync(
        Guid accountId,
        Guid departmentId,
        CancellationToken cancellationToken = default)
    {
        var org = await LoadOrgAsync(cancellationToken);
        await EnsureAccessibleAsync(accountId, departmentId, org, cancellationToken);
        return await BuildDetailAsync(org, departmentId, cancellationToken);
    }

    public async Task<DepartmentOverviewDto> GetOverviewAsync(
        Guid accountId,
        Guid departmentId,
        Guid? periodId,
        CancellationToken cancellationToken = default)
    {
        var org = await LoadOrgAsync(cancellationToken);
        await EnsureAccessibleAsync(accountId, departmentId, org, cancellationToken);

        var today = Today();
        var periods = await LoadSelectablePeriodsAsync(cancellationToken);
        var period = await ResolvePeriodAsync(periodId, periods, today, cancellationToken);
        var subtree = org.Subtree(departmentId);

        var detail = await BuildDetailAsync(org, departmentId, cancellationToken);
        var metrics = await BuildMetricsAsync(subtree, today, cancellationToken);
        var (works, workMix) = await BuildWorkAsync(subtree, today, cancellationToken);
        var operational = await BuildOperationalAsync(subtree, works, period, cancellationToken);
        var performance = await BuildPerformanceAsync(departmentId, subtree, period, periods, cancellationToken);

        return new DepartmentOverviewDto(
            detail,
            period is null ? null : ToDto(period),
            periods.Select(ToDto).ToList(),
            metrics with { OpenWorkCount = works.Count, AttentionWorkCount = works.Count(w => IsAttention(w, today)) },
            performance,
            workMix,
            operational,
            time.GetUtcNow());
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1862:Use the 'StringComparison' method overloads to perform case-insensitive string comparisons",
        Justification = "EF Core must translate this predicate to SQL; Npgsql cannot translate StringComparison overloads.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version", Justification = "Translated to PostgreSQL lower(), using database collation rather than CLR culture.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "Npgsql translates only the parameterless ToLower() to SQL lower().")]
    public async Task<PagedResult<DepartmentMemberDto>> GetMembersAsync(
        Guid accountId,
        Guid departmentId,
        DepartmentMemberFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var status = ParseEnum<EmployeeStatus>(filter.Status, "status");

        var org = await LoadOrgAsync(cancellationToken);
        await EnsureAccessibleAsync(accountId, departmentId, org, cancellationToken);

        var periods = await LoadSelectablePeriodsAsync(cancellationToken);
        var period = await ResolvePeriodAsync(filter.PeriodId, periods, Today(), cancellationToken);
        var subtree = org.Subtree(departmentId);

        var query = db.Employees.AsNoTracking().Where(e => subtree.Contains(e.DepartmentId));

        query = status.HasValue
            ? query.Where(e => e.Status == status.Value)
            : query.Where(e => e.Status != EmployeeStatus.Terminated);

        if (filter.JobPositionId.HasValue)
        {
            query = query.Where(e => e.JobPositionId == filter.JobPositionId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim().ToLowerInvariant();
            query = query.Where(e =>
                e.EmployeeCode.ToLower().Contains(keyword)
                || e.FullName.ToLower().Contains(keyword)
                || e.Email.ToLower().Contains(keyword));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(e => e.EmployeeCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new MemberRow(
                e.Id,
                e.EmployeeCode,
                e.FullName,
                e.DepartmentId,
                e.Department.Name,
                e.JobPositionId,
                e.JobPosition.Title,
                e.Status))
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var openCounts = await db.Works.AsNoTracking()
            .Where(IsOpenWork)
            .Where(w => ids.Contains(w.AssigneeEmployeeId))
            .GroupBy(w => w.AssigneeEmployeeId)
            .Select(g => new { EmployeeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Count, cancellationToken);

        var scores = new Dictionary<Guid, decimal>();
        if (period is not null)
        {
            var evaluations = await db.PerformanceEvaluations.AsNoTracking()
                .Where(pe => pe.EvaluationPeriodId == period.Id
                    && pe.TotalScore != null
                    && pe.Status != PerformanceEvaluationStatus.Draft
                    && ids.Contains(pe.EmployeeId))
                .Select(pe => new { pe.EmployeeId, Score = pe.TotalScore!.Value })
                .ToListAsync(cancellationToken);
            scores = evaluations.ToDictionary(x => x.EmployeeId, x => Round(x.Score));
        }

        var items = rows
            .Select(r => new DepartmentMemberDto(
                r.Id,
                r.EmployeeCode,
                r.FullName,
                r.DepartmentId,
                r.DepartmentName,
                r.JobPositionId,
                r.JobPositionTitle,
                r.Status.ToEnumString(),
                openCounts.GetValueOrDefault(r.Id),
                scores.TryGetValue(r.Id, out var score) ? score : null))
            .ToList();

        return new PagedResult<DepartmentMemberDto>(items, totalCount, page, pageSize);
    }

    // ----- Detail ---------------------------------------------------------------------------

    private async Task<DepartmentDetailDto> BuildDetailAsync(
        OrgSnapshot org,
        Guid departmentId,
        CancellationToken cancellationToken)
    {
        var node = org.Nodes[departmentId];
        var subtree = org.Subtree(departmentId);
        var counts = await CountEmployeesByDepartmentAsync(cancellationToken);
        var manager = await ResolveManagerAsync(subtree, cancellationToken);

        return new DepartmentDetailDto(
            node.Id,
            node.Code,
            node.Name,
            node.Description,
            node.Level.ToEnumString(),
            node.Status.ToEnumString(),
            node.ParentId,
            org.Ancestors(departmentId),
            manager,
            counts.GetValueOrDefault(departmentId),
            subtree.Sum(id => counts.GetValueOrDefault(id)),
            org.ChildrenOf(departmentId).Count,
            node.LastSyncAt,
            node.UpdatedAt);
    }

    private async Task<DepartmentManagerDto?> ResolveManagerAsync(
        List<Guid> subtree,
        CancellationToken cancellationToken)
    {
        var active = await db.Employees.AsNoTracking()
            .Where(e => subtree.Contains(e.DepartmentId) && e.Status == EmployeeStatus.Active)
            .Select(e => new ManagerCandidate(e.Id, e.EmployeeCode, e.FullName, e.ManagerId, e.JobPosition.Title))
            .ToListAsync(cancellationToken);

        var insideIds = active.Select(e => e.Id).ToHashSet();
        var candidates = active
            .Where(e => e.ManagerId is null || !insideIds.Contains(e.ManagerId.Value))
            .ToList();

        var reportCounts = active
            .Where(e => e.ManagerId.HasValue)
            .GroupBy(e => e.ManagerId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var internalManager = candidates
            .Select(c => (Candidate: c, Reports: reportCounts.GetValueOrDefault(c.Id)))
            .Where(x => x.Reports > 0)
            .OrderByDescending(x => x.Reports)
            .ThenBy(x => x.Candidate.EmployeeCode, StringComparer.Ordinal)
            .Select(x => x.Candidate)
            .FirstOrDefault();

        if (internalManager is not null)
        {
            return new DepartmentManagerDto(
                internalManager.Id,
                internalManager.EmployeeCode,
                internalManager.FullName,
                internalManager.JobPositionTitle);
        }

        var outsideManagerIds = candidates
            .Where(c => c.ManagerId.HasValue)
            .GroupBy(c => c.ManagerId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        if (outsideManagerIds.Count == 0)
        {
            return null;
        }

        var keys = outsideManagerIds.Keys.ToList();
        var managers = await db.Employees.AsNoTracking()
            .Where(e => keys.Contains(e.Id) && e.Status != EmployeeStatus.Terminated)
            .Select(e => new ManagerCandidate(e.Id, e.EmployeeCode, e.FullName, e.ManagerId, e.JobPosition.Title))
            .ToListAsync(cancellationToken);

        var chosen = managers
            .OrderByDescending(m => outsideManagerIds[m.Id])
            .ThenBy(m => m.EmployeeCode, StringComparer.Ordinal)
            .FirstOrDefault();

        return chosen is null
            ? null
            : new DepartmentManagerDto(chosen.Id, chosen.EmployeeCode, chosen.FullName, chosen.JobPositionTitle);
    }

    // ----- Overview -------------------------------------------------------------------------

    private async Task<DepartmentMetricsDto> BuildMetricsAsync(
        List<Guid> subtree,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        _ = today;
        var byStatus = await db.Employees.AsNoTracking()
            .Where(e => subtree.Contains(e.DepartmentId))
            .GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var active = byStatus.Where(x => x.Status == EmployeeStatus.Active).Sum(x => x.Count);
        var onLeave = byStatus.Where(x => x.Status == EmployeeStatus.OnLeave).Sum(x => x.Count);
        var total = byStatus.Where(x => x.Status != EmployeeStatus.Terminated).Sum(x => x.Count);

        var pendingReviews = await db.ResultSubmissions.AsNoTracking()
            .Where(s => (s.Status == SubmissionStatus.Submitted || s.Status == SubmissionStatus.UnderReview)
                && subtree.Contains(s.Work.AssigneeEmployee.DepartmentId))
            .CountAsync(cancellationToken);

        return new DepartmentMetricsDto(total, active, onLeave, 0, 0, pendingReviews);
    }

    private async Task<(List<OpenWorkRow> Works, List<DepartmentWorkMixDto> Mix)> BuildWorkAsync(
        List<Guid> subtree,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        _ = today;
        var works = await db.Works.AsNoTracking()
            .Where(IsOpenWork)
            .Where(w => subtree.Contains(w.AssigneeEmployee.DepartmentId))
            .Select(w => new OpenWorkRow(
                w.Status,
                w.PlannedEndDate,
                w.WorkTypeId,
                w.WorkType != null ? w.WorkType.Code : null,
                w.WorkType != null ? w.WorkType.Name : null))
            .ToListAsync(cancellationToken);

        var mix = works
            .GroupBy(w => w.WorkTypeId)
            .Select(g => new DepartmentWorkMixDto(g.Key, g.First().WorkTypeCode, g.First().WorkTypeName, g.Count()))
            .OrderByDescending(m => m.Count)
            .ThenBy(m => m.Name is null)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        return (works, mix);
    }

    private async Task<DepartmentOperationalDto> BuildOperationalAsync(
        List<Guid> subtree,
        List<OpenWorkRow> works,
        EvaluationPeriod? period,
        CancellationToken cancellationToken)
    {
        var today = Today();
        int? completed = null;

        if (period is not null)
        {
            var start = new DateTimeOffset(period.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var endExclusive = new DateTimeOffset(period.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            completed = await db.Works.AsNoTracking()
                .Where(w => (w.Status == WorkStatus.Done || w.Status == WorkStatus.Verified)
                    && subtree.Contains(w.AssigneeEmployee.DepartmentId)
                    && w.UpdatedAt >= start
                    && w.UpdatedAt < endExclusive)
                .CountAsync(cancellationToken);
        }

        return new DepartmentOperationalDto(
            works.Count(w => w.Status == WorkStatus.New),
            works.Count(w => w.Status is WorkStatus.InProgress or WorkStatus.HandedOver),
            works.Count(w => IsOverdue(w, today)),
            null,
            completed);
    }

    private async Task<DepartmentPerformanceDto> BuildPerformanceAsync(
        Guid departmentId,
        List<Guid> subtree,
        EvaluationPeriod? period,
        List<EvaluationPeriod> periods,
        CancellationToken cancellationToken)
    {
        if (period is null)
        {
            return new DepartmentPerformanceDto(null, null, null, 0, []);
        }

        var (average, count) = await AverageScoreAsync(period.Id, subtree, cancellationToken);

        decimal? change = null;
        if (average.HasValue)
        {
            var previous = periods
                .Where(p => p.PeriodType == period.PeriodType && p.EndDate < period.StartDate)
                .OrderByDescending(p => p.EndDate)
                .FirstOrDefault();
            if (previous is not null)
            {
                var (previousAverage, _) = await AverageScoreAsync(previous.Id, subtree, cancellationToken);
                change = previousAverage.HasValue ? Round(average.Value - previousAverage.Value) : null;
            }
        }

        var kpis = await BuildKpisAsync(departmentId, subtree, period, cancellationToken);
        return new DepartmentPerformanceDto(average, null, change, count, kpis);
    }

    private async Task<(decimal? Average, int Count)> AverageScoreAsync(
        Guid periodId,
        List<Guid> subtree,
        CancellationToken cancellationToken)
    {
        var scores = await db.PerformanceEvaluations.AsNoTracking()
            .Where(pe => pe.EvaluationPeriodId == periodId
                && pe.TotalScore != null
                && pe.Status != PerformanceEvaluationStatus.Draft
                && pe.Employee.Status != EmployeeStatus.Terminated
                && subtree.Contains(pe.Employee.DepartmentId))
            .Select(pe => pe.TotalScore!.Value)
            .ToListAsync(cancellationToken);

        return scores.Count == 0 ? (null, 0) : (Round(scores.Average()), scores.Count);
    }

    private async Task<List<DepartmentKpiPerformanceDto>> BuildKpisAsync(
        Guid departmentId,
        List<Guid> subtree,
        EvaluationPeriod period,
        CancellationToken cancellationToken)
    {
        var assignments = await db.TargetAssignments.AsNoTracking()
            .Where(t => t.DepartmentId == departmentId
                && t.EvaluationPeriodId == period.Id
                && t.Status == TargetAssignmentStatus.Active)
            .Select(t => new TargetRow(
                t.KpiDefinitionId,
                t.Version,
                t.TargetValue,
                t.KpiDefinition.Code,
                t.KpiDefinition.Name,
                t.KpiDefinition.Unit,
                t.KpiDefinition.Direction,
                t.KpiDefinition.AggregationType,
                t.KpiDefinition.MaxAchievementPercent))
            .ToListAsync(cancellationToken);

        var latest = assignments
            .GroupBy(a => a.KpiDefinitionId)
            .Select(g => g.OrderByDescending(a => a.Version).First())
            .OrderBy(a => a.Code, StringComparer.Ordinal)
            .ToList();

        if (latest.Count == 0)
        {
            return [];
        }

        var kpiIds = latest.Select(a => a.KpiDefinitionId).ToList();
        var progress = await db.ActualProgresses.AsNoTracking()
            .Where(a => a.Status == ActualProgressStatus.Valid
                && kpiIds.Contains(a.TargetAssignment.KpiDefinitionId)
                && a.ProgressDate >= period.StartDate
                && a.ProgressDate <= period.EndDate
                && a.TargetAssignment.Employee != null
                && a.TargetAssignment.Employee.Status != EmployeeStatus.Terminated
                && subtree.Contains(a.TargetAssignment.Employee.DepartmentId))
            .Select(a => new ProgressRow(a.TargetAssignment.KpiDefinitionId, a.Value, a.Denominator))
            .ToListAsync(cancellationToken);

        var byKpi = progress.ToLookup(p => p.KpiDefinitionId);

        return latest
            .Select(a =>
            {
                var actual = AggregateActual(a, byKpi[a.KpiDefinitionId].ToList());
                return new DepartmentKpiPerformanceDto(
                    a.KpiDefinitionId,
                    a.Code,
                    a.Name,
                    a.Unit,
                    a.Direction.ToEnumString(),
                    a.TargetValue,
                    actual,
                    Achievement(a.Direction, a.TargetValue, actual, a.MaxAchievementPercent));
            })
            .ToList();
    }

    private static decimal? AggregateActual(TargetRow target, List<ProgressRow> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        switch (target.AggregationType)
        {
            case KpiAggregationType.Sum:
                return Round(rows.Sum(r => r.Value));
            case KpiAggregationType.Avg:
                return Round(rows.Average(r => r.Value));
            case KpiAggregationType.Ratio:
                var denominator = rows.Sum(r => r.Denominator ?? 0m);
                if (denominator == 0m)
                {
                    return null;
                }

                var ratio = rows.Sum(r => r.Value) / denominator;
                return Round(target.Unit.Trim() == "%" ? ratio * 100m : ratio);
            default:
                return null;
        }
    }

    /// <summary>Achievement percent for a KPI; null when the actual or a non-zero target is unavailable.</summary>
    public static decimal? Achievement(KpiDirection direction, decimal target, decimal? actual, decimal? maxPercent)
    {
        if (!actual.HasValue || target == 0m)
        {
            return null;
        }

        decimal value;
        switch (direction)
        {
            case KpiDirection.HigherBetter:
                value = actual.Value / target * 100m;
                break;
            case KpiDirection.LowerBetter:
                value = actual.Value == 0m ? maxPercent ?? 100m : target / actual.Value * 100m;
                break;
            default:
                value = 100m - Math.Abs(actual.Value - target) / Math.Abs(target) * 100m;
                break;
        }

        value = Math.Max(value, 0m);
        if (maxPercent.HasValue)
        {
            value = Math.Min(value, maxPercent.Value);
        }

        return Round(value);
    }

    // ----- Periods --------------------------------------------------------------------------

    private async Task<List<EvaluationPeriod>> LoadSelectablePeriodsAsync(CancellationToken cancellationToken)
    {
        var periods = await db.EvaluationPeriods.AsNoTracking()
            .Where(p => p.PeriodType != PeriodType.Day)
            .ToListAsync(cancellationToken);

        return periods
            .OrderByDescending(p => p.StartDate)
            .ThenByDescending(p => (int)p.PeriodType)
            .ToList();
    }

    private async Task<EvaluationPeriod?> ResolvePeriodAsync(
        Guid? periodId,
        List<EvaluationPeriod> selectable,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (periodId.HasValue)
        {
            var existing = selectable.FirstOrDefault(p => p.Id == periodId.Value);
            if (existing is not null)
            {
                return existing;
            }

            var isDay = await db.EvaluationPeriods.AsNoTracking()
                .AnyAsync(p => p.Id == periodId.Value && p.PeriodType == PeriodType.Day, cancellationToken);
            if (isDay)
            {
                throw new ValidationException("Kỳ đánh giá theo ngày không được hỗ trợ cho màn hình phòng ban.");
            }

            throw new NotFoundException("Kỳ đánh giá", periodId.Value);
        }

        var current = selectable
            .Where(p => p.StartDate <= today && p.EndDate >= today)
            .OrderByDescending(p => (int)p.PeriodType)
            .FirstOrDefault();
        if (current is not null)
        {
            return current;
        }

        return selectable
            .Where(p => p.EndDate < today)
            .OrderByDescending(p => p.EndDate)
            .ThenByDescending(p => (int)p.PeriodType)
            .FirstOrDefault();
    }

    private static EvaluationPeriodDto ToDto(EvaluationPeriod p) =>
        new(p.Id, p.Code, p.Name, p.PeriodType.ToEnumString(), p.StartDate, p.EndDate);

    // ----- Organization & access ------------------------------------------------------------

    private async Task<OrgSnapshot> LoadOrgAsync(CancellationToken cancellationToken)
    {
        var nodes = await db.Departments.AsNoTracking()
            .Select(d => new DeptNode(
                d.Id,
                d.Code,
                d.Name,
                d.Description,
                d.ParentDepartmentId,
                d.LevelType,
                d.Status,
                d.LastSyncAt,
                d.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new OrgSnapshot(nodes);
    }

    private async Task<Dictionary<Guid, int>> CountEmployeesByDepartmentAsync(CancellationToken cancellationToken)
    {
        return await db.Employees.AsNoTracking()
            .Where(e => e.Status != EmployeeStatus.Terminated)
            .GroupBy(e => e.DepartmentId)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count, cancellationToken);
    }

    private async Task EnsureAccessibleAsync(
        Guid accountId,
        Guid departmentId,
        OrgSnapshot org,
        CancellationToken cancellationToken)
    {
        if (!org.Nodes.ContainsKey(departmentId))
        {
            throw new NotFoundException("Phòng ban", departmentId);
        }

        var visible = await ResolveVisibleAsync(accountId, org, cancellationToken);
        if (visible is not null && !visible.Contains(departmentId))
        {
            throw new ForbiddenException("Bạn không có quyền xem phòng ban này.");
        }
    }

    /// <summary>Returns null when every department is visible.</summary>
    private async Task<HashSet<Guid>?> ResolveVisibleAsync(
        Guid accountId,
        OrgSnapshot org,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId && !a.IsLocked && a.Employee.Status == EmployeeStatus.Active)
            .Select(a => new { a.Employee.DepartmentId })
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            throw new ForbiddenException();
        }

        var granted = await permissions.GetEffectivePermissionsAsync(accountId, cancellationToken);
        var level = granted
            .Where(p => (p.Resource == "*" && p.Action == "*")
                || (string.Equals(p.Resource, "evaluation", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Action, "read", StringComparison.OrdinalIgnoreCase)))
            .Select(p => PermissionScopes.GetScopeLevel(p.Scope))
            .DefaultIfEmpty(0)
            .Max();

        if (level >= AllScopeLevel)
        {
            return null;
        }

        if (level < DepartmentScopeLevel)
        {
            throw new ForbiddenException();
        }

        var ownDepartmentId = account.DepartmentId;
        if (!org.Nodes.ContainsKey(ownDepartmentId))
        {
            throw new ForbiddenException();
        }

        var rootId = level >= BranchScopeLevel ? org.BranchRoot(ownDepartmentId) : ownDepartmentId;
        return org.Subtree(rootId).ToHashSet();
    }

    private static TEnum? ParseEnum<TEnum>(string? value, string field)
        where TEnum : struct, Enum => EnumApiFormat.ParseEnum<TEnum>(value, field);

    private DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    private static bool IsOverdue(OpenWorkRow work, DateOnly today) =>
        work.PlannedEndDate.HasValue && work.PlannedEndDate.Value < today;

    private static bool IsAttention(OpenWorkRow work, DateOnly today) =>
        work.Status == WorkStatus.Rejected || IsOverdue(work, today);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // ----- Private shapes -------------------------------------------------------------------

    private sealed record DeptNode(
        Guid Id,
        string Code,
        string Name,
        string? Description,
        Guid? ParentId,
        DepartmentLevelType Level,
        RecordStatus Status,
        DateTimeOffset LastSyncAt,
        DateTimeOffset UpdatedAt);

    private sealed record ManagerCandidate(
        Guid Id,
        string EmployeeCode,
        string FullName,
        Guid? ManagerId,
        string JobPositionTitle);

    private sealed record MemberRow(
        Guid Id,
        string EmployeeCode,
        string FullName,
        Guid DepartmentId,
        string DepartmentName,
        Guid JobPositionId,
        string JobPositionTitle,
        EmployeeStatus Status);

    private sealed record OpenWorkRow(
        WorkStatus Status,
        DateOnly? PlannedEndDate,
        Guid? WorkTypeId,
        string? WorkTypeCode,
        string? WorkTypeName);

    private sealed record TargetRow(
        Guid KpiDefinitionId,
        int Version,
        decimal TargetValue,
        string Code,
        string Name,
        string Unit,
        KpiDirection Direction,
        KpiAggregationType AggregationType,
        decimal? MaxAchievementPercent);

    private sealed record ProgressRow(Guid KpiDefinitionId, decimal Value, decimal? Denominator);

    private sealed class OrgSnapshot
    {
        private static readonly List<Guid> NoChildren = [];
        private readonly Dictionary<Guid, List<Guid>> _children;

        public OrgSnapshot(IReadOnlyCollection<DeptNode> nodes)
        {
            Nodes = nodes.ToDictionary(n => n.Id);
            _children = nodes
                .Where(n => n.ParentId.HasValue && Nodes.ContainsKey(n.ParentId.Value))
                .GroupBy(n => n.ParentId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());
        }

        public Dictionary<Guid, DeptNode> Nodes { get; }

        public List<Guid> ChildrenOf(Guid id) => _children.GetValueOrDefault(id) ?? NoChildren;

        /// <summary>The department and all of its descendants (cycle-safe).</summary>
        public List<Guid> Subtree(Guid id)
        {
            var result = new List<Guid>();
            var seen = new HashSet<Guid>();
            var queue = new Queue<Guid>();
            queue.Enqueue(id);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!seen.Add(current))
                {
                    continue;
                }

                result.Add(current);
                foreach (var child in ChildrenOf(current))
                {
                    queue.Enqueue(child);
                }
            }

            return result;
        }

        /// <summary>Ancestors ordered root first, excluding the department itself.</summary>
        public List<DepartmentRefDto> Ancestors(Guid id)
        {
            var chain = new List<DepartmentRefDto>();
            var seen = new HashSet<Guid> { id };
            var current = Nodes[id].ParentId;
            while (current.HasValue && Nodes.TryGetValue(current.Value, out var node) && seen.Add(node.Id))
            {
                chain.Add(new DepartmentRefDto(node.Id, node.Code, node.Name));
                current = node.ParentId;
            }

            chain.Reverse();
            return chain;
        }

        /// <summary>Nearest BRANCH ancestor-or-self; the root-most ancestor when none exists.</summary>
        public Guid BranchRoot(Guid id)
        {
            var current = id;
            var seen = new HashSet<Guid>();
            while (seen.Add(current))
            {
                var node = Nodes[current];
                if (node.Level == DepartmentLevelType.Branch || !node.ParentId.HasValue
                    || !Nodes.ContainsKey(node.ParentId.Value))
                {
                    return current;
                }

                current = node.ParentId.Value;
            }

            return current;
        }
    }
}
