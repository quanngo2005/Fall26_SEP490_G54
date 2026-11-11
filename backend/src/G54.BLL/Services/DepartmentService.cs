using System.Linq.Expressions;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Department;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

public sealed partial class DepartmentService(
    AppDbContext db,
    IPermissionService permissions,
    TimeProvider time,
    IOptions<SystemParametersOptions> parameters) : IDepartmentService
{
    private static readonly int DepartmentScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Dept);
    private static readonly int BranchScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Branch);
    private static readonly int AllScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.All);

    private static readonly Expression<Func<Work, bool>> IsOpenWork = w => WorkStatusGroups.Open.Contains(w.Status);

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

        IEnumerable<DepartmentNode> query = org.Nodes.Values;
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

        // Soft-deleted (ARCHIVED) departments are listed only when asked for explicitly.
        query = status.HasValue
            ? query.Where(n => n.Status == status.Value)
            : query.Where(n => n.Status != RecordStatus.Archived);

        if (level.HasValue)
        {
            query = query.Where(n => n.Level == level.Value);
        }

        if (filter.ParentDepartmentId.HasValue)
        {
            query = query.Where(n => n.ParentId == filter.ParentDepartmentId.Value);
        }

        var ordered = query.OrderBy(n => n.Code, StringComparer.Ordinal).ToList();
        var pagedNodes = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var pagedSubtreeIds = pagedNodes.SelectMany(n => org.Subtree(n.Id)).Distinct().ToList();
        var activeEmployees = await db.Employees.AsNoTracking()
            .Where(e => pagedSubtreeIds.Contains(e.DepartmentId) && e.Status == EmployeeStatus.Active)
            .Select(e => new ManagerCandidate(
                e.Id, e.EmployeeCode, e.FullName, e.ManagerId, e.JobPosition.Title, e.DepartmentId, e.JobPosition.Code))
            .ToListAsync(cancellationToken);

        var items = new List<DepartmentSummaryDto>();
        foreach (var n in pagedNodes)
        {
            var subtree = org.Subtree(n.Id);
            var subtreeSet = subtree.ToHashSet();
            var subtreeEmployees = activeEmployees.Where(e => subtreeSet.Contains(e.DepartmentId)).ToList();
            var tellers = subtreeEmployees.Count(e => e.JobPositionCode == JobPositionCodes.Teller);
            var sales = subtreeEmployees.Count(e => e.JobPositionCode == JobPositionCodes.Sales);

            items.Add(new DepartmentSummaryDto(
                n.Id,
                n.Code,
                n.Name,
                n.Level.ToEnumString(),
                n.Status.ToEnumString(),
                n.ParentId,
                n.ParentId.HasValue && org.Nodes.TryGetValue(n.ParentId.Value, out var parent) ? parent.Name : null,
                subtree.Sum(id => counts.GetValueOrDefault(id)),
                org.ChildrenOf(n.Id).Count,
                FindInternalManager(subtreeEmployees)?.FullName,
                tellers,
                sales,
                n.LastSyncAt));
        }

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
        string? compare = null,
        CancellationToken cancellationToken = default)
    {
        var mode = ParseCompare(compare);
        var org = await LoadOrgAsync(cancellationToken);
        await EnsureAccessibleAsync(accountId, departmentId, org, cancellationToken);

        var today = Today();
        var periods = await LoadSelectablePeriodsAsync(cancellationToken);
        var period = await ResolvePeriodAsync(periodId, periods, today, cancellationToken);
        var comparePeriod = ResolveComparePeriod(period, periods, mode);
        var subtree = org.Subtree(departmentId);

        var detail = await BuildDetailAsync(org, departmentId, cancellationToken);
        var metrics = await BuildMetricsAsync(subtree, cancellationToken);
        var (works, workMix) = await BuildWorkAsync(subtree, cancellationToken);
        var operational = await BuildOperationalAsync(subtree, works, period, metrics.PendingReviewCount, cancellationToken);
        var snapshot = await LoadSnapshotAsync(subtree, period, comparePeriod, cancellationToken);
        var performance = await BuildPerformanceAsync(
            departmentId, subtree, period, comparePeriod, periods, snapshot, cancellationToken);
        var childUnits = BuildChildUnits(org, departmentId, snapshot);
        var overdueByEmployee = works
            .Where(w => IsOverdue(w, today))
            .GroupBy(w => w.AssigneeEmployeeId)
            .ToDictionary(g => g.Key, g => g.Count());
        var (declined, compared) = CountDeclined(snapshot, comparePeriod is not null);

        return new DepartmentOverviewDto(
            detail,
            period is null ? null : ToDto(period),
            comparePeriod is null ? null : ToDto(comparePeriod),
            periods.Select(ToDto).ToList(),
            metrics with
            {
                OpenWorkCount = works.Count,
                AttentionWorkCount = works.Count(w => IsAttention(w, today)),
                DeclinedEmployeeCount = declined,
                ComparedEmployeeCount = compared,
                DeclinedChildUnitCount = comparePeriod is null || childUnits.Count == 0
                    ? null
                    : childUnits.Count(c => c.ChangeFromPrevious < 0),
            },
            performance,
            workMix,
            operational,
            period is null ? [] : BuildAttention(snapshot, overdueByEmployee),
            childUnits,
            time.GetUtcNow());
    }

    public async Task<PagedResult<DepartmentMemberDto>> GetMembersAsync(
        Guid accountId,
        Guid departmentId,
        DepartmentMemberFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var members = await LoadMemberRowsAsync(accountId, departmentId, filter, cancellationToken);
        var items = await BuildMembersAsync(members, (page - 1) * pageSize, pageSize, cancellationToken);
        return new PagedResult<DepartmentMemberDto>(items, members.Rows.Count, page, pageSize);
    }

    /// <summary>The filtered subtree employees, the resolved periods and the sort; every filter is validated here.</summary>
    private async Task<MemberSelection> LoadMemberRowsAsync(
        Guid accountId,
        Guid departmentId,
        DepartmentMemberFilterDto filter,
        CancellationToken cancellationToken)
    {
        var status = ParseEnum<EmployeeStatus>(filter.Status, "status");
        var mode = ParseCompare(filter.Compare);
        var sort = ParseEnum<DepartmentMemberSort>(filter.Sort, "sort");

        var org = await LoadOrgAsync(cancellationToken);
        await EnsureAccessibleAsync(accountId, departmentId, org, cancellationToken);

        var periods = await LoadSelectablePeriodsAsync(cancellationToken);
        var period = await ResolvePeriodAsync(filter.PeriodId, periods, Today(), cancellationToken);
        var comparePeriod = ResolveComparePeriod(period, periods, mode);
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
            query = WhereKeyword(query, filter.Keyword.Trim().ToLowerInvariant());
        }

        var rows = await query
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

        return new MemberSelection(rows, period, comparePeriod, sort);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1862:Use the 'StringComparison' method overloads to perform case-insensitive string comparisons",
        Justification = "EF Core must translate this predicate to SQL; Npgsql cannot translate StringComparison overloads.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version", Justification = "Translated to PostgreSQL lower(), using database collation rather than CLR culture.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "Npgsql translates only the parameterless ToLower() to SQL lower().")]
    private static IQueryable<Employee> WhereKeyword(IQueryable<Employee> query, string keyword) =>
        query.Where(e =>
            e.EmployeeCode.ToLower().Contains(keyword)
            || e.FullName.ToLower().Contains(keyword)
            || e.Email.ToLower().Contains(keyword));

    /// <summary>
    /// Sorts the rows (with a sort: grouped by job position title, the sort inside each position), takes the requested
    /// slice and adds scores, profile group scores and work counts to it.
    /// </summary>
    private async Task<List<DepartmentMemberDto>> BuildMembersAsync(
        MemberSelection selection,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var (rows, period, comparePeriod, sort) = selection;
        var ids = rows.Select(r => r.Id).ToList();
        var current = period is null ? [] : await LoadScoredAsync(period.Id, ids, cancellationToken);
        var previous = comparePeriod is null ? [] : await LoadScoredAsync(comparePeriod.Id, ids, cancellationToken);

        decimal? Score(Guid id) => current.TryGetValue(id, out var row) ? Round(row.Score) : null;
        decimal? PreviousScore(Guid id) => previous.TryGetValue(id, out var row) ? Round(row.Score) : null;

        // Without a sort the list keeps its original order (employee code) for the clients that predate SCR-PRF-02.
        var byPosition = sort is null
            ? rows.OrderBy(_ => 0)
            : rows.OrderBy(r => r.JobPositionTitle, StringComparer.Ordinal);
        var ordered = sort switch
        {
            DepartmentMemberSort.TotalAsc => byPosition
                .ThenBy(r => Score(r.Id) is null)
                .ThenBy(r => Score(r.Id)),
            DepartmentMemberSort.Code or null => byPosition,
            _ => byPosition
                .ThenBy(r => Change(Score(r.Id), PreviousScore(r.Id)) is null)
                .ThenByDescending(r => Change(Score(r.Id), PreviousScore(r.Id))),
        };
        var pageRows = ordered.ThenBy(r => r.EmployeeCode, StringComparer.Ordinal).Skip(skip).Take(take).ToList();

        var pageIds = pageRows.Select(r => r.Id).ToList();
        var today = Today();
        var openWorks = await db.Works.AsNoTracking()
            .Where(IsOpenWork)
            .Where(w => pageIds.Contains(w.AssigneeEmployeeId))
            .Select(w => new { w.AssigneeEmployeeId, w.Status, w.PlannedEndDate })
            .ToListAsync(cancellationToken);
        var pendingReviews = await db.ResultSubmissions.AsNoTracking()
            .Where(s => (s.Status == SubmissionStatus.Submitted || s.Status == SubmissionStatus.UnderReview)
                && pageIds.Contains(s.Work.AssigneeEmployeeId))
            .GroupBy(s => s.Work.AssigneeEmployeeId)
            .Select(g => new { EmployeeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Count, cancellationToken);

        var statuses = new Dictionary<Guid, PerformanceEvaluationStatus>();
        var groupScores = new Dictionary<Guid, IReadOnlyList<GroupScore>>();
        var positionRoots = new Dictionary<Guid, IReadOnlyList<ProfileNode>>();
        if (period is not null)
        {
            statuses = (await db.PerformanceEvaluations.AsNoTracking()
                    .Where(pe => pe.EvaluationPeriodId == period.Id && pageIds.Contains(pe.EmployeeId))
                    .Select(pe => new { pe.EmployeeId, pe.Status })
                    .ToListAsync(cancellationToken))
                .GroupBy(x => x.EmployeeId)
                .ToDictionary(g => g.Key, g => g.First().Status);

            var pageScored = pageRows.Where(r => current.ContainsKey(r.Id)).Select(r => current[r.Id]).ToList();
            var positionProfiles = await LoadPositionProfilesAsync(
                pageRows.Select(r => r.JobPositionId).Distinct().ToList(), period, cancellationToken);
            var trees = await LoadProfileTreesAsync(
                pageScored.Select(s => s.ProfileId).Concat(positionProfiles.Values), cancellationToken);
            groupScores = await LoadGroupScoresAsync(pageScored, trees, cancellationToken);
            positionRoots = positionProfiles.ToDictionary(x => x.Key, x => trees.GetValueOrDefault(x.Value) ?? []);
        }

        var weakScore = parameters.Value.PerfWeakGroupScore;
        return pageRows
            .Select(r =>
            {
                var memberScores = groupScores.GetValueOrDefault(r.Id, []);
                var groups = positionRoots.GetValueOrDefault(r.JobPositionId, [])
                    .Select(root =>
                    {
                        var name = EvaluationProfileTree.NodeName(root);
                        return new DepartmentMemberGroupDto(
                            root.Item.Id,
                            name,
                            root.Item.DisplayOrder,
                            root.Item.WeightPercent,
                            memberScores.FirstOrDefault(gs => string.Equals(gs.Name, name, StringComparison.Ordinal))?.Score);
                    })
                    .ToList();
                var weakest = groups
                    .Where(g => g.Score.HasValue)
                    .OrderBy(g => g.Score)
                    .FirstOrDefault();
                var memberWorks = openWorks.Where(w => w.AssigneeEmployeeId == r.Id).ToList();
                var score = Score(r.Id);
                var previousScore = PreviousScore(r.Id);

                return new DepartmentMemberDto(
                    r.Id,
                    r.EmployeeCode,
                    r.FullName,
                    r.DepartmentId,
                    r.DepartmentName,
                    r.JobPositionId,
                    r.JobPositionTitle,
                    r.Status.ToEnumString(),
                    memberWorks.Count,
                    score,
                    statuses.TryGetValue(r.Id, out var status) ? status.ToEnumString() : null,
                    previousScore,
                    Change(score, previousScore),
                    groups,
                    weakest is not null && weakest.Score < weakScore ? weakest.ProfileItemId : null,
                    memberWorks.Count(w => IsOverdue(w.Status, w.PlannedEndDate, today)),
                    pendingReviews.GetValueOrDefault(r.Id));
            })
            .ToList();
    }

    // ----- Detail ---------------------------------------------------------------------------

    private async Task<DepartmentDetailDto> BuildDetailAsync(
        DepartmentHierarchy org,
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
            .Select(e => new ManagerCandidate(
                e.Id, e.EmployeeCode, e.FullName, e.ManagerId, e.JobPosition.Title, e.DepartmentId, e.JobPosition.Code))
            .ToListAsync(cancellationToken);

        var internalManager = FindInternalManager(active);
        if (internalManager is not null)
        {
            return new DepartmentManagerDto(
                internalManager.Id,
                internalManager.EmployeeCode,
                internalManager.FullName,
                internalManager.JobPositionTitle);
        }

        var insideIds = active.Select(e => e.Id).ToHashSet();
        var outsideManagerIds = active
            .Where(e => e.ManagerId.HasValue && !insideIds.Contains(e.ManagerId.Value))
            .GroupBy(e => e.ManagerId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        if (outsideManagerIds.Count == 0)
        {
            return null;
        }

        var keys = outsideManagerIds.Keys.ToList();
        var managers = await db.Employees.AsNoTracking()
            .Where(e => keys.Contains(e.Id) && e.Status != EmployeeStatus.Terminated)
            .Select(e => new ManagerCandidate(
                e.Id, e.EmployeeCode, e.FullName, e.ManagerId, e.JobPosition.Title, e.DepartmentId, e.JobPosition.Code))
            .ToListAsync(cancellationToken);

        var chosen = managers
            .OrderByDescending(m => outsideManagerIds[m.Id])
            .ThenBy(m => m.EmployeeCode, StringComparer.Ordinal)
            .FirstOrDefault();

        return chosen is null
            ? null
            : new DepartmentManagerDto(chosen.Id, chosen.EmployeeCode, chosen.FullName, chosen.JobPositionTitle);
    }

    /// <summary>
    /// Manager rule step 1 (docs/api/DEPARTMENT_CONTRACT.md): among the active employees of a subtree, the one
    /// whose own manager is outside it and who has the most direct reports inside (tie: lowest employee code).
    /// </summary>
    private static ManagerCandidate? FindInternalManager(IReadOnlyCollection<ManagerCandidate> active)
    {
        var insideIds = active.Select(e => e.Id).ToHashSet();
        var reportCounts = active
            .Where(e => e.ManagerId.HasValue)
            .GroupBy(e => e.ManagerId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return active
            .Where(e => e.ManagerId is null || !insideIds.Contains(e.ManagerId.Value))
            .Select(c => (Candidate: c, Reports: reportCounts.GetValueOrDefault(c.Id)))
            .Where(x => x.Reports > 0)
            .OrderByDescending(x => x.Reports)
            .ThenBy(x => x.Candidate.EmployeeCode, StringComparer.Ordinal)
            .Select(x => x.Candidate)
            .FirstOrDefault();
    }

    // ----- Overview -------------------------------------------------------------------------

    private async Task<DepartmentMetricsDto> BuildMetricsAsync(
        List<Guid> subtree,
        CancellationToken cancellationToken)
    {
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
        CancellationToken cancellationToken)
    {
        var works = await db.Works.AsNoTracking()
            .Where(IsOpenWork)
            .Where(w => subtree.Contains(w.AssigneeEmployee.DepartmentId))
            .Select(w => new OpenWorkRow(
                w.AssigneeEmployeeId,
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
        int pendingReview,
        CancellationToken cancellationToken)
    {
        var today = Today();
        int? completed = null;

        if (period is not null)
        {
            var start = new DateTimeOffset(period.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var endExclusive = new DateTimeOffset(period.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            completed = await db.Works.AsNoTracking()
                .Where(w => WorkStatusGroups.Completed.Contains(w.Status)
                    && subtree.Contains(w.AssigneeEmployee.DepartmentId)
                    && w.UpdatedAt >= start
                    && w.UpdatedAt < endExclusive)
                .CountAsync(cancellationToken);
        }

        // In progress excludes overdue work, so completed / in progress / waiting / overdue are disjoint.
        return new DepartmentOperationalDto(
            works.Count(w => w.Status == WorkStatus.New),
            works.Count(w => (w.Status is WorkStatus.InProgress or WorkStatus.HandedOver) && !IsOverdue(w, today)),
            works.Count(w => IsOverdue(w, today)),
            null,
            completed,
            pendingReview);
    }

    private async Task<DepartmentPerformanceDto> BuildPerformanceAsync(
        Guid departmentId,
        List<Guid> subtree,
        EvaluationPeriod? period,
        EvaluationPeriod? comparePeriod,
        List<EvaluationPeriod> periods,
        ScoreSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (period is null)
        {
            return new DepartmentPerformanceDto(null, null, null, null, 0, [], [], [], []);
        }

        var average = Mean(snapshot.Current.Values.Select(s => s.Score));
        var previousAverage = Mean(snapshot.Previous.Values);

        var kpis = await LoadKpiRowsAsync(departmentId, subtree, period, includeCampaign: true, cancellationToken);
        var previousAchievement = comparePeriod is null
            ? []
            : (await LoadKpiRowsAsync(departmentId, subtree, comparePeriod, includeCampaign: false, cancellationToken))
                .ToDictionary(k => k.KpiDefinitionId, k => k.AchievementPercent);

        return new DepartmentPerformanceDto(
            average,
            previousAverage,
            null,
            Change(average, previousAverage),
            snapshot.Current.Count,
            kpis.Select(k => new DepartmentKpiPerformanceDto(
                k.KpiDefinitionId,
                k.Code,
                k.Name,
                k.Unit,
                k.Direction.ToEnumString(),
                k.TargetValue,
                k.ActualValue,
                k.AchievementPercent)).ToList(),
            await BuildTrendAsync(period, periods, snapshot, cancellationToken),
            BuildKpiGroups(kpis, previousAchievement, snapshot),
            BuildPositions(snapshot));
    }

    /// <summary>
    /// One row per KPI with an ACTIVE target on this node and period (highest version wins), its actual over the
    /// subtree and, when asked, the part of a SUM actual that came from campaign work (BR-26).
    /// </summary>
    private async Task<List<KpiRow>> LoadKpiRowsAsync(
        Guid departmentId,
        List<Guid> subtree,
        EvaluationPeriod period,
        bool includeCampaign,
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
        var campaign = includeCampaign
            ? await LoadCampaignActualsAsync(
                latest.Where(a => a.AggregationType == KpiAggregationType.Sum).Select(a => a.KpiDefinitionId).ToList(),
                subtree,
                period,
                cancellationToken)
            : [];

        return latest
            .Select(a =>
            {
                var actual = AggregateActual(a, byKpi[a.KpiDefinitionId].ToList());
                decimal? campaignActual = includeCampaign && actual.HasValue && a.AggregationType == KpiAggregationType.Sum
                    ? Round(campaign.GetValueOrDefault(a.KpiDefinitionId))
                    : null;
                return new KpiRow(
                    a.KpiDefinitionId,
                    a.Code,
                    a.Name,
                    a.Unit,
                    a.Direction,
                    a.TargetValue,
                    actual,
                    Achievement(a.Direction, a.TargetValue, actual, a.MaxAchievementPercent),
                    campaignActual);
            })
            .ToList();
    }

    /// <summary>
    /// Sum of the raw values that VALID verified results of campaign work added to the progress rows counted in the
    /// KPI actual (same KPI, period and subtree filters), by KPI. A verified result reaches its work through the
    /// verification's submission or through the matched transaction.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> LoadCampaignActualsAsync(
        List<Guid> kpiIds,
        List<Guid> subtree,
        EvaluationPeriod period,
        CancellationToken cancellationToken)
    {
        if (kpiIds.Count == 0)
        {
            return [];
        }

        return await db.WorkResultItems.AsNoTracking()
            .Where(i => i.VerifiedWorkResult.Status == VerifiedResultStatus.Valid
                && i.ActualProgress.Status == ActualProgressStatus.Valid
                && kpiIds.Contains(i.ActualProgress.TargetAssignment.KpiDefinitionId)
                && i.ActualProgress.ProgressDate >= period.StartDate
                && i.ActualProgress.ProgressDate <= period.EndDate
                && i.ActualProgress.TargetAssignment.Employee != null
                && i.ActualProgress.TargetAssignment.Employee.Status != EmployeeStatus.Terminated
                && subtree.Contains(i.ActualProgress.TargetAssignment.Employee.DepartmentId)
                && ((i.VerifiedWorkResult.Verification != null
                        && i.VerifiedWorkResult.Verification.ResultSubmission.Work.BusinessCampaignId != null)
                    || (i.VerifiedWorkResult.WorkTransaction != null
                        && i.VerifiedWorkResult.WorkTransaction.Work.BusinessCampaignId != null)))
            .GroupBy(i => i.ActualProgress.TargetAssignment.KpiDefinitionId)
            .Select(g => new { KpiDefinitionId = g.Key, Value = g.Sum(i => i.RawValue) })
            .ToDictionaryAsync(x => x.KpiDefinitionId, x => x.Value, cancellationToken);
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
                var entryCount = rows.Sum(r => r.Denominator ?? 1m);
                return entryCount == 0m ? null : Round(rows.Sum(r => r.Value) / entryCount);
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

    private Task<DepartmentHierarchy> LoadOrgAsync(CancellationToken cancellationToken) =>
        DepartmentHierarchy.LoadAsync(db.Departments, cancellationToken);

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
        DepartmentHierarchy org,
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
        DepartmentHierarchy org,
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
                || (string.Equals(p.Resource, OrganizationAccessCodes.Resource, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Action, OrganizationAccessCodes.Read, StringComparison.OrdinalIgnoreCase)))
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

    private DateOnly Today() => time.GetBusinessToday();

    private static bool IsOverdue(OpenWorkRow work, DateOnly today) => IsOverdue(work.Status, work.PlannedEndDate, today);

    private static bool IsOverdue(WorkStatus status, DateOnly? plannedEndDate, DateOnly today) =>
        status == WorkStatus.Overdue
        || (plannedEndDate.HasValue && plannedEndDate.Value < today);

    private static bool IsAttention(OpenWorkRow work, DateOnly today) =>
        work.Status == WorkStatus.Rejected || IsOverdue(work, today);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // ----- Private shapes -------------------------------------------------------------------

    private sealed record ManagerCandidate(
        Guid Id,
        string EmployeeCode,
        string FullName,
        Guid? ManagerId,
        string JobPositionTitle,
        Guid DepartmentId,
        string JobPositionCode);

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
        Guid AssigneeEmployeeId,
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

    private sealed record KpiRow(
        Guid KpiDefinitionId,
        string Code,
        string Name,
        string Unit,
        KpiDirection Direction,
        decimal TargetValue,
        decimal? ActualValue,
        decimal? AchievementPercent,
        decimal? CampaignActualValue);

    private sealed record MemberSelection(
        List<MemberRow> Rows,
        EvaluationPeriod? Period,
        EvaluationPeriod? ComparePeriod,
        DepartmentMemberSort? Sort);
}
