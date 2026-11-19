using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Validation;
using G54.BLL.Dtos.Department;
using G54.BLL.Dtos.TargetAllocation;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// Implementation of target quarter split, allocation, and publish (Phase 2, docs/api/TARGET_ALLOCATION_CONTRACT.md, 2.A).
/// </summary>
public sealed class TargetSetupService(
    IUnitOfWork uow, AppDbContext db, IPermissionService permissions, ITargetChangeLogService changeLog,
    INotifier notifier, IDepartmentService departmentService) : ITargetSetupService
{
    private const string AuditEntityName = "BranchYearTargetImport";

    /// <summary>Only SUM KPIs are split additively over quarters; every other KPI keeps its year value in each quarter.</summary>
    private static bool IsPeriodAdditive(KpiAggregationType aggregation) => aggregation == KpiAggregationType.Sum;

    /// <summary>RATIO and NO_AGGREGATION KPIs are one branch-wide value and are not split over quarters or departments.</summary>
    private static bool IsBranchWide(KpiAggregationType aggregation) =>
        aggregation is KpiAggregationType.Ratio or KpiAggregationType.NoAggregation;

    /// <summary>
    /// Only SUM KPIs are allocated to departments in step 3. AVG KPIs keep one branch value (step 3 shows them as
    /// "ngưỡng chung, không phân bổ"), so they are neither allocatable nor a publish blocker.
    /// </summary>
    private static bool IsAllocatedToDepartments(KpiAggregationType aggregation) => IsPeriodAdditive(aggregation);

    /// <summary>All 4 quarters are present and match the year: their sum for SUM KPIs, each value otherwise.</summary>
    private static bool QuartersMatch(KpiAggregationType aggregation, decimal yearValue, List<decimal> quarters) =>
        quarters.Count == 4 && (IsPeriodAdditive(aggregation) ? quarters.Sum() == yearValue : quarters.All(q => q == yearValue));

    /// <summary>
    /// Non-SUM KPIs have nothing to split: each branch quarter is the year value. Resets existing branch-quarter rows that
    /// hold another value (e.g. split under the old rule) and adds the missing DRAFT rows, which are returned; the caller saves.
    /// </summary>
    private async Task<List<TargetAssignment>> EnsureFixedQuartersAsync(
        Guid branchId,
        IEnumerable<TargetAssignment> branchYearRows,
        IReadOnlyList<EvaluationPeriod> quarterPeriods,
        CancellationToken cancellationToken)
    {
        var fixedRows = branchYearRows.Where(r => !IsPeriodAdditive(r.KpiDefinition.AggregationType)).ToList();
        var fixedIds = fixedRows.Select(r => r.Id).ToList();
        var existingRows = await db.TargetAssignments
            .Where(t => t.SplitDimension == TargetSplitDimension.Period
                && t.ParentAssignmentId != null
                && fixedIds.Contains(t.ParentAssignmentId.Value))
            .ToListAsync(cancellationToken);
        var existing = existingRows.Select(r => (r.ParentAssignmentId!.Value, r.EvaluationPeriodId)).ToHashSet();

        var added = new List<TargetAssignment>();
        var now = DateTimeOffset.UtcNow;
        foreach (var branchYearRow in fixedRows)
        {
            foreach (var row in existingRows.Where(r => r.ParentAssignmentId == branchYearRow.Id && r.TargetValue != branchYearRow.TargetValue))
            {
                row.TargetValue = branchYearRow.TargetValue;
            }

            foreach (var quarter in quarterPeriods.Where(q => !existing.Contains((branchYearRow.Id, q.Id))))
            {
                var row = new TargetAssignment
                {
                    Id = Guid.NewGuid(),
                    ParentAssignmentId = branchYearRow.Id,
                    DepartmentId = branchId,
                    KpiDefinitionId = branchYearRow.KpiDefinitionId,
                    EvaluationPeriodId = quarter.Id,
                    TargetValue = branchYearRow.TargetValue,
                    SplitDimension = TargetSplitDimension.Period,
                    Status = TargetAssignmentStatus.Draft,
                    Version = 1,
                    CreatedAt = now,
                    AssignedAt = now,
                    EffectiveFrom = quarter.StartDate
                };
                await uow.TargetAssignments.AddAsync(row, cancellationToken);
                added.Add(row);
            }
        }

        return added;
    }

    /// <summary>
    /// Loads and validates a branch-year target assignment; returns the validated assignment, branch ID, year, year period ID, and year period.
    /// Throws NotFoundException if not found or invalid, or ForbiddenException if user lacks access.
    /// </summary>
    private async Task<(TargetAssignment branchYear, Guid branchId, int year, Guid yearPeriodId, EvaluationPeriod yearPeriod)>
        LoadAndValidateBranchYearAsync(Guid branchYearId, Guid userId, CancellationToken cancellationToken)
    {
        var branchYear = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.EvaluationPeriod)
            .Include(t => t.KpiDefinition)
            .FirstOrDefaultAsync(t => t.Id == branchYearId, cancellationToken)
            ?? throw new NotFoundException("Branch-year target not found.");

        // Must be a branch-level (SplitDimension = null), year-period assignment with no parent
        if (branchYear.SplitDimension is not null || branchYear.ParentAssignmentId is not null
            || branchYear.EvaluationPeriod.PeriodType != PeriodType.Year)
        {
            throw new NotFoundException("Invalid branch-year target.");
        }

        if (branchYear.DepartmentId is null)
        {
            throw new NotFoundException("Branch-year target must have a department assigned.");
        }

        // Check department access
        await DepartmentAccessGuard.EnsureDepartmentAccessAsync(uow, permissions, userId, branchYear.DepartmentId, cancellationToken);

        var branchId = branchYear.DepartmentId.Value;
        var year = branchYear.EvaluationPeriod.StartDate.Year;
        var yearPeriodId = branchYear.EvaluationPeriodId;
        var yearPeriod = branchYear.EvaluationPeriod;

        return (branchYear, branchId, year, yearPeriodId, yearPeriod);
    }

    public async Task<QuarterSplitDto> GetQuartersAsync(Guid branchYearId, Guid userId, CancellationToken cancellationToken)
    {
        // 1. Load and validate the branch-year TargetAssignment
        var (branchYear, branchId, year, yearPeriodId, yearPeriod) = await LoadAndValidateBranchYearAsync(branchYearId, userId, cancellationToken);

        // 2. Load all branch-year rows for this branch+year with their KPI definitions (one query)
        var branchYearRows = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.KpiDefinition).ThenInclude(k => k.KpiGroup)
            .Where(t => t.DepartmentId == branchId
                && t.EvaluationPeriodId == yearPeriodId
                && t.ParentAssignmentId == null
                && t.SplitDimension == null)
            .OrderBy(t => t.KpiDefinition.KpiGroup.Code)
            .ThenBy(t => t.KpiDefinition.Code)
            .ToListAsync(cancellationToken);

        var branchYearById = branchYearRows.ToDictionary(r => r.Id);

        // 3. Load the 4 QUARTER periods for this year by date range (one query)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(
            PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken);

        var quartersByPeriodId = quarterPeriods.ToDictionary(p => p.Id);

        // 4. Load DRAFT/ACTIVE quarter child rows with SplitDimension = PERIOD for all branch-year rows (one query)
        var quarterChildRows = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.ParentAssignmentId != null
                && t.SplitDimension == TargetSplitDimension.Period
                && (t.Status == TargetAssignmentStatus.Draft || t.Status == TargetAssignmentStatus.Active)
                && t.ParentAssignmentId != null && branchYearById.Keys.Contains(t.ParentAssignmentId.Value))
            .ToListAsync(cancellationToken);

        // Index by parent ID and period ID
        var quartersByParentAndPeriod = quarterChildRows
            .GroupBy(t => (t.ParentAssignmentId!.Value, t.EvaluationPeriodId))
            .ToDictionary(g => g.Key, g => g.FirstOrDefault());

        // 5. Load previous year quarters ACTIVE rows for this branch (one query)
        var previousYearPeriod = await uow.EvaluationPeriods.GetYearAsync(year - 1, cancellationToken);
        var previousYearQuarters = new Dictionary<(Guid kpiId, Guid periodId), TargetAssignment>();
        var prevQByNum = new Dictionary<int, EvaluationPeriod>();
        var previousYearValueByKpi = new Dictionary<Guid, decimal>();
        var previousYearActualByKpi = new Dictionary<Guid, decimal[]>();
        if (previousYearPeriod is not null)
        {
            previousYearValueByKpi = await db.TargetAssignments
                .AsNoTracking()
                .Where(t => t.DepartmentId == branchId
                    && t.EvaluationPeriodId == previousYearPeriod.Id
                    && t.ParentAssignmentId == null
                    && t.SplitDimension == null
                    && t.Status == TargetAssignmentStatus.Active)
                .GroupBy(t => t.KpiDefinitionId)
                .Select(g => new { KpiId = g.Key, Value = g.OrderByDescending(t => t.Version).First().TargetValue })
                .ToDictionaryAsync(x => x.KpiId, x => x.Value, cancellationToken);

            var prevQuarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(
                PeriodType.Quarter, previousYearPeriod.StartDate, previousYearPeriod.EndDate, cancellationToken);
            prevQByNum = prevQuarterPeriods
                .Select((p, idx) => (Number: idx + 1, Period: p))
                .ToDictionary(x => x.Number, x => x.Period);
            var prevQuarterPeriodIds = new HashSet<Guid>(prevQuarterPeriods.Select(p => p.Id));

            var prevQuarterRows = await db.TargetAssignments
                .AsNoTracking()
                .Where(t => t.DepartmentId == branchId
                    && t.Status == TargetAssignmentStatus.Active
                    && t.SplitDimension == TargetSplitDimension.Period
                    && prevQuarterPeriodIds.Contains(t.EvaluationPeriodId))
                .ToListAsync(cancellationToken);

            previousYearQuarters = prevQuarterRows
                .GroupBy(t => (t.KpiDefinitionId, t.EvaluationPeriodId))
                .ToDictionary(g => g.Key, g => g.FirstOrDefault()!);

            // 5b. Load previous year VALID actual progress for this branch, summed per KPI and quarter.
            // Actuals sit on descendant TargetAssignments (department/team/employee, month/week/day splits), not on
            // the branch-year row itself, so walk the parent-assignment tree down from the branch's previous-year
            // roots (one query per level, ~5 levels) to collect every assignment id in scope before summing.
            var prevRootIds = await db.TargetAssignments
                .AsNoTracking()
                .Where(t => t.DepartmentId == branchId
                    && t.ParentAssignmentId == null
                    && t.EvaluationPeriodId == previousYearPeriod.Id)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            var prevTreeIds = new HashSet<Guid>(prevRootIds);
            var prevCurrentLevelIds = prevRootIds;
            while (prevCurrentLevelIds.Count > 0)
            {
                var nextLevelIds = await db.TargetAssignments
                    .AsNoTracking()
                    .Where(t => t.ParentAssignmentId != null && prevCurrentLevelIds.Contains(t.ParentAssignmentId.Value))
                    .Select(t => t.Id)
                    .ToListAsync(cancellationToken);
                if (nextLevelIds.Count == 0)
                {
                    break;
                }

                foreach (var id in nextLevelIds)
                {
                    prevTreeIds.Add(id);
                }

                prevCurrentLevelIds = nextLevelIds;
            }

            var prevActualRows = await db.ActualProgresses
                .AsNoTracking()
                .Where(a => a.Status == ActualProgressStatus.Valid
                    && prevTreeIds.Contains(a.TargetAssignmentId)
                    && a.ProgressDate >= previousYearPeriod.StartDate
                    && a.ProgressDate <= previousYearPeriod.EndDate)
                .Select(a => new { a.TargetAssignment.KpiDefinitionId, a.ProgressDate, a.Value })
                .ToListAsync(cancellationToken);

            foreach (var group in prevActualRows.GroupBy(x => x.KpiDefinitionId))
            {
                var sums = new decimal[4];
                foreach (var item in group)
                {
                    var quarterEntry = prevQByNum.FirstOrDefault(kv =>
                        item.ProgressDate >= kv.Value.StartDate && item.ProgressDate <= kv.Value.EndDate);
                    if (quarterEntry.Key is >= 1 and <= 4)
                    {
                        sums[quarterEntry.Key - 1] += item.Value;
                    }
                }

                previousYearActualByKpi[group.Key] = sums;
            }
        }

        // 6. Load file ratios from AuditLog for this branch+year
        var fileRatios = await LoadFileRatiosAsync(branchId, year, cancellationToken);

        // 7. Build the result - quarters indexed by quarter number (1-4) derived from date
        var quartersByNumber = quarterPeriods
            .Select((p, idx) => (Number: idx + 1, Period: p))
            .ToDictionary(x => x.Number, x => x.Period);

        var rows = new List<QuarterSplitRowDto>();
        foreach (var branchYearRow in branchYearRows)
        {
            var kpi = branchYearRow.KpiDefinition;
            var isRatio = !IsPeriodAdditive(kpi.AggregationType);

            decimal? q1 = null, q2 = null, q3 = null, q4 = null;

            if (isRatio)
            {
                // Non-SUM KPIs keep the year value in every quarter
                q1 = q2 = q3 = q4 = branchYearRow.TargetValue;
            }
            else
            {
                // Load actual quarter values if they exist - key by period ID
                if (quartersByNumber.TryGetValue(1, out var q1Period) && quartersByParentAndPeriod.TryGetValue((branchYearRow.Id, q1Period.Id), out var q1Row))
                    q1 = q1Row?.TargetValue;
                if (quartersByNumber.TryGetValue(2, out var q2Period) && quartersByParentAndPeriod.TryGetValue((branchYearRow.Id, q2Period.Id), out var q2Row))
                    q2 = q2Row?.TargetValue;
                if (quartersByNumber.TryGetValue(3, out var q3Period) && quartersByParentAndPeriod.TryGetValue((branchYearRow.Id, q3Period.Id), out var q3Row))
                    q3 = q3Row?.TargetValue;
                if (quartersByNumber.TryGetValue(4, out var q4Period) && quartersByParentAndPeriod.TryGetValue((branchYearRow.Id, q4Period.Id), out var q4Row))
                    q4 = q4Row?.TargetValue;
            }

            // Calculate sum and matching
            decimal sum = isRatio ? branchYearRow.TargetValue : (q1 ?? 0) + (q2 ?? 0) + (q3 ?? 0) + (q4 ?? 0);
            decimal diff = branchYearRow.TargetValue - sum;
            bool matched = diff == 0m;

            // Get previous year quarters if they exist - indexed by KPI and period ID
            QuarterPreviousYearDto? previousYear_ = null;
            if (previousYearPeriod is not null)
            {
                decimal prevQ1 = prevQByNum.TryGetValue(1, out var p1) && previousYearQuarters.TryGetValue((branchYearRow.KpiDefinitionId, p1.Id), out var r1) ? r1.TargetValue : 0m;
                decimal prevQ2 = prevQByNum.TryGetValue(2, out var p2) && previousYearQuarters.TryGetValue((branchYearRow.KpiDefinitionId, p2.Id), out var r2) ? r2.TargetValue : 0m;
                decimal prevQ3 = prevQByNum.TryGetValue(3, out var p3) && previousYearQuarters.TryGetValue((branchYearRow.KpiDefinitionId, p3.Id), out var r3) ? r3.TargetValue : 0m;
                decimal prevQ4 = prevQByNum.TryGetValue(4, out var p4) && previousYearQuarters.TryGetValue((branchYearRow.KpiDefinitionId, p4.Id), out var r4) ? r4.TargetValue : 0m;

                if (prevQ1 > 0 || prevQ2 > 0 || prevQ3 > 0 || prevQ4 > 0)
                {
                    previousYear_ = new QuarterPreviousYearDto(prevQ1, prevQ2, prevQ3, prevQ4);
                }
            }

            // Previous year ACTUAL results: only for non-ratio SUM-aggregation KPIs with at least one quarter > 0
            QuarterPreviousYearDto? previousYearActual = null;
            if (!isRatio && IsPeriodAdditive(kpi.AggregationType)
                && previousYearActualByKpi.TryGetValue(branchYearRow.KpiDefinitionId, out var actualSums)
                && (actualSums[0] > 0 || actualSums[1] > 0 || actualSums[2] > 0 || actualSums[3] > 0))
            {
                previousYearActual = new QuarterPreviousYearDto(actualSums[0], actualSums[1], actualSums[2], actualSums[3]);
            }

            rows.Add(new QuarterSplitRowDto(
                KpiId: branchYearRow.KpiDefinitionId,
                KpiCode: kpi.Code,
                KpiName: kpi.Name,
                Unit: kpi.Unit,
                GroupCode: kpi.KpiGroup.Code,
                GroupName: kpi.KpiGroup.Name,
                AggregationType: kpi.AggregationType.ToEnumString(),
                IsRatio: isRatio,
                YearValue: branchYearRow.TargetValue,
                Q1: q1,
                Q2: q2,
                Q3: q3,
                Q4: q4,
                PreviousYearValue: previousYearValueByKpi.TryGetValue(branchYearRow.KpiDefinitionId, out var previousYearValue) ? previousYearValue : null,
                PreviousYear: previousYear_,
                PreviousYearActual: previousYearActual,
                Sum: sum,
                Diff: diff,
                Matched: matched));
        }

        // Build quarters metadata
        var quarters = new List<QuarterDto>();
        for (int qNum = 1; qNum <= 4; qNum++)
        {
            if (quartersByNumber.TryGetValue(qNum, out var quarter))
            {
                bool locked = quarter.Status != PeriodStatus.Open || quarter.EndDate < today;
                quarters.Add(new QuarterDto(quarter.Id, qNum, locked));
            }
        }

        return new QuarterSplitDto(
            BranchYearId: branchYearId,
            Year: year,
            Status: branchYearRows.All(r => r.Status == TargetAssignmentStatus.Active) ? "ACTIVE" : "DRAFT",
            ImportedAt: branchYear.CreatedAt,
            FileRatios: fileRatios,
            Quarters: quarters,
            Rows: rows);
    }

    /// <summary>
    /// Counts eligible employees per (KPI, department) pair in one pass (fixed queries, no N+1).
    /// Returns Dictionary<(kpiId, deptId), headcount>.
    /// </summary>
    private async Task<Dictionary<(Guid kpiId, Guid deptId), int>>
        CountEligibleHeadcountBatchAsync(List<Guid> kpiIds, List<Guid> departmentIds, DateOnly asOf, CancellationToken cancellationToken)
    {
        var result = new Dictionary<(Guid, Guid), int>();

        if (kpiIds.Count == 0 || departmentIds.Count == 0)
        {
            return result;
        }

        // 1. Load all active employees in these departments
        var employees = await db.Employees
            .AsNoTracking()
            .Include(e => e.JobPosition)
            .Where(e => departmentIds.Contains(e.DepartmentId) && e.Status == EmployeeStatus.Active)
            .ToListAsync(cancellationToken);

        if (employees.Count == 0)
        {
            return result;
        }

        var employeesByDept = employees.GroupBy(e => e.DepartmentId).ToDictionary(g => g.Key, g => g.ToList());
        var jobPositionIds = employees.Select(e => e.JobPositionId).Distinct().ToList();

        // 2. Load ACTIVE evaluation profiles
        var activeProfiles = await db.EvaluationProfiles
            .AsNoTracking()
            .Include(p => p.EvaluationProfileItems)
            .Where(p => jobPositionIds.Contains(p.JobPositionId)
                && p.Status == EvaluationProfileStatus.Active
                && p.EffectiveFrom <= asOf
                && (p.EffectiveTo == null || p.EffectiveTo >= asOf))
            .ToListAsync(cancellationToken);

        // Build map of (jobPosition, kpi) pairs from profiles
        var kpisByJobPos = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var profile in activeProfiles)
        {
            foreach (var item in profile.EvaluationProfileItems)
            {
                if (item.KpiDefinitionId.HasValue && kpiIds.Contains(item.KpiDefinitionId.Value))
                {
                    if (!kpisByJobPos.TryGetValue(profile.JobPositionId, out var kpis))
                    {
                        kpis = new HashSet<Guid>();
                        kpisByJobPos[profile.JobPositionId] = kpis;
                    }
                    kpis.Add(item.KpiDefinitionId.Value);
                }
            }
        }

        // 3. For job positions without profiles, check WorkType fallback
        var jobPosWithoutProfile = jobPositionIds.Where(jp => !kpisByJobPos.ContainsKey(jp)).ToList();

        if (jobPosWithoutProfile.Count > 0)
        {
            var workTypeKpiLinks = await db.WorkTypeKpis
                .AsNoTracking()
                .Where(wk => kpiIds.Contains(wk.KpiDefinitionId)
                    && wk.EffectiveFrom <= asOf
                    && (wk.EffectiveTo == null || wk.EffectiveTo >= asOf))
                .ToListAsync(cancellationToken);

            if (workTypeKpiLinks.Count > 0)
            {
                var workTypeIds = workTypeKpiLinks.Select(wk => wk.WorkTypeId).ToHashSet();

                var jobPosWorkTypes = await db.JobPositionWorkTypes
                    .AsNoTracking()
                    .Where(jpwt => jobPosWithoutProfile.Contains(jpwt.JobPositionId)
                        && workTypeIds.Contains(jpwt.WorkTypeId))
                    .ToListAsync(cancellationToken);

                foreach (var jpwt in jobPosWorkTypes)
                {
                    foreach (var link in workTypeKpiLinks.Where(wk => wk.WorkTypeId == jpwt.WorkTypeId))
                    {
                        if (!kpisByJobPos.TryGetValue(jpwt.JobPositionId, out var kpis))
                        {
                            kpis = new HashSet<Guid>();
                            kpisByJobPos[jpwt.JobPositionId] = kpis;
                        }
                        kpis.Add(link.KpiDefinitionId);
                    }
                }
            }
        }

        // 4. Count eligible employees per (KPI, department)
        foreach (var (deptId, deptEmployees) in employeesByDept)
        {
            foreach (var emp in deptEmployees)
            {
                if (kpisByJobPos.TryGetValue(emp.JobPositionId, out var eligibleKpis))
                {
                    foreach (var kpiId in eligibleKpis)
                    {
                        var key = (kpiId, deptId);
                        result.TryGetValue(key, out var count);
                        result[key] = count + 1;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Counts eligible employees per department for a given KPI (delegates to batched version).
    /// </summary>
    private async Task<(Dictionary<Guid, int> HeadcountByDept, string MostCommonJobPositionName)>
        CountEligibleHeadcountAsync(List<Guid> departmentIds, Guid kpiId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var batchResult = await CountEligibleHeadcountBatchAsync(new List<Guid> { kpiId }, departmentIds, asOf, cancellationToken);

        var result = new Dictionary<Guid, int>();
        var jobPositionCounts = new Dictionary<Guid, (string Title, int Count)>();

        foreach (var ((_, deptId), count) in batchResult)
        {
            result[deptId] = count;
        }

        // Reload to get job position titles for most common calculation (only used for display)
        if (result.Count > 0)
        {
            var employees = await db.Employees
                .AsNoTracking()
                .Include(e => e.JobPosition)
                .Where(e => departmentIds.Contains(e.DepartmentId) && e.Status == EmployeeStatus.Active)
                .ToListAsync(cancellationToken);

            var jobPositionIds = employees.Select(e => e.JobPositionId).Distinct().ToList();
            var profiles = await db.EvaluationProfiles
                .AsNoTracking()
                .Include(p => p.EvaluationProfileItems)
                .Where(p => jobPositionIds.Contains(p.JobPositionId)
                    && p.Status == EvaluationProfileStatus.Active
                    && p.EffectiveFrom <= asOf
                    && (p.EffectiveTo == null || p.EffectiveTo >= asOf))
                .ToListAsync(cancellationToken);

            var eligibleJobPosIds = new HashSet<Guid>();
            foreach (var profile in profiles)
            {
                if (profile.EvaluationProfileItems.Any(i => i.KpiDefinitionId == kpiId))
                {
                    eligibleJobPosIds.Add(profile.JobPositionId);
                }
            }

            foreach (var emp in employees.Where(e => eligibleJobPosIds.Contains(e.JobPositionId)))
            {
                if (!jobPositionCounts.TryGetValue(emp.JobPositionId, out _))
                {
                    jobPositionCounts[emp.JobPositionId] = (emp.JobPosition.Title, 0);
                }
                var (title, cnt) = jobPositionCounts[emp.JobPositionId];
                jobPositionCounts[emp.JobPositionId] = (title, cnt + 1);
            }
        }

        var mostCommonJobPositionName = string.Empty;
        if (jobPositionCounts.Count > 0)
        {
            mostCommonJobPositionName = jobPositionCounts.Values.MaxBy(x => x.Count).Title;
        }

        return (result, mostCommonJobPositionName);
    }

    public async Task<QuarterSplitDto> SaveQuartersAsync(Guid branchYearId, SaveQuarterSplitRequest request, Guid userId, CancellationToken cancellationToken)
    {
        // 1. Load and validate branch-year
        var (branchYear, branchId, year, yearPeriodId, yearPeriod) = await LoadAndValidateBranchYearAsync(branchYearId, userId, cancellationToken);

        // 2. Load all branch-year rows for validation (one query)
        var branchYearRows = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.KpiDefinition)
            .Where(t => t.DepartmentId == branchId
                && t.EvaluationPeriodId == yearPeriodId
                && t.ParentAssignmentId == null
                && t.SplitDimension == null)
            .ToListAsync(cancellationToken);

        var branchYearById = branchYearRows.ToDictionary(r => r.Id);
        var kpisByIdInBranchYear = branchYearRows.ToDictionary(r => r.KpiDefinitionId);

        // 4. Validate request
        var requestKpiIds = request.Rows.Select(r => r.KpiId).ToList();

        // Check for duplicates
        if (requestKpiIds.Count != requestKpiIds.Distinct().Count())
        {
            throw new ValidationException("Duplicate KPI IDs in request.");
        }

        // Separate ratio and non-ratio KPIs
        var requestNonRatioKpis = new Dictionary<Guid, SaveQuarterSplitRowRequest>();
        var ratioKpisInBranchYear = new Dictionary<Guid, TargetAssignment>();

        foreach (var row in request.Rows)
        {
            if (!kpisByIdInBranchYear.TryGetValue(row.KpiId, out var branchYearRow))
            {
                throw new ValidationException($"KPI {row.KpiId} is not found in this branch-year.");
            }

            // A year can be part published: only the KPIs still in DRAFT may be edited here.
            if (branchYearRow.Status != TargetAssignmentStatus.Draft)
            {
                throw new ConflictException("Chỉ tiêu năm của chi nhánh không còn ở trạng thái nháp.", ErrorCodes.TargetInvalidState);
            }

            // Check values >= 0
            if (row.Q1 < 0 || row.Q2 < 0 || row.Q3 < 0 || row.Q4 < 0)
            {
                throw new ValidationException("Quarter values must be >= 0.");
            }

            // Non-SUM KPIs are not split: ignore the row, their quarters are auto-filled below
            if (!IsPeriodAdditive(branchYearRow.KpiDefinition.AggregationType))
            {
                ratioKpisInBranchYear[row.KpiId] = branchYearRow;
            }
            else
            {
                requestNonRatioKpis[row.KpiId] = row;
            }
        }

        // Collect all ratio KPIs in this branch-year
        foreach (var branchYearRow in branchYearRows)
        {
            if (!IsPeriodAdditive(branchYearRow.KpiDefinition.AggregationType))
            {
                if (!ratioKpisInBranchYear.ContainsKey(branchYearRow.KpiDefinitionId))
                {
                    ratioKpisInBranchYear[branchYearRow.KpiDefinitionId] = branchYearRow;
                }
            }
        }

        // 5. Rows are saved as a full set for the KPIs in the request, so each one must add up to the year exactly
        // (contract: 422 TARGET_QUARTER_SUM_MISMATCH, G12/R1).
        var quarterMismatches = new List<string>();
        foreach (var (kpiId, row) in requestNonRatioKpis)
        {
            var branchYearRow = kpisByIdInBranchYear[kpiId];
            var sum = row.Q1 + row.Q2 + row.Q3 + row.Q4;
            if (sum != branchYearRow.TargetValue)
            {
                quarterMismatches.Add(
                    $"KPI {branchYearRow.KpiDefinition.Code}: tổng 4 quý = {sum}, chỉ tiêu năm = {branchYearRow.TargetValue}.");
            }
        }

        if (quarterMismatches.Count > 0)
        {
            throw new BusinessRuleException(
                ErrorCodes.TargetQuarterSumMismatch,
                null,
                422,
                quarterMismatches);
        }

        // 6. Load quarter periods (one query)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(
            PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken);

        // 7. Load existing quarter child rows (one query, with tracking for updates)
        var existingChildRows = await db.TargetAssignments
            .Where(t => t.ParentAssignmentId != null
                && t.SplitDimension == TargetSplitDimension.Period
                && t.ParentAssignmentId != null && branchYearById.Keys.Contains(t.ParentAssignmentId.Value))
            .ToListAsync(cancellationToken);

        var childByParentAndPeriod = existingChildRows
            .GroupBy(t => (t.ParentAssignmentId!.Value, t.EvaluationPeriodId))
            .ToDictionary(g => g.Key, g => g.FirstOrDefault());

        // 8. Check locked periods and validate changes
        foreach (var (kpiId, row) in requestNonRatioKpis)
        {
            var branchYearRow = kpisByIdInBranchYear[kpiId];
            var quarterValues = new[] { row.Q1, row.Q2, row.Q3, row.Q4 };

            for (int i = 0; i < 4; i++)
            {
                var quarter = quarterPeriods[i];
                bool isLocked = quarter.Status != PeriodStatus.Open || quarter.EndDate < today;

                if (isLocked)
                {
                    // Check if value is different from stored
                    var key = (branchYearRow.Id, quarter.Id);
                    decimal? storedValue = childByParentAndPeriod.TryGetValue(key, out var child) ? child?.TargetValue : null;
                    decimal requestValue = quarterValues[i];

                    if ((storedValue ?? 0m) != requestValue)
                    {
                        throw new ConflictException($"Quý {i + 1} đã khóa, không thể thay đổi.", ErrorCodes.PeriodLocked);
                    }
                }
            }
        }

        // 9. Execute in transaction
        await uow.ExecuteInTransactionAsync(async ct =>
        {
            // Insert/update non-ratio KPI quarter rows
            foreach (var (kpiId, row) in requestNonRatioKpis)
            {
                var branchYearRow = kpisByIdInBranchYear[kpiId];
                var quarterValues = new[] { row.Q1, row.Q2, row.Q3, row.Q4 };

                for (int i = 0; i < 4; i++)
                {
                    var quarter = quarterPeriods[i];
                    var key = (branchYearRow.Id, quarter.Id);
                    var now = DateTimeOffset.UtcNow;

                    if (childByParentAndPeriod.TryGetValue(key, out var existingChild) && existingChild != null)
                    {
                        // Update
                        var oldValue = existingChild.TargetValue;
                        if (oldValue != quarterValues[i])
                        {
                            existingChild.TargetValue = quarterValues[i];
                            await changeLog.LogAsync(
                                userId, TargetChangeLogAction.Manual, tier: 1, kpiId: kpiId,
                                unit: branchYearRow.KpiDefinition.Unit, period: $"Q{i + 1}",
                                oldValue: oldValue, newValue: quarterValues[i], basis: "MANUAL",
                                departmentId: branchId, year: year, cancellationToken: ct);
                        }
                    }
                    else
                    {
                        // Create
                        var newChild = new TargetAssignment
                        {
                            Id = Guid.NewGuid(),
                            ParentAssignmentId = branchYearRow.Id,
                            DepartmentId = branchId,
                            KpiDefinitionId = kpiId,
                            EvaluationPeriodId = quarter.Id,
                            TargetValue = quarterValues[i],
                            SplitDimension = TargetSplitDimension.Period,
                            Status = TargetAssignmentStatus.Draft,
                            Version = 1,
                            CreatedAt = now,
                            AssignedAt = now,
                            EffectiveFrom = quarter.StartDate
                        };
                        await uow.TargetAssignments.AddAsync(newChild, ct);
                        await changeLog.LogAsync(
                            userId, TargetChangeLogAction.Manual, tier: 1, kpiId: kpiId,
                            unit: branchYearRow.KpiDefinition.Unit, period: $"Q{i + 1}",
                            oldValue: null, newValue: quarterValues[i], basis: "MANUAL",
                            departmentId: branchId, year: year, cancellationToken: ct);
                    }
                }
            }

            // Auto-fill non-SUM KPI quarter rows (all quarters = year value)
            await EnsureFixedQuartersAsync(branchId, ratioKpisInBranchYear.Values, quarterPeriods, ct);

            await uow.SaveChangesAsync(ct);
        }, cancellationToken);

        // 10. Return updated quarter split
        return await GetQuartersAsync(branchYearId, userId, cancellationToken);
    }

    public async Task<KpiAllocationDto> GetAllocationAsync(Guid branchYearId, Guid kpiId, Guid userId, CancellationToken cancellationToken)
    {
        // 1. Load and validate branch-year
        var (branchYear, branchId, year, yearPeriodId, yearPeriod) = await LoadAndValidateBranchYearAsync(branchYearId, userId, cancellationToken);

        // 2. Find the branch-year row for this KPI (NotFoundException if none)
        var branchYearKpiRow = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.KpiDefinition)
            .Where(t => t.Status == TargetAssignmentStatus.Draft || t.Status == TargetAssignmentStatus.Active)
            .FirstOrDefaultAsync(t => t.KpiDefinitionId == kpiId
                && t.DepartmentId == branchId
                && t.EvaluationPeriodId == yearPeriodId
                && t.ParentAssignmentId == null
                && t.SplitDimension == null,
                cancellationToken)
            ?? throw new NotFoundException($"KPI {kpiId} not found in branch-year target.");

        var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(
            PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken);

        return await BuildKpiAllocationAsync(branchYearKpiRow, branchId, year, yearPeriod, quarterPeriods, cancellationToken);
    }

    /// <summary>
    /// Get department allocation grids for every allocatable KPI of a branch-year: ratio/no-aggregation KPIs
    /// (branch-wide, not split to departments) are excluded, and KPIs whose quarter split is not yet matched to
    /// the year value are omitted (they have nothing stable to allocate against yet; they simply appear once
    /// their quarter split is saved). Ordered the same way as quarter-split rows (KPI group code, then KPI code).
    /// </summary>
    public async Task<IReadOnlyList<KpiAllocationDto>> GetAllAllocationsAsync(Guid branchYearId, Guid userId, CancellationToken cancellationToken)
    {
        // 1. Load and validate branch-year (one access check for every KPI)
        var (branchYear, branchId, year, yearPeriodId, yearPeriod) = await LoadAndValidateBranchYearAsync(branchYearId, userId, cancellationToken);

        // 2. Load all branch-year KPI rows, same ordering as GetQuartersAsync
        var branchYearRows = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.KpiDefinition).ThenInclude(k => k.KpiGroup)
            .Where(t => t.DepartmentId == branchId
                && t.EvaluationPeriodId == yearPeriodId
                && t.ParentAssignmentId == null
                && t.SplitDimension == null)
            .OrderBy(t => t.KpiDefinition.KpiGroup.Code)
            .ThenBy(t => t.KpiDefinition.Code)
            .ToListAsync(cancellationToken);

        var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(
            PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken);

        var results = new List<KpiAllocationDto>();
        foreach (var branchYearRow in branchYearRows)
        {
            // Ratio/no-aggregation KPIs are one branch-wide value and are never allocated to departments.
            if (!IsAllocatedToDepartments(branchYearRow.KpiDefinition.AggregationType))
            {
                continue;
            }

            // Quarter split not finished yet: omit until the branch quarters match the year value.
            if (!await IsQuarterSplitReadyAsync(branchYearRow, quarterPeriods, cancellationToken))
            {
                continue;
            }

            results.Add(await BuildKpiAllocationAsync(branchYearRow, branchId, year, yearPeriod, quarterPeriods, cancellationToken));
        }

        return results;
    }

    /// <summary>
    /// True when the branch quarters for this KPI's row are fully populated and match the year value
    /// (same rule as the 409 TARGET_QUARTERS_NOT_READY check in SaveAllocationAsync, but non-throwing).
    /// </summary>
    private async Task<bool> IsQuarterSplitReadyAsync(
        TargetAssignment branchYearKpiRow,
        IReadOnlyList<EvaluationPeriod> quarterPeriods,
        CancellationToken cancellationToken)
    {
        var aggregation = branchYearKpiRow.KpiDefinition.AggregationType;

        var branchQuarterRows = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.ParentAssignmentId == branchYearKpiRow.Id
                && t.SplitDimension == TargetSplitDimension.Period
                && t.KpiDefinitionId == branchYearKpiRow.KpiDefinitionId)
            .ToListAsync(cancellationToken);

        var branchQuarterByPeriod = branchQuarterRows.ToDictionary(r => r.EvaluationPeriodId, r => r.TargetValue);

        var branchQuarterValues = new List<decimal>();
        foreach (var quarter in quarterPeriods)
        {
            if (!IsPeriodAdditive(aggregation))
            {
                branchQuarterValues.Add(branchYearKpiRow.TargetValue);
            }
            else if (branchQuarterByPeriod.TryGetValue(quarter.Id, out var qValue))
            {
                branchQuarterValues.Add(qValue);
            }
            else
            {
                return false;
            }
        }

        return QuartersMatch(aggregation, branchYearKpiRow.TargetValue, branchQuarterValues);
    }

    /// <summary>
    /// Builds the per-KPI department allocation grid (headcount, year/quarter values, totals, remaining).
    /// Shared by GetAllocationAsync (single KPI) and GetAllAllocationsAsync (every allocatable KPI).
    /// </summary>
    private async Task<KpiAllocationDto> BuildKpiAllocationAsync(
        TargetAssignment branchYearKpiRow,
        Guid branchId,
        int year,
        EvaluationPeriod yearPeriod,
        IReadOnlyList<EvaluationPeriod> quarterPeriods,
        CancellationToken cancellationToken)
    {
        var kpi = branchYearKpiRow.KpiDefinition;
        var kpiId = branchYearKpiRow.KpiDefinitionId;

        // 3. Get branch values (year and quarters)
        var quartersByNumber = quarterPeriods
            .Select((p, idx) => (Number: idx + 1, Period: p))
            .ToDictionary(x => x.Number, x => x.Period);

        // Load branch quarter values
        var branchQuarterRows = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.ParentAssignmentId == branchYearKpiRow.Id
                && t.SplitDimension == TargetSplitDimension.Period
                && t.KpiDefinitionId == kpiId)
            .ToListAsync(cancellationToken);

        var branchQuarterByPeriod = branchQuarterRows
            .ToDictionary(r => r.EvaluationPeriodId, r => r.TargetValue);

        var branchQ1 = quartersByNumber.TryGetValue(1, out var q1Period) && branchQuarterByPeriod.TryGetValue(q1Period.Id, out var q1Val) ? q1Val : 0m;
        var branchQ2 = quartersByNumber.TryGetValue(2, out var q2Period) && branchQuarterByPeriod.TryGetValue(q2Period.Id, out var q2Val) ? q2Val : 0m;
        var branchQ3 = quartersByNumber.TryGetValue(3, out var q3Period) && branchQuarterByPeriod.TryGetValue(q3Period.Id, out var q3Val) ? q3Val : 0m;
        var branchQ4 = quartersByNumber.TryGetValue(4, out var q4Period) && branchQuarterByPeriod.TryGetValue(q4Period.Id, out var q4Val) ? q4Val : 0m;
        if (!IsPeriodAdditive(kpi.AggregationType))
        {
            // Non-SUM KPIs keep the year value in every quarter, whatever is stored
            branchQ1 = branchQ2 = branchQ3 = branchQ4 = branchYearKpiRow.TargetValue;
        }

        // 4. Get departments and headcount
        var departments = await db.Departments
            .AsNoTracking()
            .Where(d => d.ParentDepartmentId == branchId
                && d.LevelType == DepartmentLevelType.Department)
            .OrderBy(d => d.Code)
            .ToListAsync(cancellationToken);

        var departmentIds = departments.Select(d => d.Id).ToList();

        var yearStartDate = yearPeriod.StartDate;
        var (headcountByDept, mostCommonJobPositionName) = await CountEligibleHeadcountAsync(
            departmentIds, kpiId, yearStartDate, cancellationToken);

        // 5. Filter out departments with 0 headcount
        var eligibleDepartments = departments.Where(d => headcountByDept.TryGetValue(d.Id, out var hc) && hc > 0).ToList();
        var hiddenDepartments = new List<DepartmentAllocationMetadataDto>();

        foreach (var dept in departments)
        {
            if (!headcountByDept.TryGetValue(dept.Id, out var hc) || hc == 0)
            {
                hiddenDepartments.Add(new DepartmentAllocationMetadataDto(dept.Id, dept.Name, "no_headcount"));
            }
        }

        // 6. Load existing allocation for eligible departments
        var deptYearRows = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.ParentAssignmentId == branchYearKpiRow.Id
                && t.SplitDimension == TargetSplitDimension.Org
                && t.KpiDefinitionId == kpiId
                && t.Status != TargetAssignmentStatus.Superseded
                && eligibleDepartments.Select(d => d.Id).Contains(t.DepartmentId!.Value))
            .ToListAsync(cancellationToken);

        var deptYearByDeptId = deptYearRows.ToDictionary(r => r.DepartmentId!.Value, r => r);

        // Load department quarter rows
        var deptYearIds = deptYearRows.Select(r => r.Id).ToHashSet();
        var deptQuarterRows = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.SplitDimension == TargetSplitDimension.Period
                && t.KpiDefinitionId == kpiId
                && t.ParentAssignmentId != null && deptYearIds.Contains(t.ParentAssignmentId.Value))
            .ToListAsync(cancellationToken);

        var deptQuarterByDeptAndPeriod = deptQuarterRows
            .GroupBy(r => (r.ParentAssignmentId!.Value, r.EvaluationPeriodId))
            .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.TargetValue ?? 0m);

        // 7. Load previous year values
        var previousYearValuesByDept = new Dictionary<Guid, decimal>();
        var previousYearActualByDept = new Dictionary<Guid, decimal>();
        var previousYearPeriod = await uow.EvaluationPeriods.GetYearAsync(year - 1, cancellationToken);

        if (previousYearPeriod is not null)
        {
            var prevDeptYearRows = await db.TargetAssignments
                .AsNoTracking()
                .Where(t => t.ParentAssignmentId != null
                    && t.SplitDimension == TargetSplitDimension.Org
                    && t.KpiDefinitionId == kpiId
                    && t.Status == TargetAssignmentStatus.Active
                    && eligibleDepartments.Select(d => d.Id).Contains(t.DepartmentId!.Value)
                    && t.EvaluationPeriodId == previousYearPeriod.Id)
                .ToListAsync(cancellationToken);

            foreach (var row in prevDeptYearRows)
            {
                previousYearValuesByDept[row.DepartmentId!.Value] = row.TargetValue;
            }

            // Previous year ACTUAL results per department: real actuals sit on descendant assignments of the
            // department's previous-year row (quarter/employee splits), not on the row itself, so walk the
            // parent-assignment tree down from each department's previous-year root before summing.
            if (prevDeptYearRows.Count > 0)
            {
                var rootToDept = prevDeptYearRows.ToDictionary(r => r.Id, r => r.DepartmentId!.Value);
                var idToRootId = new Dictionary<Guid, Guid>(rootToDept.Keys.Select(id => new KeyValuePair<Guid, Guid>(id, id)));
                var currentLevelIds = rootToDept.Keys.ToList();

                while (currentLevelIds.Count > 0)
                {
                    var children = await db.TargetAssignments
                        .AsNoTracking()
                        .Where(t => t.ParentAssignmentId != null && currentLevelIds.Contains(t.ParentAssignmentId.Value))
                        .Select(t => new { t.Id, t.ParentAssignmentId })
                        .ToListAsync(cancellationToken);

                    if (children.Count == 0)
                    {
                        break;
                    }

                    var nextLevelIds = new List<Guid>();
                    foreach (var child in children)
                    {
                        if (idToRootId.TryGetValue(child.ParentAssignmentId!.Value, out var rootId))
                        {
                            idToRootId[child.Id] = rootId;
                            nextLevelIds.Add(child.Id);
                        }
                    }

                    currentLevelIds = nextLevelIds;
                }

                var treeIds = idToRootId.Keys.ToList();
                var actualRows = await db.ActualProgresses
                    .AsNoTracking()
                    .Where(a => a.Status == ActualProgressStatus.Valid
                        && treeIds.Contains(a.TargetAssignmentId)
                        && a.ProgressDate >= previousYearPeriod.StartDate
                        && a.ProgressDate <= previousYearPeriod.EndDate)
                    .Select(a => new { a.TargetAssignmentId, a.Value })
                    .ToListAsync(cancellationToken);

                foreach (var group in actualRows.GroupBy(a => idToRootId[a.TargetAssignmentId]))
                {
                    var deptId = rootToDept[group.Key];
                    previousYearActualByDept[deptId] = previousYearActualByDept.GetValueOrDefault(deptId) + group.Sum(x => x.Value);
                }
            }
        }

        // 8. Build department rows and calculate totals
        var departmentRows = new List<DepartmentAllocationRowDto>();
        decimal totalsYear = 0m, totalsQ1 = 0m, totalsQ2 = 0m, totalsQ3 = 0m, totalsQ4 = 0m;

        foreach (var dept in eligibleDepartments)
        {
            if (!headcountByDept.TryGetValue(dept.Id, out var headcount))
            {
                continue;
            }

            var deptYear = deptYearByDeptId.TryGetValue(dept.Id, out var deptYearRow) ? deptYearRow.TargetValue : 0m;
            var deptQ1 = quartersByNumber.TryGetValue(1, out var q1P) && deptYearByDeptId.TryGetValue(dept.Id, out var dy1)
                ? (deptQuarterByDeptAndPeriod.TryGetValue((dy1.Id, q1P.Id), out var q1v) ? q1v : 0m)
                : 0m;
            var deptQ2 = quartersByNumber.TryGetValue(2, out var q2P) && deptYearByDeptId.TryGetValue(dept.Id, out var dy2)
                ? (deptQuarterByDeptAndPeriod.TryGetValue((dy2.Id, q2P.Id), out var q2v) ? q2v : 0m)
                : 0m;
            var deptQ3 = quartersByNumber.TryGetValue(3, out var q3P) && deptYearByDeptId.TryGetValue(dept.Id, out var dy3)
                ? (deptQuarterByDeptAndPeriod.TryGetValue((dy3.Id, q3P.Id), out var q3v) ? q3v : 0m)
                : 0m;
            var deptQ4 = quartersByNumber.TryGetValue(4, out var q4P) && deptYearByDeptId.TryGetValue(dept.Id, out var dy4)
                ? (deptQuarterByDeptAndPeriod.TryGetValue((dy4.Id, q4P.Id), out var q4v) ? q4v : 0m)
                : 0m;

            decimal? prevYear = previousYearValuesByDept.TryGetValue(dept.Id, out var pv) && pv > 0 ? pv : null;
            decimal? prevYearActual = previousYearActualByDept.TryGetValue(dept.Id, out var pva) ? pva : null;

            // matched = quarters match the year value (sum for SUM KPIs, each quarter otherwise)
            var matched = deptYear > 0 && QuartersMatch(kpi.AggregationType, deptYear, [deptQ1, deptQ2, deptQ3, deptQ4]);

            departmentRows.Add(new DepartmentAllocationRowDto(
                DepartmentId: dept.Id,
                DepartmentName: dept.Name,
                Headcount: headcount,
                Year: deptYear,
                Q1: deptQ1,
                Q2: deptQ2,
                Q3: deptQ3,
                Q4: deptQ4,
                PreviousYear: prevYear,
                PreviousYearActual: prevYearActual,
                Matched: matched,
                DeptYearId: deptYearByDeptId.TryGetValue(dept.Id, out var dyForId) ? dyForId.Id : null));

            totalsYear += deptYear;
            totalsQ1 += deptQ1;
            totalsQ2 += deptQ2;
            totalsQ3 += deptQ3;
            totalsQ4 += deptQ4;
        }

        // 9. Calculate remaining values
        var remainingYear = branchYearKpiRow.TargetValue - totalsYear;
        var remainingQ1 = branchQ1 - totalsQ1;
        var remainingQ2 = branchQ2 - totalsQ2;
        var remainingQ3 = branchQ3 - totalsQ3;
        var remainingQ4 = branchQ4 - totalsQ4;

        return new KpiAllocationDto(
            KpiId: kpiId,
            KpiName: kpi.Name,
            Unit: kpi.Unit,
            WorkType: mostCommonJobPositionName,
            Branch: new BranchTargetValuesDto(branchYearKpiRow.TargetValue, branchQ1, branchQ2, branchQ3, branchQ4),
            Departments: departmentRows,
            Totals: new AllocationTotalsDto(totalsYear, totalsQ1, totalsQ2, totalsQ3, totalsQ4),
            Remaining: new AllocationRemainingDto(remainingYear, remainingQ1, remainingQ2, remainingQ3, remainingQ4),
            HiddenDepartments: hiddenDepartments);
    }

    public async Task<KpiAllocationDto> SaveAllocationAsync(Guid branchYearId, Guid kpiId, SaveKpiAllocationRequest request, Guid userId, CancellationToken cancellationToken)
    {
        // 1. Load and validate branch-year
        var (branchYear, branchId, year, yearPeriodId, yearPeriod) = await LoadAndValidateBranchYearAsync(branchYearId, userId, cancellationToken);

        // 2. Find the KPI branch-year row and check it's not a ratio
        var branchYearKpiRow = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.KpiDefinition)
            .FirstOrDefaultAsync(t => t.KpiDefinitionId == kpiId
                && t.DepartmentId == branchId
                && t.EvaluationPeriodId == yearPeriodId
                && t.ParentAssignmentId == null
                && t.SplitDimension == null,
                cancellationToken)
            ?? throw new NotFoundException($"KPI {kpiId} not found in branch-year target.");

        // A year can be part published: only a KPI still in DRAFT may be edited here.
        if (branchYearKpiRow.Status != TargetAssignmentStatus.Draft)
        {
            throw new ConflictException("Chỉ tiêu năm của chi nhánh không còn ở trạng thái nháp.", ErrorCodes.TargetInvalidState);
        }

        if (!IsAllocatedToDepartments(branchYearKpiRow.KpiDefinition.AggregationType))
        {
            throw new ValidationException("Ratio KPIs cannot be allocated (không phân bổ).");
        }

        // 4. Check that branch quarters for this KPI are matched and exist
        var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(
            PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken);

        var branchQuarterRows = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.ParentAssignmentId == branchYearKpiRow.Id
                && t.SplitDimension == TargetSplitDimension.Period
                && t.KpiDefinitionId == kpiId)
            .ToListAsync(cancellationToken);

        var branchQuarterByPeriod = branchQuarterRows.ToDictionary(r => r.EvaluationPeriodId, r => r.TargetValue);

        // Verify all 4 quarters exist and match the year value. Non-SUM KPIs need no split: each quarter is the year
        // value, and their rows are created or reset with the allocation below.
        var aggregation = branchYearKpiRow.KpiDefinition.AggregationType;
        var branchQuarterValues = new List<decimal>();
        foreach (var quarter in quarterPeriods)
        {
            if (!IsPeriodAdditive(aggregation))
            {
                branchQuarterValues.Add(branchYearKpiRow.TargetValue);
            }
            else if (branchQuarterByPeriod.TryGetValue(quarter.Id, out var qValue))
            {
                branchQuarterValues.Add(qValue);
            }
            else
            {
                throw new ConflictException("Chỉ tiêu này chưa chia đủ 4 quý.", ErrorCodes.TargetQuartersNotReady);
            }
        }

        if (!QuartersMatch(aggregation, branchYearKpiRow.TargetValue, branchQuarterValues))
        {
            throw new ConflictException("Tổng 4 quý chưa bằng chỉ tiêu năm.", ErrorCodes.TargetQuartersNotReady);
        }

        // 5. Get all eligible departments (headcount > 0)
        var departments = await db.Departments
            .AsNoTracking()
            .Where(d => d.ParentDepartmentId == branchId
                && d.LevelType == DepartmentLevelType.Department)
            .OrderBy(d => d.Code)
            .ToListAsync(cancellationToken);

        var departmentIds = departments.Select(d => d.Id).ToList();
        var yearStartDate = yearPeriod.StartDate;
        var (headcountByDept, _) = await CountEligibleHeadcountAsync(
            departmentIds, kpiId, yearStartDate, cancellationToken);

        var eligibleDeptIds = new HashSet<Guid>();
        foreach (var dept in departments)
        {
            if (headcountByDept.TryGetValue(dept.Id, out var hc) && hc > 0)
            {
                eligibleDeptIds.Add(dept.Id);
            }
        }

        // 6. Validate request
        var requestDeptIds = request.Departments.Select(d => d.DepartmentId).ToList();

        // Check for duplicates
        if (requestDeptIds.Count != requestDeptIds.Distinct().Count())
        {
            throw new ValidationException("Duplicate DepartmentId in request.");
        }

        // Check values >= 0 and validate all request departments are eligible
        foreach (var deptReq in request.Departments)
        {
            if (deptReq.Year < 0 || deptReq.Q1 < 0 || deptReq.Q2 < 0 || deptReq.Q3 < 0 || deptReq.Q4 < 0)
            {
                throw new ValidationException("All allocation values must be >= 0.");
            }

            if (!eligibleDeptIds.Contains(deptReq.DepartmentId))
            {
                throw new BusinessRuleException(
                    ErrorCodes.TargetDepartmentNotEligible,
                    $"Department {deptReq.DepartmentId} is not eligible for this KPI.",
                    422);
            }
        }

        // 7. Exact totals: the request is the full allocation for this KPI, so the department years must equal the
        // branch year and each department's quarters must equal its year (contract: 422, R1/BR-24). Whether the
        // department quarters also match each branch quarter is checked when publishing (G16 "Còn lại 0").
        var departmentNameById = departments.ToDictionary(d => d.Id, d => d.Name);
        var allocationMismatches = new List<string>();
        var deptQuarterMismatches = new List<string>();
        decimal requestedYearTotal = 0m;

        foreach (var deptReq in request.Departments)
        {
            requestedYearTotal += deptReq.Year;
            if (!QuartersMatch(aggregation, deptReq.Year, [deptReq.Q1, deptReq.Q2, deptReq.Q3, deptReq.Q4]))
            {
                var deptName = departmentNameById.GetValueOrDefault(deptReq.DepartmentId, deptReq.DepartmentId.ToString());
                deptQuarterMismatches.Add(IsPeriodAdditive(aggregation)
                    ? $"Phòng {deptName}: tổng 4 quý = {deptReq.Q1 + deptReq.Q2 + deptReq.Q3 + deptReq.Q4}, chỉ tiêu năm = {deptReq.Year}."
                    : $"Phòng {deptName}: mỗi quý phải bằng chỉ tiêu năm = {deptReq.Year}.");
            }
        }

        if (requestedYearTotal != branchYearKpiRow.TargetValue)
        {
            allocationMismatches.Add(
                $"Tổng chỉ tiêu các phòng = {requestedYearTotal}, chỉ tiêu chi nhánh = {branchYearKpiRow.TargetValue}.");
        }

        if (allocationMismatches.Count > 0)
        {
            throw new BusinessRuleException(
                ErrorCodes.TargetAllocationSumMismatch,
                null,
                422,
                allocationMismatches);
        }

        if (deptQuarterMismatches.Count > 0)
        {
            throw new BusinessRuleException(
                ErrorCodes.TargetDeptQuarterSumMismatch,
                null,
                422,
                deptQuarterMismatches);
        }

        // 8. Check locked quarters (same as SaveQuartersAsync)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var quartersByNumber = quarterPeriods
            .Select((p, idx) => (Number: idx + 1, Period: p))
            .ToDictionary(x => x.Number, x => x.Period);

        // Load existing dept-year and dept-quarter rows (one query with tracking for updates)
        var existingDeptYearRows = await db.TargetAssignments
            .Where(t => t.ParentAssignmentId == branchYearKpiRow.Id
                && t.SplitDimension == TargetSplitDimension.Org
                && t.KpiDefinitionId == kpiId
                && t.Status != TargetAssignmentStatus.Superseded)
            .ToListAsync(cancellationToken);

        var deptYearByDeptId = existingDeptYearRows.ToDictionary(r => r.DepartmentId!.Value, r => r);
        var deptYearIds = existingDeptYearRows.Select(r => r.Id).ToHashSet();

        var existingDeptQuarterRows = await db.TargetAssignments
            .Where(t => t.SplitDimension == TargetSplitDimension.Period
                && t.KpiDefinitionId == kpiId
                && t.ParentAssignmentId != null && deptYearIds.Contains(t.ParentAssignmentId.Value))
            .ToListAsync(cancellationToken);

        var deptQuarterByDeptAndPeriod = existingDeptQuarterRows
            .GroupBy(r => (r.ParentAssignmentId!.Value, r.EvaluationPeriodId))
            .ToDictionary(g => g.Key, g => g.FirstOrDefault());

        // The request is the full set: drop DRAFT rows for departments it no longer allocates this KPI to.
        var requestedDeptIds = request.Departments.Select(d => d.DepartmentId).ToHashSet();
        var staleDeptYearRows = existingDeptYearRows
            .Where(r => r.Status == TargetAssignmentStatus.Draft && !requestedDeptIds.Contains(r.DepartmentId!.Value))
            .ToList();
        var staleDeptYearIds = staleDeptYearRows.Select(r => r.Id).ToHashSet();
        var staleDeptQuarterRows = existingDeptQuarterRows
            .Where(r => r.ParentAssignmentId.HasValue && staleDeptYearIds.Contains(r.ParentAssignmentId.Value))
            .ToList();

        // Check for locked period changes
        foreach (var deptReq in request.Departments)
        {
            var quarterValues = new[] { deptReq.Q1, deptReq.Q2, deptReq.Q3, deptReq.Q4 };

            for (int i = 0; i < 4; i++)
            {
                var quarter = quartersByNumber[i + 1];
                bool isLocked = quarter.Status != PeriodStatus.Open || quarter.EndDate < today;

                if (isLocked)
                {
                    // Check if this department has an existing quarter value
                    if (deptYearByDeptId.TryGetValue(deptReq.DepartmentId, out var deptYear))
                    {
                        var key = (deptYear.Id, quarter.Id);
                        decimal? storedValue = deptQuarterByDeptAndPeriod.TryGetValue(key, out var child) ? child?.TargetValue : null;
                        decimal requestValue = quarterValues[i];

                        if ((storedValue ?? 0m) != requestValue)
                        {
                            throw new ConflictException($"Quý {i + 1} đã khóa, không thể thay đổi.", ErrorCodes.PeriodLocked);
                        }
                    }
                }
            }
        }

        // 9. Execute in transaction
        await uow.ExecuteInTransactionAsync(async ct =>
        {
            var now = DateTimeOffset.UtcNow;

            await EnsureFixedQuartersAsync(branchId, [branchYearKpiRow], quarterPeriods, ct);

            if (staleDeptQuarterRows.Count > 0)
            {
                db.TargetAssignments.RemoveRange(staleDeptQuarterRows);
            }

            if (staleDeptYearRows.Count > 0)
            {
                db.TargetAssignments.RemoveRange(staleDeptYearRows);
            }

            // Upsert dept-year rows for each department in request
            foreach (var deptReq in request.Departments)
            {
                TargetAssignment deptYearRow;

                if (deptYearByDeptId.TryGetValue(deptReq.DepartmentId, out var existing))
                {
                    // Update
                    deptYearRow = existing;
                    var oldYear = deptYearRow.TargetValue;
                    if (oldYear != deptReq.Year)
                    {
                        deptYearRow.TargetValue = deptReq.Year;
                        await changeLog.LogAsync(
                            userId, TargetChangeLogAction.Manual, tier: 2, kpiId: kpiId,
                            unit: branchYearKpiRow.KpiDefinition.Unit, period: "YEAR",
                            oldValue: oldYear, newValue: deptReq.Year, basis: "MANUAL",
                            departmentId: deptReq.DepartmentId, year: year, cancellationToken: ct);
                    }
                }
                else
                {
                    // Create
                    deptYearRow = new TargetAssignment
                    {
                        Id = Guid.NewGuid(),
                        ParentAssignmentId = branchYearKpiRow.Id,
                        DepartmentId = deptReq.DepartmentId,
                        KpiDefinitionId = kpiId,
                        EvaluationPeriodId = yearPeriodId,
                        TargetValue = deptReq.Year,
                        SplitDimension = TargetSplitDimension.Org,
                        Status = TargetAssignmentStatus.Draft,
                        Version = 1,
                        CreatedAt = now,
                        AssignedAt = now,
                        EffectiveFrom = yearStartDate
                    };
                    await uow.TargetAssignments.AddAsync(deptYearRow, ct);
                    deptYearByDeptId[deptReq.DepartmentId] = deptYearRow;
                    await changeLog.LogAsync(
                        userId, TargetChangeLogAction.Manual, tier: 2, kpiId: kpiId,
                        unit: branchYearKpiRow.KpiDefinition.Unit, period: "YEAR",
                        oldValue: null, newValue: deptReq.Year, basis: "MANUAL",
                        departmentId: deptReq.DepartmentId, year: year, cancellationToken: ct);
                }

                // Upsert dept-quarter rows for each quarter
                var quarterValues = new[] { deptReq.Q1, deptReq.Q2, deptReq.Q3, deptReq.Q4 };

                for (int i = 0; i < 4; i++)
                {
                    var quarter = quartersByNumber[i + 1];
                    var key = (deptYearRow.Id, quarter.Id);

                    if (deptQuarterByDeptAndPeriod.TryGetValue(key, out var existingQuarter) && existingQuarter != null)
                    {
                        // Update
                        existingQuarter.TargetValue = quarterValues[i];
                    }
                    else
                    {
                        // Create
                        var newQuarter = new TargetAssignment
                        {
                            Id = Guid.NewGuid(),
                            ParentAssignmentId = deptYearRow.Id,
                            DepartmentId = deptReq.DepartmentId,
                            KpiDefinitionId = kpiId,
                            EvaluationPeriodId = quarter.Id,
                            TargetValue = quarterValues[i],
                            SplitDimension = TargetSplitDimension.Period,
                            Status = TargetAssignmentStatus.Draft,
                            Version = 1,
                            CreatedAt = now,
                            AssignedAt = now,
                            EffectiveFrom = quarter.StartDate
                        };
                        await uow.TargetAssignments.AddAsync(newQuarter, ct);
                    }
                }
            }

            await uow.SaveChangesAsync(ct);
        }, cancellationToken);

        // 10. Return updated allocation
        return await GetAllocationAsync(branchYearId, kpiId, userId, cancellationToken);
    }

    public async Task<TargetSetupDto> GetSetupAsync(Guid branchId, int year, Guid userId, CancellationToken cancellationToken)
    {
        // 1. Validate branch exists and check access
        var branch = await db.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == branchId && d.LevelType == DepartmentLevelType.Branch, cancellationToken)
            ?? throw new NotFoundException($"Branch {branchId} not found or is not a Branch level.");

        await DepartmentAccessGuard.EnsureDepartmentAccessAsync(uow, permissions, userId, branchId, cancellationToken);

        // 2. Get year period
        var yearPeriod = await uow.EvaluationPeriods.GetYearAsync(year, cancellationToken);

        if (yearPeriod == null)
        {
            return new TargetSetupDto(
                BranchYearId: null,
                HasBranchYear: false,
                ImportedAt: null,
                RowCount: 0,
                Step: "IMPORT",
                Status: "NONE",
                Quarters: new TargetSetupQuartersMetadataDto(0, 0),
                Allocation: new TargetSetupAllocationMetadataDto(0, 0),
                CanPublish: false,
                Blockers: new List<TargetSetupKpiBlockerDto>()
            );
        }

        // 3. Load all top-level branch-year rows (ParentAssignmentId null, SplitDimension null, Draft or Active)
        var branchYearRows = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.KpiDefinition)
            .Where(t => t.DepartmentId == branchId
                && t.EvaluationPeriodId == yearPeriod.Id
                && t.ParentAssignmentId == null
                && t.SplitDimension == null
                && (t.Status == TargetAssignmentStatus.Draft || t.Status == TargetAssignmentStatus.Active))
            .ToListAsync(cancellationToken);

        int rowCount = branchYearRows.Count;
        bool hasBranchYear = rowCount > 0;

        // 4. Load ImportedAt from AuditLog (latest import for this branch+year)
        var auditLog = await db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityName == AuditEntityName && a.EntityId == $"{branchId}:{year}")
            .OrderByDescending(a => a.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

        var importedAt = auditLog?.OccurredAt;

        if (!hasBranchYear)
        {
            return new TargetSetupDto(
                BranchYearId: null,
                HasBranchYear: false,
                ImportedAt: null,
                RowCount: 0,
                Step: "IMPORT",
                Status: "NONE",
                Quarters: new TargetSetupQuartersMetadataDto(0, 0),
                Allocation: new TargetSetupAllocationMetadataDto(0, 0),
                CanPublish: false,
                Blockers: new List<TargetSetupKpiBlockerDto>()
            );
        }

        // 5. Determine Status (NONE / DRAFT / ACTIVE)
        var status = branchYearRows.All(r => r.Status == TargetAssignmentStatus.Active) ? "ACTIVE" : "DRAFT";

        // 6. Separate ratio and non-ratio KPIs
        // "Non-ratio" = allocated to departments (SUM and AVG); RATIO / NO_AGGREGATION stay branch-wide
        var nonRatioKpis = branchYearRows.Where(r => !IsBranchWide(r.KpiDefinition.AggregationType)).ToList();
        var ratioKpis = branchYearRows.Where(r => IsBranchWide(r.KpiDefinition.AggregationType)).ToList();

        int totalNonRatioKpis = nonRatioKpis.Count;
        // Step 3 (department allocation) covers SUM KPIs only; AVG KPIs stop at the quarter split.
        var allocatedKpis = nonRatioKpis.Where(r => IsAllocatedToDepartments(r.KpiDefinition.AggregationType)).ToList();

        // 7. Get quarter periods
        var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(
            PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken);

        var branchYearIds = branchYearRows.Select(r => r.Id).ToHashSet();

        // 8. Load PERIOD children (branch-quarter rows) for all branch-year rows (one query)
        var quarterChildRows = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.SplitDimension == TargetSplitDimension.Period
                && (t.Status == TargetAssignmentStatus.Draft || t.Status == TargetAssignmentStatus.Active)
                && t.ParentAssignmentId != null && branchYearIds.Contains(t.ParentAssignmentId.Value))
            .ToListAsync(cancellationToken);

        // Index quarterly children by parent and period
        var quartersByParentAndPeriod = quarterChildRows
            .GroupBy(t => (t.ParentAssignmentId!.Value, t.EvaluationPeriodId))
            .ToDictionary(g => g.Key, g => g.FirstOrDefault());

        // 9. Count quarters done: for each non-ratio KPI, check if all 4 quarters exist and sum to year
        var quarterDoneByKpi = new Dictionary<Guid, bool>();
        int quartersDone = 0;

        foreach (var kpiRow in nonRatioKpis)
        {
            var quarterValues = new List<decimal>();

            foreach (var quarterPeriod in quarterPeriods)
            {
                var key = (kpiRow.Id, quarterPeriod.Id);
                if (quartersByParentAndPeriod.TryGetValue(key, out var qRow) && qRow != null)
                {
                    quarterValues.Add(qRow.TargetValue);
                }
            }

            // SUM KPIs: all 4 quarters exist and add up to the year. Other KPIs have nothing to split.
            bool isDone = !IsPeriodAdditive(kpiRow.KpiDefinition.AggregationType)
                || QuartersMatch(kpiRow.KpiDefinition.AggregationType, kpiRow.TargetValue, quarterValues);
            quarterDoneByKpi[kpiRow.KpiDefinitionId] = isDone;
            if (isDone)
            {
                quartersDone++;
            }
        }

        // 10. Load ORG children (dept-year rows) and PERIOD grandchildren (dept-quarter rows) (one query each)
        var deptYearRows = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.SplitDimension == TargetSplitDimension.Org
                && (t.Status == TargetAssignmentStatus.Draft || t.Status == TargetAssignmentStatus.Active))
            .ToListAsync(cancellationToken);

        var filteredDeptYearRows = deptYearRows.Where(r => r.ParentAssignmentId.HasValue && branchYearIds.Contains(r.ParentAssignmentId.Value)).ToList();
        deptYearRows = filteredDeptYearRows;
        var deptYearIds = deptYearRows.Select(r => r.Id).ToHashSet();

        var deptQuarterRows = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.SplitDimension == TargetSplitDimension.Period)
            .ToListAsync(cancellationToken);

        var filteredDeptQuarterRows = deptQuarterRows.Where(r => r.ParentAssignmentId.HasValue && deptYearIds.Contains(r.ParentAssignmentId.Value)).ToList();
        deptQuarterRows = filteredDeptQuarterRows;

        var deptQuarterByDeptYearAndPeriod = deptQuarterRows
            .GroupBy(r => (r.ParentAssignmentId!.Value, r.EvaluationPeriodId))
            .ToDictionary(g => g.Key, g => g.FirstOrDefault());

        // 11. Get all departments as list
        var allDepartments = await db.Departments
            .AsNoTracking()
            .Where(d => d.ParentDepartmentId == branchId && d.LevelType == DepartmentLevelType.Department)
            .ToListAsync(cancellationToken);

        var allDeptIds = allDepartments.Select(d => d.Id).ToList();

        // 12. Get headcount for all non-ratio KPIs in one batch query
        var kpiIdsToCheck = nonRatioKpis.Select(k => k.KpiDefinitionId).ToList();
        var headcountBatch = await CountEligibleHeadcountBatchAsync(kpiIdsToCheck, allDeptIds, yearPeriod.StartDate, cancellationToken);

        // 13. Build map of eligible departments per KPI
        var eligibleDeptsByKpi = new Dictionary<Guid, List<Guid>>();
        foreach (var kpiId in kpiIdsToCheck)
        {
            var eligible = new List<Guid>();
            foreach (var deptId in allDeptIds)
            {
                if (headcountBatch.TryGetValue((kpiId, deptId), out var count) && count > 0)
                {
                    eligible.Add(deptId);
                }
            }
            eligibleDeptsByKpi[kpiId] = eligible;
        }

        // 14. Count allocation done: for each non-ratio KPI that has quarters done
        var allocationDoneByKpi = new Dictionary<Guid, bool>();
        var kpisWithNoEligibleDepts = new HashSet<Guid>();
        int allocationDone = 0;

        foreach (var kpiRow in allocatedKpis)
        {
            // Only check allocation for KPIs with quarters done
            if (!quarterDoneByKpi.GetValueOrDefault(kpiRow.KpiDefinitionId, false))
            {
                allocationDoneByKpi[kpiRow.KpiDefinitionId] = false;
                continue;
            }

            var eligibleDepts = eligibleDeptsByKpi.GetValueOrDefault(kpiRow.KpiDefinitionId, new List<Guid>());

            // KPI with NO eligible departments = blocker (allocation not done)
            if (eligibleDepts.Count == 0)
            {
                allocationDoneByKpi[kpiRow.KpiDefinitionId] = false;
                kpisWithNoEligibleDepts.Add(kpiRow.KpiDefinitionId);
                continue;
            }

            // Check if all eligible departments have complete allocations
            var kpiDeptYearRows = deptYearRows.Where(r => r.KpiDefinitionId == kpiRow.KpiDefinitionId).ToList();
            var kpiDeptYearByDeptId = kpiDeptYearRows.ToDictionary(r => r.DepartmentId!.Value, r => r);

            bool allocationComplete = true;

            // Check all eligible departments
            foreach (var deptId in eligibleDepts)
            {
                if (!kpiDeptYearByDeptId.TryGetValue(deptId, out var deptYear))
                {
                    allocationComplete = false;
                    break;
                }

                // Check dept-year's quarters match its value
                var deptQuarterValues = new List<decimal>();

                foreach (var quarterPeriod in quarterPeriods)
                {
                    var key = (deptYear.Id, quarterPeriod.Id);
                    if (deptQuarterByDeptYearAndPeriod.TryGetValue(key, out var dq) && dq != null)
                    {
                        deptQuarterValues.Add(dq.TargetValue);
                    }
                }

                if (!QuartersMatch(kpiRow.KpiDefinition.AggregationType, deptYear.TargetValue, deptQuarterValues))
                {
                    allocationComplete = false;
                    break;
                }
            }

            // Check that all dept-years sum to branch-year value
            if (allocationComplete)
            {
                decimal allDeptSum = kpiDeptYearRows.Sum(r => r.TargetValue);
                allocationComplete = allDeptSum == kpiRow.TargetValue;
            }

            // ...and that each branch quarter is matched by the department quarters (G16 "Còn lại 0")
            if (allocationComplete)
            {
                foreach (var quarterPeriod in quarterPeriods)
                {
                    var branchQuarterValue = !IsPeriodAdditive(kpiRow.KpiDefinition.AggregationType)
                        ? kpiRow.TargetValue
                        : quartersByParentAndPeriod.TryGetValue((kpiRow.Id, quarterPeriod.Id), out var branchQuarter) && branchQuarter != null
                            ? branchQuarter.TargetValue
                            : 0m;
                    var deptQuarterTotal = kpiDeptYearRows.Sum(r =>
                        deptQuarterByDeptYearAndPeriod.TryGetValue((r.Id, quarterPeriod.Id), out var deptQuarter) && deptQuarter != null
                            ? deptQuarter.TargetValue
                            : 0m);

                    if (deptQuarterTotal != branchQuarterValue)
                    {
                        allocationComplete = false;
                        break;
                    }
                }
            }

            allocationDoneByKpi[kpiRow.KpiDefinitionId] = allocationComplete;
            if (allocationComplete)
            {
                allocationDone++;
            }
        }

        // 13. Build blockers (KPIs not fully done)
        var blockers = new List<TargetSetupKpiBlockerDto>();
        foreach (var kpiRow in nonRatioKpis)
        {
            bool quartersDoneForKpi = quarterDoneByKpi.GetValueOrDefault(kpiRow.KpiDefinitionId, false);
            bool allocationDoneForKpi = !IsAllocatedToDepartments(kpiRow.KpiDefinition.AggregationType)
                || allocationDoneByKpi.GetValueOrDefault(kpiRow.KpiDefinitionId, false);

            if (!quartersDoneForKpi || !allocationDoneForKpi)
            {
                blockers.Add(new TargetSetupKpiBlockerDto(
                    KpiId: kpiRow.KpiDefinitionId,
                    KpiCode: kpiRow.KpiDefinition.Code,
                    KpiName: kpiRow.KpiDefinition.Name
                ));
            }
        }

        // 14. Determine Step
        string step;
        if (status == "ACTIVE")
        {
            step = "PUBLISHED";
        }
        else if (totalNonRatioKpis == 0)
        {
            step = "PUBLISH";  // No non-ratio KPIs to split/allocate
        }
        else if (quartersDone < totalNonRatioKpis)
        {
            step = "QUARTER";
        }
        else if (allocationDone < allocatedKpis.Count)
        {
            step = "ALLOCATE";
        }
        else
        {
            step = "PUBLISH";
        }

        bool canPublish = step == "PUBLISH";

        return new TargetSetupDto(
            BranchYearId: branchYearRows[0].Id,
            HasBranchYear: true,
            ImportedAt: importedAt,
            RowCount: rowCount,
            Step: step,
            Status: status,
            Quarters: new TargetSetupQuartersMetadataDto(quartersDone, totalNonRatioKpis),
            Allocation: new TargetSetupAllocationMetadataDto(allocationDone, allocatedKpis.Count),
            CanPublish: canPublish,
            Blockers: blockers
        );
    }

    public async Task<PublishResultDto> PublishAsync(Guid branchYearId, Guid userId, CancellationToken cancellationToken)
    {
        // 1. Load and validate branch-year
        var (branchYear, branchId, year, yearPeriodId, yearPeriod) = await LoadAndValidateBranchYearAsync(branchYearId, userId, cancellationToken);

        // 2. Check not already Active (any Draft means we can proceed; if all Active, reject)
        var allBranchYearRows = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.DepartmentId == branchId
                && t.EvaluationPeriodId == yearPeriodId
                && t.ParentAssignmentId == null
                && t.SplitDimension == null)
            .ToListAsync(cancellationToken);

        if (allBranchYearRows.Count > 0 && allBranchYearRows.All(r => r.Status == TargetAssignmentStatus.Active))
        {
            throw new ConflictException("Chỉ tiêu năm của chi nhánh đã được phát hành.", ErrorCodes.TargetInvalidState);
        }

        // 3. Recompute H7 state to check CanPublish
        var setupState = await GetSetupAsync(branchId, year, userId, cancellationToken);

        if (!setupState.CanPublish)
        {
            // Build error message listing blockers
            var blockerNames = string.Join(", ", setupState.Blockers.Select(b => b.KpiName));
            var message = $"Cannot publish: incomplete KPIs: {blockerNames}";

            throw new BusinessRuleException(
                ErrorCodes.TargetPublishBlocked,
                message,
                statusCode: 422);
        }

        // 4. In ONE transaction: set all rows to Active and ApprovedAt = now
        await uow.ExecuteInTransactionAsync(async ct =>
        {
            var now = DateTimeOffset.UtcNow;

            // Load all tracked branch-year rows and their children
            var branchYearRowsTracked = await db.TargetAssignments
                .Include(t => t.KpiDefinition)
                .Where(t => t.DepartmentId == branchId
                    && t.EvaluationPeriodId == yearPeriodId
                    && t.ParentAssignmentId == null
                    && t.SplitDimension == null)
                .ToListAsync(ct);

            var branchYearIds = branchYearRowsTracked.Select(r => r.Id).ToHashSet();

            // Get all PERIOD children (branch-quarter rows)
            var periodChildren = await db.TargetAssignments
                .Where(t => t.SplitDimension == TargetSplitDimension.Period
                    && t.ParentAssignmentId != null
                    && branchYearIds.Contains(t.ParentAssignmentId.Value))
                .ToListAsync(ct);

            // Non-SUM KPIs never split by the user still get their 4 branch-quarter rows (year value each)
            var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(
                PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, ct);
            periodChildren.AddRange(await EnsureFixedQuartersAsync(branchId, branchYearRowsTracked, quarterPeriods, ct));

            // Get all ORG children (dept-year rows)
            var orgChildren = await db.TargetAssignments
                .Where(t => t.SplitDimension == TargetSplitDimension.Org
                    && t.ParentAssignmentId != null
                    && branchYearIds.Contains(t.ParentAssignmentId.Value))
                .ToListAsync(ct);

            var deptYearIds = orgChildren.Select(r => r.Id).ToHashSet();

            // Get all PERIOD grandchildren (dept-quarter rows)
            var periodGrandchildren = await db.TargetAssignments
                .Where(t => t.SplitDimension == TargetSplitDimension.Period
                    && t.ParentAssignmentId != null
                    && deptYearIds.Contains(t.ParentAssignmentId.Value))
                .ToListAsync(ct);

            // Set all to Active and ApprovedAt
            foreach (var row in branchYearRowsTracked)
            {
                row.Status = TargetAssignmentStatus.Active;
                row.ApprovedAt = now;
            }

            foreach (var row in periodChildren)
            {
                row.Status = TargetAssignmentStatus.Active;
                row.ApprovedAt = now;
            }

            foreach (var row in orgChildren)
            {
                row.Status = TargetAssignmentStatus.Active;
                row.ApprovedAt = now;
            }

            foreach (var row in periodGrandchildren)
            {
                row.Status = TargetAssignmentStatus.Active;
                row.ApprovedAt = now;
            }

            await uow.SaveChangesAsync(ct);

            // 5. Add AuditLog entry
            var branchYearCount = branchYearRowsTracked.Count;
            var quarterCount = periodChildren.Count;
            var deptYearCount = orgChildren.Count;
            var deptQuarterCount = periodGrandchildren.Count;

            var newValuesJson = JsonSerializer.Serialize(new
            {
                branchYearCount,
                quarterCount,
                deptYearCount,
                deptQuarterCount
            });

            var auditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                EntityName = "BranchYearTargetPublish",
                EntityId = $"{branchId}:{year}",
                Action = "Publish",
                NewValues = newValuesJson,
                ActorId = userId,
                OccurredAt = now
            };

            db.AuditLogs.Add(auditLog);
            await uow.SaveChangesAsync(ct);

            // D10/D20: a PUBLISH change-log entry per KPI (tier 1) and per department-year row (tier 2), plus a
            // notification to each covered department's manager.
            var kpiUnitById = branchYearRowsTracked.ToDictionary(r => r.KpiDefinitionId, r => r.KpiDefinition.Unit);
            foreach (var row in branchYearRowsTracked)
            {
                await changeLog.LogAsync(
                    userId, TargetChangeLogAction.Publish, tier: 1, kpiId: row.KpiDefinitionId,
                    unit: row.KpiDefinition.Unit, period: "YEAR", oldValue: null, newValue: row.TargetValue,
                    basis: "MANUAL", departmentId: branchId, year: year, cancellationToken: ct);
            }

            foreach (var row in orgChildren)
            {
                await changeLog.LogAsync(
                    userId, TargetChangeLogAction.Publish, tier: 2, kpiId: row.KpiDefinitionId,
                    unit: kpiUnitById.GetValueOrDefault(row.KpiDefinitionId, "?"), period: "YEAR",
                    oldValue: null, newValue: row.TargetValue, basis: "MANUAL",
                    departmentId: row.DepartmentId, year: year, cancellationToken: ct);
            }

            var coveredDeptIds = orgChildren.Select(r => r.DepartmentId!.Value).Distinct().ToList();
            var deptNotifyDrafts = new List<NotificationDraft>();
            foreach (var deptId in coveredDeptIds)
            {
                try
                {
                    var detail = await departmentService.GetDepartmentAsync(userId, deptId, ct);
                    if (detail.Manager is not null)
                    {
                        deptNotifyDrafts.Add(new NotificationDraft(
                            detail.Manager.Id,
                            NotificationType.TargetRevised,
                            "Chỉ tiêu phòng ban đã được phát hành",
                            $"Chỉ tiêu KPI năm {year} của phòng {detail.Name} đã được phát hành. Vui lòng phân bổ chỉ tiêu nhân viên.",
                            NotificationSeverity.Important,
                            NotificationRelatedType.Target,
                            null));
                    }
                }
                catch (Exception ex) when (ex is NotFoundException or ForbiddenException)
                {
                    // Department resolution is best-effort for this notification; never block the publish on it.
                }
            }

            if (deptNotifyDrafts.Count > 0)
            {
                await notifier.TryPublishAsync(deptNotifyDrafts, ct);
            }
        }, cancellationToken);

        // 6. Load updated counts and return result
        var now2 = DateTimeOffset.UtcNow;
        var finalBranchYearRows = await db.TargetAssignments
            .Where(t => t.DepartmentId == branchId
                && t.EvaluationPeriodId == yearPeriodId
                && t.ParentAssignmentId == null
                && t.SplitDimension == null
                && t.Status == TargetAssignmentStatus.Active)
            .ToListAsync(cancellationToken);

        var finalBranchYearIds = finalBranchYearRows.Select(r => r.Id).ToHashSet();

        var finalPeriodChildren = await db.TargetAssignments
            .Where(t => t.ParentAssignmentId != null && finalBranchYearIds.Contains(t.ParentAssignmentId.Value)
                && t.SplitDimension == TargetSplitDimension.Period
                && t.Status == TargetAssignmentStatus.Active)
            .ToListAsync(cancellationToken);

        var finalOrgChildren = await db.TargetAssignments
            .Where(t => t.ParentAssignmentId != null && finalBranchYearIds.Contains(t.ParentAssignmentId.Value)
                && t.SplitDimension == TargetSplitDimension.Org
                && t.Status == TargetAssignmentStatus.Active)
            .ToListAsync(cancellationToken);

        var finalDeptYearIds = finalOrgChildren.Select(r => r.Id).ToHashSet();

        var finalPeriodGrandchildren = await db.TargetAssignments
            .Where(t => t.ParentAssignmentId != null && finalDeptYearIds.Contains(t.ParentAssignmentId.Value)
                && t.SplitDimension == TargetSplitDimension.Period
                && t.Status == TargetAssignmentStatus.Active)
            .ToListAsync(cancellationToken);

        return new PublishResultDto(
            PublishedAt: now2,
            BranchYearCount: finalBranchYearRows.Count,
            QuarterCount: finalPeriodChildren.Count,
            DepartmentYearCount: finalOrgChildren.Count,
            DepartmentQuarterCount: finalPeriodGrandchildren.Count
        );
    }

    private async Task<QuarterRatiosDto?> LoadFileRatiosAsync(Guid branchId, int year, CancellationToken cancellationToken)
    {
        var auditLog = await db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityName == AuditEntityName && a.EntityId == $"{branchId}:{year}")
            .OrderByDescending(a => a.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (auditLog?.NewValues is null)
        {
            return null;
        }

        try
        {
            var doc = JsonDocument.Parse(auditLog.NewValues);
            if (doc.RootElement.TryGetProperty("quarterRatios", out var ratiosElement))
            {
                // The reader stores the quarters as Q1..Q4; older audit rows may use lower case.
                decimal? GetRatio(string key)
                {
                    foreach (var property in ratiosElement.EnumerateObject())
                    {
                        if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                        {
                            return property.Value.GetDecimal();
                        }
                    }

                    return null;
                }

                var q1 = GetRatio("q1");
                var q2 = GetRatio("q2");
                var q3 = GetRatio("q3");
                var q4 = GetRatio("q4");

                if (q1.HasValue && q2.HasValue && q3.HasValue && q4.HasValue)
                {
                    return new QuarterRatiosDto(q1.Value, q2.Value, q3.Value, q4.Value);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // A malformed legacy audit entry only loses the "theo tỷ lệ file" prefill, not the whole screen.
            return null;
        }

        return null;
    }

    // =========================================================================
    // Tier 3: department -> employee allocation (docs/api/TARGET_ALLOCATION_CONTRACT.md, Phase 3)
    // =========================================================================

    /// <summary>
    /// Loads and validates a department-year KPI row (the Tier-3 anchor: pins department + KPI + year).
    /// Throws NotFoundException if not found/invalid, or ForbiddenException if the user lacks department access.
    /// </summary>
    private async Task<(TargetAssignment deptYear, Guid departmentId, int year, Guid yearPeriodId, EvaluationPeriod yearPeriod)>
        LoadAndValidateDeptYearAsync(Guid deptYearId, Guid userId, CancellationToken cancellationToken)
    {
        var deptYear = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.KpiDefinition)
            .Include(t => t.EvaluationPeriod)
            .FirstOrDefaultAsync(t => t.Id == deptYearId, cancellationToken)
            ?? throw new NotFoundException("Department-year target not found.");

        // Must be a department-level (SplitDimension = ORG), year-period assignment with a parent (branch-year row)
        // and no employee (an employee-year row under it has EmployeeId set, not this row itself).
        if (deptYear.SplitDimension != TargetSplitDimension.Org || deptYear.ParentAssignmentId is null
            || deptYear.EmployeeId is not null || deptYear.EvaluationPeriod.PeriodType != PeriodType.Year)
        {
            throw new NotFoundException("Invalid department-year target.");
        }

        if (deptYear.DepartmentId is null)
        {
            throw new NotFoundException("Department-year target must have a department assigned.");
        }

        await DepartmentAccessGuard.EnsureDepartmentAccessAsync(uow, permissions, userId, deptYear.DepartmentId, cancellationToken);

        return (deptYear, deptYear.DepartmentId.Value, deptYear.EvaluationPeriod.StartDate.Year, deptYear.EvaluationPeriodId, deptYear.EvaluationPeriod);
    }

    /// <summary>
    /// Splits a department's active employees into those eligible for a KPI (hold a responsible position, via their
    /// active evaluation profile, falling back to work-type when the position has no profile item for this KPI) and
    /// those who are not (locked, D7/BR rule: "only employees holding a responsible position receive targets").
    /// </summary>
    private async Task<(List<Employee> Eligible, List<Employee> Ineligible, string? MostCommonJobPositionName)>
        GetEligibleEmployeesAsync(Guid departmentId, Guid kpiId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var employees = await db.Employees
            .AsNoTracking()
            .Include(e => e.JobPosition)
            .Where(e => e.DepartmentId == departmentId && e.Status == EmployeeStatus.Active)
            .OrderBy(e => e.EmployeeCode)
            .ToListAsync(cancellationToken);

        if (employees.Count == 0)
        {
            return (new List<Employee>(), new List<Employee>(), null);
        }

        var jobPositionIds = employees.Select(e => e.JobPositionId).Distinct().ToList();

        var eligibleJobPositionIds = new HashSet<Guid>();

        var activeProfiles = await db.EvaluationProfiles
            .AsNoTracking()
            .Include(p => p.EvaluationProfileItems)
            .Where(p => jobPositionIds.Contains(p.JobPositionId)
                && p.Status == EvaluationProfileStatus.Active
                && p.EffectiveFrom <= asOf
                && (p.EffectiveTo == null || p.EffectiveTo >= asOf))
            .ToListAsync(cancellationToken);

        var jobPosWithProfile = new HashSet<Guid>();
        foreach (var profile in activeProfiles)
        {
            jobPosWithProfile.Add(profile.JobPositionId);
            if (profile.EvaluationProfileItems.Any(i => i.KpiDefinitionId == kpiId))
            {
                eligibleJobPositionIds.Add(profile.JobPositionId);
            }
        }

        // Job positions without an active profile fall back to the work-type link (same rule as department headcount).
        var jobPosWithoutProfile = jobPositionIds.Where(jp => !jobPosWithProfile.Contains(jp)).ToList();
        if (jobPosWithoutProfile.Count > 0)
        {
            var workTypeKpiLinks = await db.WorkTypeKpis
                .AsNoTracking()
                .Where(wk => wk.KpiDefinitionId == kpiId
                    && wk.EffectiveFrom <= asOf
                    && (wk.EffectiveTo == null || wk.EffectiveTo >= asOf))
                .Select(wk => wk.WorkTypeId)
                .ToListAsync(cancellationToken);

            if (workTypeKpiLinks.Count > 0)
            {
                var matchingJobPositions = await db.JobPositionWorkTypes
                    .AsNoTracking()
                    .Where(jpwt => jobPosWithoutProfile.Contains(jpwt.JobPositionId) && workTypeKpiLinks.Contains(jpwt.WorkTypeId))
                    .Select(jpwt => jpwt.JobPositionId)
                    .ToListAsync(cancellationToken);

                foreach (var jp in matchingJobPositions)
                {
                    eligibleJobPositionIds.Add(jp);
                }
            }
        }

        var eligible = employees.Where(e => eligibleJobPositionIds.Contains(e.JobPositionId)).ToList();
        var ineligible = employees.Where(e => !eligibleJobPositionIds.Contains(e.JobPositionId)).ToList();
        var mostCommonJobPositionName = eligible
            .GroupBy(e => e.JobPosition.Title)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        return (eligible, ineligible, mostCommonJobPositionName);
    }

    public async Task<EmployeeAllocationDto> GetEmployeeAllocationAsync(Guid deptYearId, Guid userId, CancellationToken cancellationToken)
    {
        var (deptYear, departmentId, year, yearPeriodId, yearPeriod) = await LoadAndValidateDeptYearAsync(deptYearId, userId, cancellationToken);

        var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(
            PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken);

        return await BuildEmployeeAllocationAsync(deptYear, departmentId, year, yearPeriod, quarterPeriods, cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeAllocationDto>> GetAllEmployeeAllocationsAsync(Guid deptYearId, Guid userId, CancellationToken cancellationToken)
    {
        var (anchorDeptYear, departmentId, year, yearPeriodId, yearPeriod) = await LoadAndValidateDeptYearAsync(deptYearId, userId, cancellationToken);

        var deptYearRows = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.KpiDefinition).ThenInclude(k => k.KpiGroup)
            .Where(t => t.DepartmentId == departmentId
                && t.EvaluationPeriodId == yearPeriodId
                && t.SplitDimension == TargetSplitDimension.Org
                && t.EmployeeId == null
                && t.ParentAssignmentId != null
                && t.Status != TargetAssignmentStatus.Superseded)
            .OrderBy(t => t.KpiDefinition.KpiGroup.Code)
            .ThenBy(t => t.KpiDefinition.Code)
            .ToListAsync(cancellationToken);

        var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(
            PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken);

        var results = new List<EmployeeAllocationDto>();
        foreach (var row in deptYearRows)
        {
            results.Add(await BuildEmployeeAllocationAsync(row, departmentId, year, yearPeriod, quarterPeriods, cancellationToken));
        }

        return results;
    }

    /// <summary>
    /// Builds the per-KPI employee allocation grid (one tier below BuildKpiAllocationAsync's department grid).
    /// Shared by GetEmployeeAllocationAsync and GetAllEmployeeAllocationsAsync.
    /// </summary>
    private async Task<EmployeeAllocationDto> BuildEmployeeAllocationAsync(
        TargetAssignment deptYearRow,
        Guid departmentId,
        int year,
        EvaluationPeriod yearPeriod,
        IReadOnlyList<EvaluationPeriod> quarterPeriods,
        CancellationToken cancellationToken)
    {
        var kpi = deptYearRow.KpiDefinition;
        var kpiId = deptYearRow.KpiDefinitionId;
        var aggregation = kpi.AggregationType;

        var quartersByNumber = quarterPeriods
            .Select((p, idx) => (Number: idx + 1, Period: p))
            .ToDictionary(x => x.Number, x => x.Period);

        // Department's own quarter values (SplitDimension = PERIOD, no employee attached).
        var deptQuarterRows = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.ParentAssignmentId == deptYearRow.Id
                && t.SplitDimension == TargetSplitDimension.Period
                && t.EmployeeId == null
                && t.KpiDefinitionId == kpiId)
            .ToListAsync(cancellationToken);

        var deptQuarterByPeriod = deptQuarterRows.ToDictionary(r => r.EvaluationPeriodId, r => r.TargetValue);

        var deptQ1 = quartersByNumber.TryGetValue(1, out var q1Period) && deptQuarterByPeriod.TryGetValue(q1Period.Id, out var q1Val) ? q1Val : 0m;
        var deptQ2 = quartersByNumber.TryGetValue(2, out var q2Period) && deptQuarterByPeriod.TryGetValue(q2Period.Id, out var q2Val) ? q2Val : 0m;
        var deptQ3 = quartersByNumber.TryGetValue(3, out var q3Period) && deptQuarterByPeriod.TryGetValue(q3Period.Id, out var q3Val) ? q3Val : 0m;
        var deptQ4 = quartersByNumber.TryGetValue(4, out var q4Period) && deptQuarterByPeriod.TryGetValue(q4Period.Id, out var q4Val) ? q4Val : 0m;
        if (!IsPeriodAdditive(aggregation))
        {
            deptQ1 = deptQ2 = deptQ3 = deptQ4 = deptYearRow.TargetValue;
        }

        var department = await db.Departments.AsNoTracking().FirstAsync(d => d.Id == departmentId, cancellationToken);

        // Eligible employees (hold a responsible position for this KPI) and locked (ineligible) employees.
        var (eligibleEmployees, ineligibleEmployees, _) = await GetEligibleEmployeesAsync(departmentId, kpiId, yearPeriod.StartDate, cancellationToken);
        var eligibleEmployeeIds = eligibleEmployees.Select(e => e.Id).ToHashSet();

        var lockedEmployees = ineligibleEmployees
            .Select(e => new EmployeeAllocationMetadataDto(e.Id, e.FullName, "not_responsible_position"))
            .ToList();

        // Existing employee-year rows for this KPI under this department-year row.
        var employeeYearRows = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.ParentAssignmentId == deptYearRow.Id
                && t.SplitDimension == TargetSplitDimension.Org
                && t.EmployeeId != null
                && t.KpiDefinitionId == kpiId
                && t.Status != TargetAssignmentStatus.Superseded
                && eligibleEmployeeIds.Contains(t.EmployeeId!.Value))
            .ToListAsync(cancellationToken);

        var employeeYearByEmployeeId = employeeYearRows.ToDictionary(r => r.EmployeeId!.Value, r => r);

        var employeeYearIds = employeeYearRows.Select(r => r.Id).ToHashSet();
        var employeeQuarterRows = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.SplitDimension == TargetSplitDimension.Period
                && t.KpiDefinitionId == kpiId
                && t.ParentAssignmentId != null && employeeYearIds.Contains(t.ParentAssignmentId.Value))
            .ToListAsync(cancellationToken);

        var employeeQuarterByEmployeeAndPeriod = employeeQuarterRows
            .GroupBy(r => (r.ParentAssignmentId!.Value, r.EvaluationPeriodId))
            .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.TargetValue ?? 0m);

        // Previous year values and actuals per employee.
        var previousYearValuesByEmployee = new Dictionary<Guid, decimal>();
        var previousYearActualByEmployee = new Dictionary<Guid, decimal>();
        var previousYearPeriod = await uow.EvaluationPeriods.GetYearAsync(year - 1, cancellationToken);

        if (previousYearPeriod is not null && eligibleEmployeeIds.Count > 0)
        {
            var prevEmployeeYearRows = await db.TargetAssignments
                .AsNoTracking()
                .Where(t => t.SplitDimension == TargetSplitDimension.Org
                    && t.EmployeeId != null && eligibleEmployeeIds.Contains(t.EmployeeId.Value)
                    && t.KpiDefinitionId == kpiId
                    && t.Status == TargetAssignmentStatus.Active
                    && t.EvaluationPeriodId == previousYearPeriod.Id)
                .ToListAsync(cancellationToken);

            foreach (var row in prevEmployeeYearRows)
            {
                previousYearValuesByEmployee[row.EmployeeId!.Value] = row.TargetValue;
            }

            if (prevEmployeeYearRows.Count > 0)
            {
                var rootToEmployee = prevEmployeeYearRows.ToDictionary(r => r.Id, r => r.EmployeeId!.Value);
                var treeIds = new HashSet<Guid>(rootToEmployee.Keys);
                var idToRootId = rootToEmployee.Keys.ToDictionary(id => id, id => id);
                var currentLevelIds = rootToEmployee.Keys.ToList();

                while (currentLevelIds.Count > 0)
                {
                    var children = await db.TargetAssignments
                        .AsNoTracking()
                        .Where(t => t.ParentAssignmentId != null && currentLevelIds.Contains(t.ParentAssignmentId.Value))
                        .Select(t => new { t.Id, t.ParentAssignmentId })
                        .ToListAsync(cancellationToken);

                    if (children.Count == 0)
                    {
                        break;
                    }

                    var nextLevelIds = new List<Guid>();
                    foreach (var child in children)
                    {
                        if (idToRootId.TryGetValue(child.ParentAssignmentId!.Value, out var rootId))
                        {
                            idToRootId[child.Id] = rootId;
                            treeIds.Add(child.Id);
                            nextLevelIds.Add(child.Id);
                        }
                    }

                    currentLevelIds = nextLevelIds;
                }

                var treeIdList = treeIds.ToList();
                var actualRows = await db.ActualProgresses
                    .AsNoTracking()
                    .Where(a => a.Status == ActualProgressStatus.Valid
                        && treeIdList.Contains(a.TargetAssignmentId)
                        && a.ProgressDate >= previousYearPeriod.StartDate
                        && a.ProgressDate <= previousYearPeriod.EndDate)
                    .Select(a => new { a.TargetAssignmentId, a.Value })
                    .ToListAsync(cancellationToken);

                foreach (var group in actualRows.GroupBy(a => idToRootId[a.TargetAssignmentId]))
                {
                    var empId = rootToEmployee[group.Key];
                    previousYearActualByEmployee[empId] = previousYearActualByEmployee.GetValueOrDefault(empId) + group.Sum(x => x.Value);
                }
            }
        }

        // Build employee rows and totals.
        var employeeRows = new List<EmployeeAllocationRowDto>();
        decimal totalsYear = 0m, totalsQ1 = 0m, totalsQ2 = 0m, totalsQ3 = 0m, totalsQ4 = 0m;

        foreach (var emp in eligibleEmployees)
        {
            var empYear = employeeYearByEmployeeId.TryGetValue(emp.Id, out var empYearRow) ? empYearRow.TargetValue : 0m;
            decimal GetQuarter(int number)
            {
                if (!quartersByNumber.TryGetValue(number, out var period) || !employeeYearByEmployeeId.TryGetValue(emp.Id, out var yearRow))
                {
                    return 0m;
                }

                return employeeQuarterByEmployeeAndPeriod.TryGetValue((yearRow.Id, period.Id), out var val) ? val : 0m;
            }

            var empQ1 = GetQuarter(1);
            var empQ2 = GetQuarter(2);
            var empQ3 = GetQuarter(3);
            var empQ4 = GetQuarter(4);

            decimal? prevYear = previousYearValuesByEmployee.TryGetValue(emp.Id, out var pv) && pv > 0 ? pv : null;
            decimal? prevYearActual = previousYearActualByEmployee.TryGetValue(emp.Id, out var pva) ? pva : null;

            var matched = empYear > 0 && QuartersMatch(aggregation, empYear, [empQ1, empQ2, empQ3, empQ4]);

            employeeRows.Add(new EmployeeAllocationRowDto(
                EmployeeId: emp.Id,
                EmployeeName: emp.FullName,
                JobPositionName: emp.JobPosition.Title,
                Year: empYear,
                Q1: empQ1,
                Q2: empQ2,
                Q3: empQ3,
                Q4: empQ4,
                PreviousYear: prevYear,
                PreviousYearActual: prevYearActual,
                Matched: matched));

            totalsYear += empYear;
            totalsQ1 += empQ1;
            totalsQ2 += empQ2;
            totalsQ3 += empQ3;
            totalsQ4 += empQ4;
        }

        return new EmployeeAllocationDto(
            DeptYearId: deptYearRow.Id,
            KpiId: kpiId,
            KpiName: kpi.Name,
            Unit: kpi.Unit,
            DepartmentId: departmentId,
            DepartmentName: department.Name,
            Status: deptYearRow.Status.ToString().ToUpperInvariant(),
            Department: new DepartmentTargetValuesDto(deptYearRow.TargetValue, deptQ1, deptQ2, deptQ3, deptQ4),
            Employees: employeeRows,
            Totals: new AllocationTotalsDto(totalsYear, totalsQ1, totalsQ2, totalsQ3, totalsQ4),
            Remaining: new AllocationRemainingDto(
                deptYearRow.TargetValue - totalsYear, deptQ1 - totalsQ1, deptQ2 - totalsQ2, deptQ3 - totalsQ3, deptQ4 - totalsQ4),
            LockedEmployees: lockedEmployees);
    }

    public async Task<EmployeeAllocationDto> SaveEmployeeAllocationAsync(Guid deptYearId, SaveEmployeeAllocationRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var (deptYear, departmentId, year, yearPeriodId, yearPeriod) = await LoadAndValidateDeptYearAsync(deptYearId, userId, cancellationToken);

        // Tier 3 opens only once the department's own tier (tier 2) is Published/ACTIVE (business rule).
        if (deptYear.Status != TargetAssignmentStatus.Active)
        {
            throw new ConflictException(
                "Chỉ tiêu phòng ban (cấp 2) chưa được phát hành nên chưa thể giao chỉ tiêu nhân viên.",
                ErrorCodes.TargetTierNotPublished);
        }

        var aggregation = deptYear.KpiDefinition.AggregationType;
        var kpiId = deptYear.KpiDefinitionId;

        var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(
            PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken);
        var quartersByNumber = quarterPeriods
            .Select((p, idx) => (Number: idx + 1, Period: p))
            .ToDictionary(x => x.Number, x => x.Period);

        var (eligibleEmployees, _, _) = await GetEligibleEmployeesAsync(departmentId, kpiId, yearPeriod.StartDate, cancellationToken);
        var eligibleEmployeeIds = eligibleEmployees.Select(e => e.Id).ToHashSet();
        var employeeNameById = eligibleEmployees.ToDictionary(e => e.Id, e => e.FullName);

        // Validate request shape.
        var requestEmployeeIds = request.Employees.Select(e => e.EmployeeId).ToList();
        if (requestEmployeeIds.Count != requestEmployeeIds.Distinct().Count())
        {
            throw new ValidationException("Duplicate EmployeeId in request.");
        }

        foreach (var empReq in request.Employees)
        {
            if (empReq.Year < 0 || empReq.Q1 < 0 || empReq.Q2 < 0 || empReq.Q3 < 0 || empReq.Q4 < 0)
            {
                throw new ValidationException("All allocation values must be >= 0.");
            }

            if (!eligibleEmployeeIds.Contains(empReq.EmployeeId))
            {
                throw new BusinessRuleException(
                    ErrorCodes.TargetEmployeeNotEligible,
                    $"Employee {empReq.EmployeeId} does not hold a responsible position for this KPI.",
                    422);
            }
        }

        // Exact totals: employees must equal the department year; each employee's quarters must equal their year.
        decimal requestedYearTotal = 0m;
        var quarterMismatches = new List<string>();
        foreach (var empReq in request.Employees)
        {
            requestedYearTotal += empReq.Year;
            if (!QuartersMatch(aggregation, empReq.Year, [empReq.Q1, empReq.Q2, empReq.Q3, empReq.Q4]))
            {
                var empName = employeeNameById.GetValueOrDefault(empReq.EmployeeId, empReq.EmployeeId.ToString());
                quarterMismatches.Add(IsPeriodAdditive(aggregation)
                    ? $"Nhân viên {empName}: tổng 4 quý = {empReq.Q1 + empReq.Q2 + empReq.Q3 + empReq.Q4}, chỉ tiêu năm = {empReq.Year}."
                    : $"Nhân viên {empName}: mỗi quý phải bằng chỉ tiêu năm = {empReq.Year}.");
            }
        }

        if (requestedYearTotal != deptYear.TargetValue)
        {
            throw new BusinessRuleException(
                ErrorCodes.TargetEmployeeAllocationSumMismatch,
                null,
                422,
                [$"Tổng chỉ tiêu các nhân viên = {requestedYearTotal}, chỉ tiêu phòng = {deptYear.TargetValue}."]);
        }

        if (quarterMismatches.Count > 0)
        {
            throw new BusinessRuleException(ErrorCodes.TargetEmployeeQuarterSumMismatch, null, 422, quarterMismatches);
        }

        // Locked-quarter check (same rule as department/branch allocation).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var existingEmployeeYearRows = await db.TargetAssignments
            .Where(t => t.ParentAssignmentId == deptYear.Id
                && t.SplitDimension == TargetSplitDimension.Org
                && t.EmployeeId != null
                && t.KpiDefinitionId == kpiId
                && t.Status != TargetAssignmentStatus.Superseded)
            .ToListAsync(cancellationToken);

        // Once any employee-year row for this KPI is ACTIVE (tier 3 already published for it), editing it here
        // would bypass the mid-term adjustment flow (D12/D13, deferred) and could violate the DB's children-sum
        // trigger mid-transaction (it re-checks the ACTIVE sibling sum after every single row update).
        if (existingEmployeeYearRows.Any(r => r.Status == TargetAssignmentStatus.Active))
        {
            throw new ConflictException(
                "Chỉ tiêu nhân viên cho KPI này đã được phát hành; chỉ có thể sửa qua điều chỉnh giữa kỳ.",
                ErrorCodes.TargetInvalidState);
        }

        var employeeYearByEmployeeId = existingEmployeeYearRows.ToDictionary(r => r.EmployeeId!.Value, r => r);
        var employeeYearIds = existingEmployeeYearRows.Select(r => r.Id).ToHashSet();

        var existingEmployeeQuarterRows = await db.TargetAssignments
            .Where(t => t.SplitDimension == TargetSplitDimension.Period
                && t.KpiDefinitionId == kpiId
                && t.ParentAssignmentId != null && employeeYearIds.Contains(t.ParentAssignmentId.Value))
            .ToListAsync(cancellationToken);

        var employeeQuarterByEmployeeAndPeriod = existingEmployeeQuarterRows
            .GroupBy(r => (r.ParentAssignmentId!.Value, r.EvaluationPeriodId))
            .ToDictionary(g => g.Key, g => g.FirstOrDefault());

        var requestedEmployeeIds = request.Employees.Select(e => e.EmployeeId).ToHashSet();
        var staleEmployeeYearRows = existingEmployeeYearRows
            .Where(r => r.Status == TargetAssignmentStatus.Draft && !requestedEmployeeIds.Contains(r.EmployeeId!.Value))
            .ToList();
        var staleEmployeeYearIds = staleEmployeeYearRows.Select(r => r.Id).ToHashSet();
        var staleEmployeeQuarterRows = existingEmployeeQuarterRows
            .Where(r => r.ParentAssignmentId.HasValue && staleEmployeeYearIds.Contains(r.ParentAssignmentId.Value))
            .ToList();

        foreach (var empReq in request.Employees)
        {
            var quarterValues = new[] { empReq.Q1, empReq.Q2, empReq.Q3, empReq.Q4 };
            for (int i = 0; i < 4; i++)
            {
                var quarter = quartersByNumber[i + 1];
                var isLocked = quarter.Status != PeriodStatus.Open || quarter.EndDate < today;

                if (isLocked && employeeYearByEmployeeId.TryGetValue(empReq.EmployeeId, out var empYearRow))
                {
                    var key = (empYearRow.Id, quarter.Id);
                    var storedValue = employeeQuarterByEmployeeAndPeriod.TryGetValue(key, out var child) ? child?.TargetValue : null;
                    if ((storedValue ?? 0m) != quarterValues[i])
                    {
                        throw new ConflictException($"Quý {i + 1} đã khóa, không thể thay đổi.", ErrorCodes.PeriodLocked);
                    }
                }
            }
        }

        await uow.ExecuteInTransactionAsync(async ct =>
        {
            var now = DateTimeOffset.UtcNow;

            if (staleEmployeeQuarterRows.Count > 0)
            {
                db.TargetAssignments.RemoveRange(staleEmployeeQuarterRows);
            }

            if (staleEmployeeYearRows.Count > 0)
            {
                db.TargetAssignments.RemoveRange(staleEmployeeYearRows);
            }

            foreach (var empReq in request.Employees)
            {
                TargetAssignment employeeYearRow;
                if (employeeYearByEmployeeId.TryGetValue(empReq.EmployeeId, out var existing))
                {
                    employeeYearRow = existing;
                    employeeYearRow.TargetValue = empReq.Year;
                }
                else
                {
                    employeeYearRow = new TargetAssignment
                    {
                        Id = Guid.NewGuid(),
                        ParentAssignmentId = deptYear.Id,
                        EmployeeId = empReq.EmployeeId,
                        KpiDefinitionId = kpiId,
                        EvaluationPeriodId = yearPeriodId,
                        TargetValue = empReq.Year,
                        SplitDimension = TargetSplitDimension.Org,
                        Status = TargetAssignmentStatus.Draft,
                        Version = 1,
                        CreatedAt = now,
                        AssignedAt = now,
                        EffectiveFrom = yearPeriod.StartDate
                    };
                    await uow.TargetAssignments.AddAsync(employeeYearRow, ct);
                    employeeYearByEmployeeId[empReq.EmployeeId] = employeeYearRow;
                }

                var quarterValues = new[] { empReq.Q1, empReq.Q2, empReq.Q3, empReq.Q4 };
                for (int i = 0; i < 4; i++)
                {
                    var quarter = quartersByNumber[i + 1];
                    var key = (employeeYearRow.Id, quarter.Id);

                    if (employeeQuarterByEmployeeAndPeriod.TryGetValue(key, out var existingQuarter) && existingQuarter != null)
                    {
                        existingQuarter.TargetValue = quarterValues[i];
                    }
                    else
                    {
                        var newQuarter = new TargetAssignment
                        {
                            Id = Guid.NewGuid(),
                            ParentAssignmentId = employeeYearRow.Id,
                            EmployeeId = empReq.EmployeeId,
                            KpiDefinitionId = kpiId,
                            EvaluationPeriodId = quarter.Id,
                            TargetValue = quarterValues[i],
                            SplitDimension = TargetSplitDimension.Period,
                            Status = TargetAssignmentStatus.Draft,
                            Version = 1,
                            CreatedAt = now,
                            AssignedAt = now,
                            EffectiveFrom = quarter.StartDate
                        };
                        await uow.TargetAssignments.AddAsync(newQuarter, ct);
                    }
                }
            }

            await uow.SaveChangesAsync(ct);
        }, cancellationToken);

        return await GetEmployeeAllocationAsync(deptYearId, userId, cancellationToken);
    }

    public async Task<PublishEmployeesResultDto> PublishEmployeesAsync(Guid deptYearId, Guid userId, CancellationToken cancellationToken)
    {
        var (deptYear, departmentId, year, yearPeriodId, yearPeriod) = await LoadAndValidateDeptYearAsync(deptYearId, userId, cancellationToken);

        if (deptYear.Status != TargetAssignmentStatus.Active)
        {
            throw new ConflictException(
                "Chỉ tiêu phòng ban (cấp 2) chưa được phát hành nên chưa thể phát hành chỉ tiêu nhân viên.",
                ErrorCodes.TargetTierNotPublished);
        }

        var deptYearRows = await db.TargetAssignments
            .Include(t => t.KpiDefinition)
            .Where(t => t.DepartmentId == departmentId
                && t.EvaluationPeriodId == yearPeriodId
                && t.SplitDimension == TargetSplitDimension.Org
                && t.EmployeeId == null
                && t.ParentAssignmentId != null
                && t.Status == TargetAssignmentStatus.Active)
            .ToListAsync(cancellationToken);

        var blockers = new List<string>();
        var allEmployeeYearRows = new List<TargetAssignment>();
        var allEmployeeQuarterRows = new List<TargetAssignment>();

        foreach (var row in deptYearRows)
        {
            var (eligibleEmployees, _, _) = await GetEligibleEmployeesAsync(departmentId, row.KpiDefinitionId, yearPeriod.StartDate, cancellationToken);
            var eligibleIds = eligibleEmployees.Select(e => e.Id).ToHashSet();

            var employeeYearRows = await db.TargetAssignments
                .Where(t => t.ParentAssignmentId == row.Id
                    && t.SplitDimension == TargetSplitDimension.Org
                    && t.EmployeeId != null
                    && t.KpiDefinitionId == row.KpiDefinitionId)
                .ToListAsync(cancellationToken);

            if (eligibleIds.Count == 0)
            {
                // No one is responsible for this KPI in this department: nothing to publish, not a blocker.
                continue;
            }

            var coveredIds = employeeYearRows.Select(r => r.EmployeeId!.Value).ToHashSet();
            var missing = eligibleIds.Except(coveredIds).Any();
            var sum = employeeYearRows.Sum(r => r.TargetValue);

            if (missing || sum != row.TargetValue)
            {
                blockers.Add($"{row.KpiDefinition.Name}: chưa giao đủ chỉ tiêu nhân viên (tổng {sum}/{row.TargetValue}).");
                continue;
            }

            var employeeYearIds = employeeYearRows.Select(r => r.Id).ToHashSet();
            var employeeQuarterRows = await db.TargetAssignments
                .Where(t => t.SplitDimension == TargetSplitDimension.Period
                    && t.KpiDefinitionId == row.KpiDefinitionId
                    && t.ParentAssignmentId != null && employeeYearIds.Contains(t.ParentAssignmentId.Value))
                .ToListAsync(cancellationToken);

            var aggregation = row.KpiDefinition.AggregationType;
            var quarterByEmployeeYear = employeeQuarterRows
                .GroupBy(r => r.ParentAssignmentId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var anyMismatch = employeeYearRows.Any(empYear =>
            {
                var quarters = quarterByEmployeeYear.TryGetValue(empYear.Id, out var q) ? q.Select(x => x.TargetValue).ToList() : [];
                return quarters.Count != 4 || !QuartersMatch(aggregation, empYear.TargetValue, quarters);
            });

            if (anyMismatch)
            {
                blockers.Add($"{row.KpiDefinition.Name}: chỉ tiêu quý của nhân viên chưa khớp chỉ tiêu năm.");
                continue;
            }

            allEmployeeYearRows.AddRange(employeeYearRows);
            allEmployeeQuarterRows.AddRange(employeeQuarterRows);
        }

        if (blockers.Count > 0)
        {
            throw new BusinessRuleException(ErrorCodes.TargetEmployeePublishBlocked, null, 422, blockers);
        }

        await uow.ExecuteInTransactionAsync(async ct =>
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var row in allEmployeeYearRows)
            {
                row.Status = TargetAssignmentStatus.Active;
                row.ApprovedAt = now;
            }

            foreach (var row in allEmployeeQuarterRows)
            {
                row.Status = TargetAssignmentStatus.Active;
                row.ApprovedAt = now;
            }

            await uow.SaveChangesAsync(ct);

            var auditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                EntityName = "EmployeeTargetPublish",
                EntityId = $"{departmentId}:{year}",
                Action = "Publish",
                NewValues = JsonSerializer.Serialize(new
                {
                    employeeYearCount = allEmployeeYearRows.Count,
                    employeeQuarterCount = allEmployeeQuarterRows.Count
                }),
                ActorId = userId,
                OccurredAt = now
            };

            db.AuditLogs.Add(auditLog);
            await uow.SaveChangesAsync(ct);

            // Target Allocation v3 (D10, D20): create a PENDING ack for every newly-published employee that
            // does not already have one for this year, and record a PUBLISH change-log entry per KPI.
            var coveredEmployeeIds = allEmployeeYearRows.Select(r => r.EmployeeId!.Value).Distinct().ToList();
            var existingAckEmployeeIds = await db.EmployeeTargetAcks
                .Where(a => a.Year == year && coveredEmployeeIds.Contains(a.EmployeeId))
                .Select(a => a.EmployeeId)
                .ToListAsync(ct);

            foreach (var employeeId in coveredEmployeeIds.Except(existingAckEmployeeIds))
            {
                db.EmployeeTargetAcks.Add(new EmployeeTargetAck
                {
                    Id = Guid.NewGuid(),
                    EmployeeId = employeeId,
                    Year = year,
                    Status = EmployeeTargetAckStatus.Pending,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            await uow.SaveChangesAsync(ct);

            foreach (var row in allEmployeeYearRows)
            {
                await changeLog.LogAsync(
                    userId, TargetChangeLogAction.Publish, tier: 3, kpiId: row.KpiDefinitionId,
                    unit: row.KpiDefinition.Unit, period: "YEAR", oldValue: null, newValue: row.TargetValue,
                    basis: "MANUAL", employeeId: row.EmployeeId, departmentId: departmentId, year: year,
                    cancellationToken: ct);
            }

            // D20: persist a notification for every employee newly covered by this publish.
            var publishDrafts = coveredEmployeeIds
                .Select(employeeId => new NotificationDraft(
                    employeeId,
                    NotificationType.TargetRevised,
                    "Chỉ tiêu năm đã được phát hành",
                    $"Chỉ tiêu KPI năm {year} của bạn đã được phát hành. Vui lòng xác nhận.",
                    NotificationSeverity.Important,
                    NotificationRelatedType.Target,
                    null))
                .ToList();
            if (publishDrafts.Count > 0)
            {
                await notifier.TryPublishAsync(publishDrafts, ct);
            }
        }, cancellationToken);

        return new PublishEmployeesResultDto(
            PublishedAt: DateTimeOffset.UtcNow,
            DepartmentId: departmentId,
            Year: year,
            EmployeeYearCount: allEmployeeYearRows.Count,
            EmployeeQuarterCount: allEmployeeQuarterRows.Count);
    }

    // =========================================================================
    // Mid-term adjustment (Target Allocation v3, D12/D13): the only way to change a published target.
    // Reuses the Save*/Publish validations above, but requires the target rows to already be ACTIVE and writes
    // through versioning (SUPERSEDED + Version+1, docs/api/TARGET_ALLOCATION_CONTRACT.md R4) instead of an in-place
    // update, with an ADJUST change-log entry per changed row.
    // =========================================================================

    private async Task<Employee?> ResolveChangedByEmployeeAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Employees
            .AsNoTracking()
            .Where(e => e.Id == userId || (e.Account != null && e.Account.Id == userId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<QuarterSplitDto> AdjustQuartersAsync(Guid branchYearId, AdjustQuarterSplitRequest request, Guid userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ChangeReason))
        {
            throw new ValidationException("Lý do điều chỉnh (changeReason) là bắt buộc.");
        }

        var (branchYear, branchId, year, yearPeriodId, yearPeriod) = await LoadAndValidateBranchYearAsync(branchYearId, userId, cancellationToken);

        if (branchYear.Status != TargetAssignmentStatus.Active)
        {
            throw new ConflictException(
                "Chỉ tiêu năm của chi nhánh chưa được phát hành; chưa cần điều chỉnh giữa kỳ, hãy dùng lưu thông thường.",
                ErrorCodes.TargetInvalidState);
        }

        var branchYearRows = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.KpiDefinition)
            .Where(t => t.DepartmentId == branchId && t.EvaluationPeriodId == yearPeriodId
                && t.ParentAssignmentId == null && t.SplitDimension == null)
            .ToListAsync(cancellationToken);
        var kpisByIdInBranchYear = branchYearRows.ToDictionary(r => r.KpiDefinitionId);

        var requestKpiIds = request.Rows.Select(r => r.KpiId).ToList();
        if (requestKpiIds.Count != requestKpiIds.Distinct().Count())
        {
            throw new ValidationException("Duplicate KPI IDs in request.");
        }

        var quarterMismatches = new List<string>();
        var rowsToApply = new List<(TargetAssignment BranchYearRow, SaveQuarterSplitRowRequest Row)>();
        foreach (var row in request.Rows)
        {
            if (!kpisByIdInBranchYear.TryGetValue(row.KpiId, out var branchYearRow))
            {
                throw new ValidationException($"KPI {row.KpiId} is not found in this branch-year.");
            }

            if (!IsPeriodAdditive(branchYearRow.KpiDefinition.AggregationType))
            {
                throw new ValidationException($"KPI {branchYearRow.KpiDefinition.Code} không chia theo quý, không cần điều chỉnh.");
            }

            if (branchYearRow.Status != TargetAssignmentStatus.Active)
            {
                throw new ConflictException($"KPI {branchYearRow.KpiDefinition.Code} chưa được phát hành.", ErrorCodes.TargetInvalidState);
            }

            if (row.Q1 < 0 || row.Q2 < 0 || row.Q3 < 0 || row.Q4 < 0)
            {
                throw new ValidationException("Quarter values must be >= 0.");
            }

            var sum = row.Q1 + row.Q2 + row.Q3 + row.Q4;
            if (sum != branchYearRow.TargetValue)
            {
                quarterMismatches.Add($"KPI {branchYearRow.KpiDefinition.Code}: tổng 4 quý = {sum}, chỉ tiêu năm = {branchYearRow.TargetValue}.");
            }

            rowsToApply.Add((branchYearRow, row));
        }

        if (quarterMismatches.Count > 0)
        {
            throw new BusinessRuleException(ErrorCodes.TargetQuarterSumMismatch, null, 422, quarterMismatches);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken);

        var branchYearRowIds = rowsToApply.Select(r => r.BranchYearRow.Id).ToList();
        var existingActiveQuarters = await db.TargetAssignments
            .Where(t => t.ParentAssignmentId != null && branchYearRowIds.Contains(t.ParentAssignmentId.Value)
                && t.SplitDimension == TargetSplitDimension.Period && t.Status == TargetAssignmentStatus.Active)
            .ToListAsync(cancellationToken);
        var existingByParentAndPeriod = existingActiveQuarters
            .GroupBy(r => (r.ParentAssignmentId!.Value, r.EvaluationPeriodId))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var (branchYearRow, row) in rowsToApply)
        {
            var quarterValues = new[] { row.Q1, row.Q2, row.Q3, row.Q4 };
            for (var i = 0; i < 4; i++)
            {
                var quarter = quarterPeriods[i];
                var isLocked = quarter.Status != PeriodStatus.Open || quarter.EndDate < today;
                if (isLocked)
                {
                    var stored = existingByParentAndPeriod.TryGetValue((branchYearRow.Id, quarter.Id), out var ex) ? ex.TargetValue : 0m;
                    if (stored != quarterValues[i])
                    {
                        throw new ConflictException($"Quý {i + 1} đã khóa, không thể điều chỉnh.", ErrorCodes.PeriodLocked);
                    }
                }
            }
        }

        var changedByEmployee = await ResolveChangedByEmployeeAsync(userId, cancellationToken);

        await uow.ExecuteInTransactionAsync(async ct =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var (branchYearRow, row) in rowsToApply)
            {
                var quarterValues = new[] { row.Q1, row.Q2, row.Q3, row.Q4 };
                for (var i = 0; i < 4; i++)
                {
                    var quarter = quarterPeriods[i];
                    if (!existingByParentAndPeriod.TryGetValue((branchYearRow.Id, quarter.Id), out var existing)
                        || existing.TargetValue == quarterValues[i])
                    {
                        continue;
                    }

                    var oldValue = existing.TargetValue;
                    existing.Status = TargetAssignmentStatus.Superseded;
                    existing.EffectiveTo = today < existing.EffectiveFrom ? existing.EffectiveFrom : today;
                    existing.UpdatedAt = now;

                    var next = new TargetAssignment
                    {
                        Id = Guid.NewGuid(),
                        ParentAssignmentId = branchYearRow.Id,
                        DepartmentId = branchId,
                        KpiDefinitionId = branchYearRow.KpiDefinitionId,
                        EvaluationPeriodId = quarter.Id,
                        TargetValue = quarterValues[i],
                        SplitDimension = TargetSplitDimension.Period,
                        Status = TargetAssignmentStatus.Active,
                        Version = existing.Version + 1,
                        AssignedAt = now,
                        ApprovedAt = now,
                        ChangeReason = request.ChangeReason,
                        CreatedAt = now,
                        UpdatedAt = now,
                        EffectiveFrom = today < existing.EffectiveFrom ? existing.EffectiveFrom : today,
                        PreviousVersionId = existing.Id,
                        ChangedByEmployeeId = changedByEmployee?.Id
                    };
                    await uow.TargetAssignments.AddAsync(next, ct);

                    await changeLog.LogAsync(
                        userId, TargetChangeLogAction.Adjust, tier: 1, kpiId: branchYearRow.KpiDefinitionId,
                        unit: branchYearRow.KpiDefinition.Unit, period: $"Q{i + 1}",
                        oldValue: oldValue, newValue: quarterValues[i], basis: "MANUAL",
                        departmentId: branchId, year: year, cancellationToken: ct);
                }
            }

            await uow.SaveChangesAsync(ct);
        }, cancellationToken);

        return await GetQuartersAsync(branchYearId, userId, cancellationToken);
    }

    public async Task<KpiAllocationDto> AdjustAllocationAsync(Guid branchYearId, Guid kpiId, AdjustKpiAllocationRequest request, Guid userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ChangeReason))
        {
            throw new ValidationException("Lý do điều chỉnh (changeReason) là bắt buộc.");
        }

        var (branchYear, branchId, year, yearPeriodId, yearPeriod) = await LoadAndValidateBranchYearAsync(branchYearId, userId, cancellationToken);

        var branchYearKpiRow = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.KpiDefinition)
            .FirstOrDefaultAsync(t => t.KpiDefinitionId == kpiId && t.DepartmentId == branchId
                && t.EvaluationPeriodId == yearPeriodId && t.ParentAssignmentId == null && t.SplitDimension == null,
                cancellationToken)
            ?? throw new NotFoundException($"KPI {kpiId} not found in branch-year target.");

        if (!IsAllocatedToDepartments(branchYearKpiRow.KpiDefinition.AggregationType))
        {
            throw new ValidationException("Ratio KPIs cannot be allocated (không phân bổ).");
        }

        if (branchYearKpiRow.Status != TargetAssignmentStatus.Active)
        {
            throw new ConflictException(
                "Chỉ tiêu này chưa được phát hành; chưa cần điều chỉnh giữa kỳ, hãy dùng lưu thông thường.",
                ErrorCodes.TargetInvalidState);
        }

        var aggregation = branchYearKpiRow.KpiDefinition.AggregationType;
        var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken);

        var existingDeptYearRows = await db.TargetAssignments
            .Where(t => t.ParentAssignmentId == branchYearKpiRow.Id && t.SplitDimension == TargetSplitDimension.Org
                && t.KpiDefinitionId == kpiId && t.Status == TargetAssignmentStatus.Active)
            .ToListAsync(cancellationToken);
        var deptYearByDeptId = existingDeptYearRows.ToDictionary(r => r.DepartmentId!.Value, r => r);

        var requestDeptIds = request.Departments.Select(d => d.DepartmentId).ToList();
        if (requestDeptIds.Count != requestDeptIds.Distinct().Count())
        {
            throw new ValidationException("Duplicate DepartmentId in request.");
        }

        if (!requestDeptIds.ToHashSet().SetEquals(deptYearByDeptId.Keys))
        {
            throw new ValidationException(
                "Yêu cầu điều chỉnh phải gồm đầy đủ các phòng ban đang được phân bổ cho KPI này (không thêm/bớt phòng).");
        }

        var departmentNames = await db.Departments
            .AsNoTracking()
            .Where(d => requestDeptIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        var allocationMismatches = new List<string>();
        var deptQuarterMismatches = new List<string>();
        decimal requestedYearTotal = 0m;
        foreach (var deptReq in request.Departments)
        {
            if (deptReq.Year < 0 || deptReq.Q1 < 0 || deptReq.Q2 < 0 || deptReq.Q3 < 0 || deptReq.Q4 < 0)
            {
                throw new ValidationException("All allocation values must be >= 0.");
            }

            requestedYearTotal += deptReq.Year;
            if (!QuartersMatch(aggregation, deptReq.Year, [deptReq.Q1, deptReq.Q2, deptReq.Q3, deptReq.Q4]))
            {
                var deptName = departmentNames.GetValueOrDefault(deptReq.DepartmentId, deptReq.DepartmentId.ToString());
                deptQuarterMismatches.Add(IsPeriodAdditive(aggregation)
                    ? $"Phòng {deptName}: tổng 4 quý = {deptReq.Q1 + deptReq.Q2 + deptReq.Q3 + deptReq.Q4}, chỉ tiêu năm = {deptReq.Year}."
                    : $"Phòng {deptName}: mỗi quý phải bằng chỉ tiêu năm = {deptReq.Year}.");
            }
        }

        if (requestedYearTotal != branchYearKpiRow.TargetValue)
        {
            allocationMismatches.Add($"Tổng chỉ tiêu các phòng = {requestedYearTotal}, chỉ tiêu chi nhánh = {branchYearKpiRow.TargetValue}.");
        }

        if (allocationMismatches.Count > 0)
        {
            throw new BusinessRuleException(ErrorCodes.TargetAllocationSumMismatch, null, 422, allocationMismatches);
        }

        if (deptQuarterMismatches.Count > 0)
        {
            throw new BusinessRuleException(ErrorCodes.TargetDeptQuarterSumMismatch, null, 422, deptQuarterMismatches);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var deptYearIds = existingDeptYearRows.Select(r => r.Id).ToList();
        var existingDeptQuarterRows = await db.TargetAssignments
            .Where(t => t.SplitDimension == TargetSplitDimension.Period && t.KpiDefinitionId == kpiId
                && t.ParentAssignmentId != null && deptYearIds.Contains(t.ParentAssignmentId.Value)
                && t.Status == TargetAssignmentStatus.Active)
            .ToListAsync(cancellationToken);
        var deptQuarterByDeptAndPeriod = existingDeptQuarterRows
            .GroupBy(r => (r.ParentAssignmentId!.Value, r.EvaluationPeriodId))
            .ToDictionary(g => g.Key, g => g.First());

        // Locked-quarter check: a locked quarter cannot change value.
        foreach (var deptReq in request.Departments)
        {
            var deptYearRow = deptYearByDeptId[deptReq.DepartmentId];
            var quarterValues = new[] { deptReq.Q1, deptReq.Q2, deptReq.Q3, deptReq.Q4 };
            for (var i = 0; i < 4; i++)
            {
                var quarter = quarterPeriods[i];
                var isLocked = quarter.Status != PeriodStatus.Open || quarter.EndDate < today;
                if (isLocked)
                {
                    var stored = deptQuarterByDeptAndPeriod.TryGetValue((deptYearRow.Id, quarter.Id), out var ex) ? ex.TargetValue : 0m;
                    if (stored != quarterValues[i])
                    {
                        throw new ConflictException($"Quý {i + 1} đã khóa, không thể điều chỉnh.", ErrorCodes.PeriodLocked);
                    }
                }
            }
        }

        var changedByEmployee = await ResolveChangedByEmployeeAsync(userId, cancellationToken);
        var changedDeptIds = new List<Guid>();

        await uow.ExecuteInTransactionAsync(async ct =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var deptReq in request.Departments)
            {
                var deptYearRow = deptYearByDeptId[deptReq.DepartmentId];
                var quarterValues = new[] { deptReq.Q1, deptReq.Q2, deptReq.Q3, deptReq.Q4 };
                var oldYear = deptYearRow.TargetValue;

                var quarterChanged = false;
                for (var i = 0; i < 4; i++)
                {
                    var quarter = quarterPeriods[i];
                    var stored = deptQuarterByDeptAndPeriod.TryGetValue((deptYearRow.Id, quarter.Id), out var ex) ? ex.TargetValue : 0m;
                    if (stored != quarterValues[i])
                    {
                        quarterChanged = true;
                        break;
                    }
                }

                if (oldYear == deptReq.Year && !quarterChanged)
                {
                    continue;
                }

                changedDeptIds.Add(deptReq.DepartmentId);

                // Re-version the dept-year row (its own value may be unchanged, but its quarter children are about
                // to move under a new parent id, so they must be versioned together).
                deptYearRow.Status = TargetAssignmentStatus.Superseded;
                deptYearRow.EffectiveTo = today < deptYearRow.EffectiveFrom ? deptYearRow.EffectiveFrom : today;
                deptYearRow.UpdatedAt = now;

                var newDeptYearRow = new TargetAssignment
                {
                    Id = Guid.NewGuid(),
                    ParentAssignmentId = branchYearKpiRow.Id,
                    DepartmentId = deptReq.DepartmentId,
                    KpiDefinitionId = kpiId,
                    EvaluationPeriodId = yearPeriodId,
                    TargetValue = deptReq.Year,
                    SplitDimension = TargetSplitDimension.Org,
                    Status = TargetAssignmentStatus.Active,
                    Version = deptYearRow.Version + 1,
                    AssignedAt = now,
                    ApprovedAt = now,
                    ChangeReason = request.ChangeReason,
                    CreatedAt = now,
                    UpdatedAt = now,
                    EffectiveFrom = today < deptYearRow.EffectiveFrom ? deptYearRow.EffectiveFrom : today,
                    PreviousVersionId = deptYearRow.Id,
                    ChangedByEmployeeId = changedByEmployee?.Id
                };
                await uow.TargetAssignments.AddAsync(newDeptYearRow, ct);

                if (oldYear != deptReq.Year)
                {
                    await changeLog.LogAsync(
                        userId, TargetChangeLogAction.Adjust, tier: 2, kpiId: kpiId,
                        unit: branchYearKpiRow.KpiDefinition.Unit, period: "YEAR",
                        oldValue: oldYear, newValue: deptReq.Year, basis: "MANUAL",
                        departmentId: deptReq.DepartmentId, year: year, cancellationToken: ct);
                }

                for (var i = 0; i < 4; i++)
                {
                    var quarter = quarterPeriods[i];
                    var existingQuarter = deptQuarterByDeptAndPeriod.TryGetValue((deptYearRow.Id, quarter.Id), out var exq) ? exq : null;
                    var oldQuarterValue = existingQuarter?.TargetValue ?? 0m;

                    if (existingQuarter is not null)
                    {
                        existingQuarter.Status = TargetAssignmentStatus.Superseded;
                        existingQuarter.EffectiveTo = today < existingQuarter.EffectiveFrom ? existingQuarter.EffectiveFrom : today;
                        existingQuarter.UpdatedAt = now;
                    }

                    var newQuarter = new TargetAssignment
                    {
                        Id = Guid.NewGuid(),
                        ParentAssignmentId = newDeptYearRow.Id,
                        DepartmentId = deptReq.DepartmentId,
                        KpiDefinitionId = kpiId,
                        EvaluationPeriodId = quarter.Id,
                        TargetValue = quarterValues[i],
                        SplitDimension = TargetSplitDimension.Period,
                        Status = TargetAssignmentStatus.Active,
                        Version = (existingQuarter?.Version ?? 0) + 1,
                        AssignedAt = now,
                        ApprovedAt = now,
                        ChangeReason = request.ChangeReason,
                        CreatedAt = now,
                        UpdatedAt = now,
                        EffectiveFrom = existingQuarter is not null && today < existingQuarter.EffectiveFrom ? existingQuarter.EffectiveFrom : today,
                        PreviousVersionId = existingQuarter?.Id,
                        ChangedByEmployeeId = changedByEmployee?.Id
                    };
                    await uow.TargetAssignments.AddAsync(newQuarter, ct);

                    if (oldQuarterValue != quarterValues[i])
                    {
                        await changeLog.LogAsync(
                            userId, TargetChangeLogAction.Adjust, tier: 2, kpiId: kpiId,
                            unit: branchYearKpiRow.KpiDefinition.Unit, period: $"Q{i + 1}",
                            oldValue: oldQuarterValue, newValue: quarterValues[i], basis: "MANUAL",
                            departmentId: deptReq.DepartmentId, year: year, cancellationToken: ct);
                    }
                }

                // D13: reopen tier 3 under this department as mismatched. Existing employee-year rows (and their
                // quarter children) are re-parented onto the new dept-year id so they keep showing in
                // GetEmployeeAllocationAsync (their own values are untouched — DM has not rebalanced yet), but are
                // demoted to DRAFT: the DB's deferred children-sum trigger requires ACTIVE children of an ACTIVE
                // SUM-KPI parent to sum exactly, and they no longer do (the department year just changed), so they
                // cannot stay ACTIVE until the DM republishes them via AdjustEmployeeAllocationAsync.
                var employeeYearChildren = await db.TargetAssignments
                    .Where(t => t.ParentAssignmentId == deptYearRow.Id && t.SplitDimension == TargetSplitDimension.Org
                        && t.EmployeeId != null && t.Status == TargetAssignmentStatus.Active)
                    .ToListAsync(ct);
                var employeeYearChildIds = employeeYearChildren.Select(c => c.Id).ToHashSet();
                var employeeQuarterChildren = employeeYearChildIds.Count == 0
                    ? []
                    : await db.TargetAssignments
                        .Where(t => t.ParentAssignmentId != null && employeeYearChildIds.Contains(t.ParentAssignmentId.Value)
                            && t.Status == TargetAssignmentStatus.Active)
                        .ToListAsync(ct);

                foreach (var employeeYearChild in employeeYearChildren)
                {
                    employeeYearChild.ParentAssignmentId = newDeptYearRow.Id;
                    employeeYearChild.Status = TargetAssignmentStatus.Draft;
                    employeeYearChild.UpdatedAt = now;
                }

                foreach (var employeeQuarterChild in employeeQuarterChildren)
                {
                    employeeQuarterChild.Status = TargetAssignmentStatus.Draft;
                    employeeQuarterChild.UpdatedAt = now;
                }
            }

            await uow.SaveChangesAsync(ct);
        }, cancellationToken);

        // D13: notify each adjusted department's manager that tier 3 needs rebalancing (the employee totals under
        // it no longer equal the new department year — GetEmployeeAllocationAsync reflects this live via Matched).
        if (changedDeptIds.Count > 0)
        {
            var drafts = new List<NotificationDraft>();
            foreach (var deptId in changedDeptIds.Distinct())
            {
                try
                {
                    var detail = await departmentService.GetDepartmentAsync(userId, deptId, cancellationToken);
                    if (detail.Manager is not null)
                    {
                        drafts.Add(new NotificationDraft(
                            detail.Manager.Id,
                            NotificationType.TargetRevised,
                            "Chỉ tiêu phòng ban đã được điều chỉnh giữa kỳ",
                            $"Chỉ tiêu KPI {branchYearKpiRow.KpiDefinition.Name} năm {year} của phòng {detail.Name} đã được điều chỉnh. Vui lòng cân đối và phát hành lại chỉ tiêu nhân viên.",
                            NotificationSeverity.Important,
                            NotificationRelatedType.Target,
                            null));
                    }
                }
                catch (Exception ex) when (ex is NotFoundException or ForbiddenException)
                {
                    // Best-effort notification only.
                }
            }

            if (drafts.Count > 0)
            {
                await notifier.TryPublishAsync(drafts, cancellationToken);
            }
        }

        return await GetAllocationAsync(branchYearId, kpiId, userId, cancellationToken);
    }

    public async Task<EmployeeAllocationDto> AdjustEmployeeAllocationAsync(Guid deptYearId, AdjustEmployeeAllocationRequest request, Guid userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ChangeReason))
        {
            throw new ValidationException("Lý do điều chỉnh (changeReason) là bắt buộc.");
        }

        var (deptYear, departmentId, year, yearPeriodId, yearPeriod) = await LoadAndValidateDeptYearAsync(deptYearId, userId, cancellationToken);

        if (deptYear.Status != TargetAssignmentStatus.Active)
        {
            throw new ConflictException(
                "Chỉ tiêu phòng ban (cấp 2) chưa được phát hành.",
                ErrorCodes.TargetTierNotPublished);
        }

        var aggregation = deptYear.KpiDefinition.AggregationType;
        var kpiId = deptYear.KpiDefinitionId;
        var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken);

        var (eligibleEmployees, _, _) = await GetEligibleEmployeesAsync(departmentId, kpiId, yearPeriod.StartDate, cancellationToken);
        var eligibleEmployeeIds = eligibleEmployees.Select(e => e.Id).ToHashSet();
        var employeeNameById = eligibleEmployees.ToDictionary(e => e.Id, e => e.FullName);

        // Accepts ACTIVE rows (adjusting an already-published allocation) and DRAFT rows left by a tier-2 BM
        // adjustment that reopened this department's tier 3 as mismatched (D13) — both count as "already
        // published for this KPI" for the purposes of this endpoint; only a never-published KPI (no row at all,
        // or only SUPERSEDED history) is rejected below.
        var existingEmployeeYearRows = await db.TargetAssignments
            .Where(t => t.ParentAssignmentId == deptYear.Id && t.SplitDimension == TargetSplitDimension.Org
                && t.EmployeeId != null && t.KpiDefinitionId == kpiId && t.Status != TargetAssignmentStatus.Superseded)
            .ToListAsync(cancellationToken);

        if (existingEmployeeYearRows.Count == 0)
        {
            throw new ConflictException(
                "Chỉ tiêu nhân viên cho KPI này chưa được phát hành; chưa cần điều chỉnh giữa kỳ, hãy dùng lưu thông thường.",
                ErrorCodes.TargetInvalidState);
        }

        var employeeYearByEmployeeId = existingEmployeeYearRows.ToDictionary(r => r.EmployeeId!.Value, r => r);

        var requestEmployeeIds = request.Employees.Select(e => e.EmployeeId).ToList();
        if (requestEmployeeIds.Count != requestEmployeeIds.Distinct().Count())
        {
            throw new ValidationException("Duplicate EmployeeId in request.");
        }

        if (!requestEmployeeIds.ToHashSet().SetEquals(employeeYearByEmployeeId.Keys))
        {
            throw new ValidationException(
                "Yêu cầu điều chỉnh phải gồm đầy đủ các nhân viên đang được giao chỉ tiêu cho KPI này (không thêm/bớt).");
        }

        foreach (var empReq in request.Employees)
        {
            if (empReq.Year < 0 || empReq.Q1 < 0 || empReq.Q2 < 0 || empReq.Q3 < 0 || empReq.Q4 < 0)
            {
                throw new ValidationException("All allocation values must be >= 0.");
            }

            if (!eligibleEmployeeIds.Contains(empReq.EmployeeId))
            {
                throw new BusinessRuleException(
                    ErrorCodes.TargetEmployeeNotEligible,
                    $"Employee {empReq.EmployeeId} does not hold a responsible position for this KPI.",
                    422);
            }
        }

        decimal requestedYearTotal = 0m;
        var quarterMismatches = new List<string>();
        foreach (var empReq in request.Employees)
        {
            requestedYearTotal += empReq.Year;
            if (!QuartersMatch(aggregation, empReq.Year, [empReq.Q1, empReq.Q2, empReq.Q3, empReq.Q4]))
            {
                var empName = employeeNameById.GetValueOrDefault(empReq.EmployeeId, empReq.EmployeeId.ToString());
                quarterMismatches.Add(IsPeriodAdditive(aggregation)
                    ? $"Nhân viên {empName}: tổng 4 quý = {empReq.Q1 + empReq.Q2 + empReq.Q3 + empReq.Q4}, chỉ tiêu năm = {empReq.Year}."
                    : $"Nhân viên {empName}: mỗi quý phải bằng chỉ tiêu năm = {empReq.Year}.");
            }
        }

        if (requestedYearTotal != deptYear.TargetValue)
        {
            throw new BusinessRuleException(
                ErrorCodes.TargetEmployeeAllocationSumMismatch, null, 422,
                [$"Tổng chỉ tiêu các nhân viên = {requestedYearTotal}, chỉ tiêu phòng = {deptYear.TargetValue}."]);
        }

        if (quarterMismatches.Count > 0)
        {
            throw new BusinessRuleException(ErrorCodes.TargetEmployeeQuarterSumMismatch, null, 422, quarterMismatches);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var employeeYearIds = existingEmployeeYearRows.Select(r => r.Id).ToList();
        var existingEmployeeQuarterRows = await db.TargetAssignments
            .Where(t => t.SplitDimension == TargetSplitDimension.Period && t.KpiDefinitionId == kpiId
                && t.ParentAssignmentId != null && employeeYearIds.Contains(t.ParentAssignmentId.Value)
                && t.Status != TargetAssignmentStatus.Superseded)
            .ToListAsync(cancellationToken);
        var employeeQuarterByEmployeeAndPeriod = existingEmployeeQuarterRows
            .GroupBy(r => (r.ParentAssignmentId!.Value, r.EvaluationPeriodId))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var empReq in request.Employees)
        {
            var empYearRow = employeeYearByEmployeeId[empReq.EmployeeId];
            var quarterValues = new[] { empReq.Q1, empReq.Q2, empReq.Q3, empReq.Q4 };
            for (var i = 0; i < 4; i++)
            {
                var quarter = quarterPeriods[i];
                var isLocked = quarter.Status != PeriodStatus.Open || quarter.EndDate < today;
                if (isLocked)
                {
                    var stored = employeeQuarterByEmployeeAndPeriod.TryGetValue((empYearRow.Id, quarter.Id), out var ex) ? ex.TargetValue : 0m;
                    if (stored != quarterValues[i])
                    {
                        throw new ConflictException($"Quý {i + 1} đã khóa, không thể điều chỉnh.", ErrorCodes.PeriodLocked);
                    }
                }
            }
        }

        var changedByEmployee = await ResolveChangedByEmployeeAsync(userId, cancellationToken);
        var reconfirmEmployeeIds = new List<Guid>();

        await uow.ExecuteInTransactionAsync(async ct =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var empReq in request.Employees)
            {
                var empYearRow = employeeYearByEmployeeId[empReq.EmployeeId];
                var quarterValues = new[] { empReq.Q1, empReq.Q2, empReq.Q3, empReq.Q4 };
                var oldYear = empYearRow.TargetValue;

                var quarterChanged = false;
                for (var i = 0; i < 4; i++)
                {
                    var quarter = quarterPeriods[i];
                    var stored = employeeQuarterByEmployeeAndPeriod.TryGetValue((empYearRow.Id, quarter.Id), out var ex) ? ex.TargetValue : 0m;
                    if (stored != quarterValues[i])
                    {
                        quarterChanged = true;
                        break;
                    }
                }

                if (oldYear == empReq.Year && !quarterChanged)
                {
                    continue;
                }

                reconfirmEmployeeIds.Add(empReq.EmployeeId);

                empYearRow.Status = TargetAssignmentStatus.Superseded;
                empYearRow.EffectiveTo = today < empYearRow.EffectiveFrom ? empYearRow.EffectiveFrom : today;
                empYearRow.UpdatedAt = now;

                var newEmpYearRow = new TargetAssignment
                {
                    Id = Guid.NewGuid(),
                    ParentAssignmentId = deptYear.Id,
                    EmployeeId = empReq.EmployeeId,
                    KpiDefinitionId = kpiId,
                    EvaluationPeriodId = yearPeriodId,
                    TargetValue = empReq.Year,
                    SplitDimension = TargetSplitDimension.Org,
                    Status = TargetAssignmentStatus.Active,
                    Version = empYearRow.Version + 1,
                    AssignedAt = now,
                    ApprovedAt = now,
                    ChangeReason = request.ChangeReason,
                    CreatedAt = now,
                    UpdatedAt = now,
                    EffectiveFrom = today < empYearRow.EffectiveFrom ? empYearRow.EffectiveFrom : today,
                    PreviousVersionId = empYearRow.Id,
                    ChangedByEmployeeId = changedByEmployee?.Id
                };
                await uow.TargetAssignments.AddAsync(newEmpYearRow, ct);

                if (oldYear != empReq.Year)
                {
                    await changeLog.LogAsync(
                        userId, TargetChangeLogAction.Adjust, tier: 3, kpiId: kpiId,
                        unit: deptYear.KpiDefinition.Unit, period: "YEAR",
                        oldValue: oldYear, newValue: empReq.Year, basis: "MANUAL",
                        employeeId: empReq.EmployeeId, departmentId: departmentId, year: year, cancellationToken: ct);
                }

                for (var i = 0; i < 4; i++)
                {
                    var quarter = quarterPeriods[i];
                    var existingQuarter = employeeQuarterByEmployeeAndPeriod.TryGetValue((empYearRow.Id, quarter.Id), out var exq) ? exq : null;
                    var oldQuarterValue = existingQuarter?.TargetValue ?? 0m;

                    if (existingQuarter is not null)
                    {
                        existingQuarter.Status = TargetAssignmentStatus.Superseded;
                        existingQuarter.EffectiveTo = today < existingQuarter.EffectiveFrom ? existingQuarter.EffectiveFrom : today;
                        existingQuarter.UpdatedAt = now;
                    }

                    var newQuarter = new TargetAssignment
                    {
                        Id = Guid.NewGuid(),
                        ParentAssignmentId = newEmpYearRow.Id,
                        EmployeeId = empReq.EmployeeId,
                        KpiDefinitionId = kpiId,
                        EvaluationPeriodId = quarter.Id,
                        TargetValue = quarterValues[i],
                        SplitDimension = TargetSplitDimension.Period,
                        Status = TargetAssignmentStatus.Active,
                        Version = (existingQuarter?.Version ?? 0) + 1,
                        AssignedAt = now,
                        ApprovedAt = now,
                        ChangeReason = request.ChangeReason,
                        CreatedAt = now,
                        UpdatedAt = now,
                        EffectiveFrom = existingQuarter is not null && today < existingQuarter.EffectiveFrom ? existingQuarter.EffectiveFrom : today,
                        PreviousVersionId = existingQuarter?.Id,
                        ChangedByEmployeeId = changedByEmployee?.Id
                    };
                    await uow.TargetAssignments.AddAsync(newQuarter, ct);

                    if (oldQuarterValue != quarterValues[i])
                    {
                        await changeLog.LogAsync(
                            userId, TargetChangeLogAction.Adjust, tier: 3, kpiId: kpiId,
                            unit: deptYear.KpiDefinition.Unit, period: $"Q{i + 1}",
                            oldValue: oldQuarterValue, newValue: quarterValues[i], basis: "MANUAL",
                            employeeId: empReq.EmployeeId, departmentId: departmentId, year: year, cancellationToken: ct);
                    }
                }
            }

            if (reconfirmEmployeeIds.Count > 0)
            {
                var acks = await db.EmployeeTargetAcks
                    .Where(a => a.Year == year && reconfirmEmployeeIds.Contains(a.EmployeeId))
                    .ToListAsync(ct);
                var ackByEmployee = acks.ToDictionary(a => a.EmployeeId);

                foreach (var employeeId in reconfirmEmployeeIds)
                {
                    if (ackByEmployee.TryGetValue(employeeId, out var ack))
                    {
                        if (ack.Status is EmployeeTargetAckStatus.Confirmed or EmployeeTargetAckStatus.Pending)
                        {
                            ack.Status = EmployeeTargetAckStatus.Reconfirm;
                            ack.ConfirmedAt = null;
                            ack.UpdatedAt = now;
                        }
                    }
                    else
                    {
                        db.EmployeeTargetAcks.Add(new EmployeeTargetAck
                        {
                            Id = Guid.NewGuid(),
                            EmployeeId = employeeId,
                            Year = year,
                            Status = EmployeeTargetAckStatus.Reconfirm,
                            CreatedAt = now,
                            UpdatedAt = now
                        });
                    }
                }
            }

            await uow.SaveChangesAsync(ct);
        }, cancellationToken);

        // D12/D13: tell each reconfirm-flipped employee their targets changed.
        if (reconfirmEmployeeIds.Count > 0)
        {
            var drafts = reconfirmEmployeeIds.Distinct()
                .Select(employeeId => new NotificationDraft(
                    employeeId,
                    NotificationType.TargetRevised,
                    "Chỉ tiêu của bạn đã được điều chỉnh giữa kỳ",
                    $"Chỉ tiêu KPI {deptYear.KpiDefinition.Name} năm {year} của bạn đã được điều chỉnh. Vui lòng xác nhận lại.",
                    NotificationSeverity.Important,
                    NotificationRelatedType.Target,
                    null))
                .ToList();
            await notifier.TryPublishAsync(drafts, cancellationToken);
        }

        return await GetEmployeeAllocationAsync(deptYearId, userId, cancellationToken);
    }

    public async Task<RemindDepartmentManagerResultDto> RemindDepartmentManagerAsync(Guid branchYearId, Guid departmentId, Guid userId, CancellationToken cancellationToken)
    {
        var (_, branchId, year, _, _) = await LoadAndValidateBranchYearAsync(branchYearId, userId, cancellationToken);

        var department = await db.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == departmentId, cancellationToken)
            ?? throw new NotFoundException($"Department {departmentId} not found.");

        if (department.ParentDepartmentId != branchId || department.LevelType != DepartmentLevelType.Department)
        {
            throw new ValidationException(ErrorCodes.TargetDepartmentNotInBranch);
        }

        DepartmentManagerDto? manager = null;
        try
        {
            var detail = await departmentService.GetDepartmentAsync(userId, departmentId, cancellationToken);
            manager = detail.Manager;
        }
        catch (Exception ex) when (ex is NotFoundException or ForbiddenException)
        {
            // No resolvable department -> no-op below.
        }

        if (manager is null)
        {
            return new RemindDepartmentManagerResultDto(DateTimeOffset.UtcNow, null, false);
        }

        await notifier.TryPublishAsync(
            [
                new NotificationDraft(
                    manager.Id,
                    NotificationType.TargetRevised,
                    "Nhắc nhở: cân đối chỉ tiêu nhân viên",
                    $"Vui lòng cân đối và phát hành chỉ tiêu nhân viên cho năm {year} của phòng {department.Name}.",
                    NotificationSeverity.Important,
                    NotificationRelatedType.Target,
                    null)
            ],
            cancellationToken);

        return new RemindDepartmentManagerResultDto(DateTimeOffset.UtcNow, manager.Id, true);
    }

    public async Task<ProgressRollupDto> GetProgressRollupAsync(Guid branchYearId, Guid userId, CancellationToken cancellationToken)
    {
        var (branchYear, branchId, year, yearPeriodId, yearPeriod) = await LoadAndValidateBranchYearAsync(branchYearId, userId, cancellationToken);

        var branchYearRows = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.KpiDefinition).ThenInclude(k => k.KpiGroup)
            .Where(t => t.DepartmentId == branchId && t.EvaluationPeriodId == yearPeriodId
                && t.ParentAssignmentId == null && t.SplitDimension == null
                && (t.Status == TargetAssignmentStatus.Draft || t.Status == TargetAssignmentStatus.Active))
            .OrderBy(t => t.KpiDefinition.KpiGroup.Code)
            .ThenBy(t => t.KpiDefinition.Code)
            .ToListAsync(cancellationToken);

        var departments = await db.Departments
            .AsNoTracking()
            .Where(d => d.ParentDepartmentId == branchId && d.LevelType == DepartmentLevelType.Department)
            .OrderBy(d => d.Code)
            .ToListAsync(cancellationToken);

        var kpiResults = new List<ProgressRollupKpiDto>();
        foreach (var branchYearRow in branchYearRows.Where(r => IsAllocatedToDepartments(r.KpiDefinition.AggregationType)))
        {
            var kpi = branchYearRow.KpiDefinition;

            var deptYearRows = await db.TargetAssignments
                .AsNoTracking()
                .Where(t => t.ParentAssignmentId == branchYearRow.Id && t.SplitDimension == TargetSplitDimension.Org
                    && t.KpiDefinitionId == kpi.Id && t.Status == TargetAssignmentStatus.Active)
                .ToListAsync(cancellationToken);
            var deptYearByDeptId = deptYearRows.ToDictionary(r => r.DepartmentId!.Value, r => r);

            var departmentRows = new List<ProgressRollupDepartmentRowDto>();
            foreach (var dept in departments)
            {
                if (!deptYearByDeptId.TryGetValue(dept.Id, out var deptYearRow))
                {
                    continue;
                }

                // Reconciled (VALID) actuals only, over the department's whole tree for this KPI/year.
                var level1Ids = await db.TargetAssignments
                    .AsNoTracking()
                    .Where(t => t.ParentAssignmentId == deptYearRow.Id)
                    .Select(t => t.Id)
                    .ToListAsync(cancellationToken);
                var level2Ids = level1Ids.Count == 0
                    ? []
                    : await db.TargetAssignments
                        .AsNoTracking()
                        .Where(t => t.ParentAssignmentId != null && level1Ids.Contains(t.ParentAssignmentId.Value))
                        .Select(t => t.Id)
                        .ToListAsync(cancellationToken);
                var treeIds = new List<Guid> { deptYearRow.Id };
                treeIds.AddRange(level1Ids);
                treeIds.AddRange(level2Ids);

                var achieved = await db.ActualProgresses
                    .AsNoTracking()
                    .Where(a => a.Status == ActualProgressStatus.Valid && treeIds.Contains(a.TargetAssignmentId)
                        && a.ProgressDate >= yearPeriod.StartDate && a.ProgressDate <= yearPeriod.EndDate)
                    .SumAsync(a => a.Value, cancellationToken);

                var employeeYearRows = await db.TargetAssignments
                    .AsNoTracking()
                    .Where(t => t.ParentAssignmentId == deptYearRow.Id && t.SplitDimension == TargetSplitDimension.Org
                        && t.EmployeeId != null && t.Status == TargetAssignmentStatus.Active)
                    .ToListAsync(cancellationToken);

                var allocatedToEmployees = employeeYearRows.Sum(r => r.TargetValue);
                var employeeIds = employeeYearRows.Select(r => r.EmployeeId!.Value).Distinct().ToList();
                var confirmedCount = employeeIds.Count == 0
                    ? 0
                    : await db.EmployeeTargetAcks
                        .Where(a => a.Year == year && employeeIds.Contains(a.EmployeeId) && a.Status == EmployeeTargetAckStatus.Confirmed)
                        .CountAsync(cancellationToken);

                departmentRows.Add(new ProgressRollupDepartmentRowDto(
                    DepartmentId: dept.Id,
                    DepartmentName: dept.Name,
                    DeptYearId: deptYearRow.Id,
                    Target: deptYearRow.TargetValue,
                    Achieved: achieved,
                    AchievedPercent: deptYearRow.TargetValue > 0 ? Math.Round(achieved / deptYearRow.TargetValue * 100m, 1) : 0m,
                    AllocatedToEmployeesPercent: deptYearRow.TargetValue > 0 ? Math.Round(allocatedToEmployees / deptYearRow.TargetValue * 100m, 1) : 0m,
                    EmployeesTotal: employeeIds.Count,
                    EmployeesConfirmed: confirmedCount));
            }

            kpiResults.Add(new ProgressRollupKpiDto(kpi.Id, kpi.Code, kpi.Name, kpi.Unit, departmentRows));
        }

        return new ProgressRollupDto(branchYearId, year, kpiResults);
    }

    // =========================================================================
    // Phase 2.1 allocation (departments, quarters, confirm, tree)
    // =========================================================================

    private Task<HashSet<Guid>?> ResolveVisibleAsync(Guid userId, CancellationToken ct) =>
        DepartmentAccessGuard.ResolveVisibleAsync(uow, permissions, userId, ct);

    private Task EnsureDepartmentAccessAsync(Guid userId, Guid? departmentId, CancellationToken ct) =>
        DepartmentAccessGuard.EnsureDepartmentAccessAsync(uow, permissions, userId, departmentId, ct);

    private static bool IsVisibleRow(TargetAssignment row, HashSet<Guid>? visible) =>
        visible is null
        || row.ParentAssignmentId == null
        || (row.DepartmentId.HasValue && visible.Contains(row.DepartmentId.Value));

    public async Task<List<TargetNodeDto>> AllocateDepartmentsAsync(Guid branchYearId, AllocateDepartmentsDto dto, Guid userId, CancellationToken ct = default)
    {
        var branchYearTarget = await uow.TargetAssignments.GetWithContextAsync(branchYearId, ct);
        if (branchYearTarget == null || branchYearTarget.ParentAssignmentId != null)
            throw new NotFoundException("Branch-year target not found.");

        await EnsureDepartmentAccessAsync(userId, branchYearTarget.DepartmentId, ct);

        if (branchYearTarget.EvaluationPeriod?.Status != PeriodStatus.Open)
            throw new ConflictException("Evaluation period is not open.", ErrorCodes.TargetPeriodNotOpen);

        if (dto.Items is not { Count: > 0 })
            throw new ValidationException("Danh sách phân bổ phải có ít nhất một phòng ban.");

        var deptIds = dto.Items.Select(i => i.DepartmentId).Distinct().ToList();
        var depts = await uow.Departments.ListAsync(d => deptIds.Contains(d.Id), ct);

        foreach (var deptId in deptIds)
        {
            var dept = depts.FirstOrDefault(d => d.Id == deptId);
            if (dept == null || dept.ParentDepartmentId != branchYearTarget.DepartmentId)
                throw new BusinessRuleException(ErrorCodes.TargetDepartmentNotInBranch, null, 422);
        }

        var byKpi = dto.Items.GroupBy(i => i.KpiDefinitionId).ToList();
        if (byKpi.Count > 1 || byKpi[0].Key != branchYearTarget.KpiDefinitionId)
            throw new ValidationException("Request may only contain the branch year target's own KPI.");

        foreach (var item in dto.Items)
        {
            var clash = await uow.TargetAssignments.AnyAsync(
                t => t.KpiDefinitionId == item.KpiDefinitionId && t.DepartmentId == item.DepartmentId
                     && t.EvaluationPeriodId == branchYearTarget.EvaluationPeriodId && t.ParentAssignmentId != branchYearTarget.Id,
                ct);
            if (clash)
                throw new ConflictException("A year target for this department and KPI already exists outside this allocation.", ErrorCodes.TargetInvalidState);
        }

        var totalTarget = dto.Items.Sum(i => i.TargetValue);
        if (totalTarget < branchYearTarget.TargetValue)
            throw new BusinessRuleException(ErrorCodes.TargetDepartmentSumBelow, null, 422);

        await uow.ExecuteInTransactionAsync(async transaction =>
        {
            foreach (var item in dto.Items)
            {
                var existing = (await uow.TargetAssignments.GetCurrentChildrenAsync(branchYearTarget.Id, transaction))
                    .FirstOrDefault(c => c.DepartmentId == item.DepartmentId && c.KpiDefinitionId == item.KpiDefinitionId);

                if (existing != null)
                {
                    if (existing.Status == TargetAssignmentStatus.Draft)
                    {
                        existing.TargetValue = item.TargetValue;
                    }
                    else if (existing.Status == TargetAssignmentStatus.Active)
                    {
                        if (string.IsNullOrWhiteSpace(dto.ChangeReason))
                            throw new ValidationException("changeReason is required when changing an active target.");

                        var descendants = await uow.TargetAssignments.GetActiveDescendantsAsync(existing.Id, transaction);
                        foreach (var desc in descendants)
                            desc.Status = TargetAssignmentStatus.Superseded;
                        existing.Status = TargetAssignmentStatus.Superseded;

                        var newRow = new TargetAssignment
                        {
                            Id = Guid.NewGuid(),
                            DepartmentId = item.DepartmentId,
                            KpiDefinitionId = item.KpiDefinitionId,
                            EvaluationPeriodId = branchYearTarget.EvaluationPeriodId,
                            ParentAssignmentId = branchYearTarget.Id,
                            SplitDimension = TargetSplitDimension.Org,
                            TargetValue = item.TargetValue,
                            Version = existing.Version + 1,
                            Status = TargetAssignmentStatus.Draft,
                            // Not assigned yet (still a draft): leave AssignedAt null. Setting it to a pre-save
                            // DateTimeOffset.UtcNow here raced the AuditSaveChangesInterceptor's CreatedAt stamp
                            // (captured later, at the batch's SaveChangesAsync), occasionally breaking
                            // ck_target_assignment_assigned_time under load.
                            ChangeReason = dto.ChangeReason,
                            EffectiveFrom = branchYearTarget.EvaluationPeriod!.StartDate,
                        };
                        await uow.TargetAssignments.AddAsync(newRow, transaction);
                    }
                }
                else
                {
                    var newRow = new TargetAssignment
                    {
                        Id = Guid.NewGuid(),
                        DepartmentId = item.DepartmentId,
                        KpiDefinitionId = item.KpiDefinitionId,
                        EvaluationPeriodId = branchYearTarget.EvaluationPeriodId,
                        ParentAssignmentId = branchYearTarget.Id,
                        SplitDimension = TargetSplitDimension.Org,
                        TargetValue = item.TargetValue,
                        Version = 1,
                        Status = TargetAssignmentStatus.Draft,
                        // Not assigned yet; see the comment above.
                        EffectiveFrom = branchYearTarget.EvaluationPeriod!.StartDate,
                    };
                    await uow.TargetAssignments.AddAsync(newRow, transaction);
                }
            }
        }, ct);

        var tree = await GetTreeAsync(branchYearTarget.DepartmentId!.Value, int.Parse(branchYearTarget.EvaluationPeriod!.Code, System.Globalization.CultureInfo.InvariantCulture), null, userId, ct);
        return tree;
    }

    public async Task<List<TargetNodeDto>> AllocateQuartersAsync(Guid deptYearId, AllocateQuartersDto dto, Guid userId, CancellationToken ct = default)
    {
        var deptYearTarget = await uow.TargetAssignments.GetWithContextAsync(deptYearId, ct);
        if (deptYearTarget == null || deptYearTarget.ParentAssignmentId == null)
            throw new NotFoundException("Department-year target not found.");

        await EnsureDepartmentAccessAsync(userId, deptYearTarget.DepartmentId, ct);

        var branchYearTarget = await uow.TargetAssignments.GetWithContextAsync(deptYearTarget.ParentAssignmentId.Value, ct);
        if (branchYearTarget?.ParentAssignmentId != null)
            throw new ValidationException("Branch year target reference invalid.");

        if (deptYearTarget.EvaluationPeriod?.Status != PeriodStatus.Open)
            throw new ConflictException("Evaluation period is not open.", ErrorCodes.TargetPeriodNotOpen);

        var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(
            PeriodType.Quarter,
            branchYearTarget!.EvaluationPeriod!.StartDate,
            branchYearTarget.EvaluationPeriod.EndDate,
            ct);

        foreach (var item in dto.Items)
        {
            var clash = await uow.TargetAssignments.AnyAsync(
                t => t.KpiDefinitionId == deptYearTarget.KpiDefinitionId && t.DepartmentId == deptYearTarget.DepartmentId
                     && t.EvaluationPeriodId == item.QuarterPeriodId && t.ParentAssignmentId != deptYearTarget.Id,
                ct);
            if (clash)
                throw new ConflictException("A target for this department, KPI and quarter already exists outside this allocation.", ErrorCodes.TargetInvalidState);
        }

        var totalTarget = dto.Items.Sum(i => i.TargetValue);
        if (totalTarget < deptYearTarget.TargetValue)
            throw new BusinessRuleException(ErrorCodes.TargetQuarterSumBelow, null, 422);

        await uow.ExecuteInTransactionAsync(async transaction =>
        {
            foreach (var item in dto.Items)
            {
                var quarterPeriod = quarterPeriods.FirstOrDefault(q => q.Id == item.QuarterPeriodId);
                if (quarterPeriod == null)
                    throw new ValidationException("Quarter period not in valid range.");

                var existing = (await uow.TargetAssignments.GetCurrentChildrenAsync(deptYearTarget.Id, transaction))
                    .FirstOrDefault(c => c.EvaluationPeriodId == item.QuarterPeriodId);

                if (existing != null)
                {
                    if (existing.Status == TargetAssignmentStatus.Draft)
                    {
                        existing.TargetValue = item.TargetValue;
                    }
                    else if (existing.Status == TargetAssignmentStatus.Active)
                    {
                        if (string.IsNullOrWhiteSpace(dto.ChangeReason))
                            throw new ValidationException("changeReason is required when changing an active target.");

                        var descendants = await uow.TargetAssignments.GetActiveDescendantsAsync(existing.Id, transaction);
                        foreach (var desc in descendants)
                            desc.Status = TargetAssignmentStatus.Superseded;
                        existing.Status = TargetAssignmentStatus.Superseded;

                        var newRow = new TargetAssignment
                        {
                            Id = Guid.NewGuid(),
                            DepartmentId = deptYearTarget.DepartmentId,
                            KpiDefinitionId = deptYearTarget.KpiDefinitionId,
                            EvaluationPeriodId = item.QuarterPeriodId,
                            ParentAssignmentId = deptYearTarget.Id,
                            SplitDimension = TargetSplitDimension.Period,
                            TargetValue = item.TargetValue,
                            Version = existing.Version + 1,
                            Status = TargetAssignmentStatus.Draft,
                            // Not assigned yet (still a draft); see the AllocateDepartmentsAsync comment above.
                            ChangeReason = dto.ChangeReason,
                            EffectiveFrom = quarterPeriod.StartDate,
                        };
                        await uow.TargetAssignments.AddAsync(newRow, transaction);
                    }
                }
                else
                {
                    var newRow = new TargetAssignment
                    {
                        Id = Guid.NewGuid(),
                        DepartmentId = deptYearTarget.DepartmentId,
                        KpiDefinitionId = deptYearTarget.KpiDefinitionId,
                        EvaluationPeriodId = item.QuarterPeriodId,
                        ParentAssignmentId = deptYearTarget.Id,
                        SplitDimension = TargetSplitDimension.Period,
                        TargetValue = item.TargetValue,
                        Version = 1,
                        Status = TargetAssignmentStatus.Draft,
                        // Not assigned yet; see the AllocateDepartmentsAsync comment above.
                        EffectiveFrom = quarterPeriod.StartDate,
                    };
                    await uow.TargetAssignments.AddAsync(newRow, transaction);
                }
            }
        }, ct);

        var tree = await GetTreeAsync(branchYearTarget.DepartmentId!.Value, int.Parse(branchYearTarget.EvaluationPeriod!.Code, System.Globalization.CultureInfo.InvariantCulture), null, userId, ct);
        return tree;
    }

    public async Task<TargetNodeDto> ConfirmAsync(Guid deptYearId, Guid userId, CancellationToken ct = default)
    {
        var deptYearTarget = await uow.TargetAssignments.GetWithContextAsync(deptYearId, ct);
        if (deptYearTarget == null)
            throw new NotFoundException("Department-year target not found.");

        await EnsureDepartmentAccessAsync(userId, deptYearTarget.DepartmentId, ct);

        // Confirming rewrites the quarter and month targets, so a locked year (or any locked quarter) is read-only.
        PeriodLockGuard.EnsureWritable(deptYearTarget.EvaluationPeriod);

        var branchYearTarget = await uow.TargetAssignments.GetWithContextAsync(deptYearTarget.ParentAssignmentId!.Value, ct);

        var quarterTargets = await uow.TargetAssignments.GetCurrentChildrenAsync(deptYearTarget.Id, ct);
        if (quarterTargets.Count == 0)
            throw new ConflictException("Department has no quarter allocation to confirm.", ErrorCodes.TargetInvalidState);

        // The month and day rows are generated under each quarter, so a locked quarter, month or day blocks the confirm.
        foreach (var quarterTarget in quarterTargets)
        {
            PeriodLockGuard.EnsureWritable(quarterTarget.EvaluationPeriod);

            var quarter = quarterTarget.EvaluationPeriod!;
            foreach (var childType in new[] { PeriodType.Month, PeriodType.Day })
            {
                foreach (var childPeriod in await uow.EvaluationPeriods.GetInRangeAsync(childType, quarter.StartDate, quarter.EndDate, ct))
                    PeriodLockGuard.EnsureWritable(childPeriod);
            }
        }

        // Unique index (kpi, department, period, version): new rows must take the next free version.
        var versions = (await uow.TargetAssignments.ListAsync(
                t => t.KpiDefinitionId == deptYearTarget.KpiDefinitionId && t.DepartmentId == deptYearTarget.DepartmentId, ct))
            .GroupBy(t => t.EvaluationPeriodId)
            .ToDictionary(g => g.Key, g => g.Max(t => t.Version));

        // DB rule: ACTIVE children of an ACTIVE SUM-KPI parent must sum exactly to the parent target.
        var isSum = deptYearTarget.KpiDefinition?.AggregationType == KpiAggregationType.Sum;
        if (isSum && quarterTargets.Sum(c => c.TargetValue) != deptYearTarget.TargetValue)
            throw new BusinessRuleException(ErrorCodes.TargetInvalidState, "Quarter targets must sum exactly to the department year target before confirming.", 422);

        var deptYearTargets = await uow.TargetAssignments.GetCurrentChildrenAsync(branchYearTarget!.Id, ct);
        var branchReady = deptYearTargets.All(s => s.Id == deptYearTarget.Id || s.Status == TargetAssignmentStatus.Active);
        if (branchReady && isSum && deptYearTargets.Sum(c => c.TargetValue) != branchYearTarget.TargetValue)
            throw new BusinessRuleException(ErrorCodes.TargetInvalidState, "Department year targets must sum exactly to the branch year target before confirming.", 422);

        await uow.ExecuteInTransactionAsync(async transaction =>
        {
            if (branchReady)
            {
                branchYearTarget.Status = TargetAssignmentStatus.Active;
                branchYearTarget.ApprovedAt = DateTimeOffset.UtcNow;
            }

            deptYearTarget.Status = TargetAssignmentStatus.Active;
            deptYearTarget.ApprovedAt = DateTimeOffset.UtcNow;

            foreach (var quarterTarget in quarterTargets)
            {
                var staleChildTargets = await uow.TargetAssignments.GetActiveDescendantsAsync(quarterTarget.Id, transaction);
                foreach (var staleChild in staleChildTargets)
                    staleChild.Status = TargetAssignmentStatus.Superseded;

                quarterTarget.Status = TargetAssignmentStatus.Active;
                quarterTarget.ApprovedAt = DateTimeOffset.UtcNow;

                var months = await uow.EvaluationPeriods.GetInRangeAsync(
                    PeriodType.Month,
                    quarterTarget.EvaluationPeriod!.StartDate,
                    quarterTarget.EvaluationPeriod.EndDate,
                    transaction);

                var monthParts = months.Select(m => (m.StartDate, m.EndDate)).ToList();
                var monthValues = SplitByWorkingDays(quarterTarget.TargetValue, monthParts);

                for (int i = 0; i < months.Count; i++)
                {
                    var monthId = Guid.NewGuid();
                    var monthTarget = new TargetAssignment
                    {
                        Id = monthId,
                        DepartmentId = deptYearTarget.DepartmentId,
                        KpiDefinitionId = deptYearTarget.KpiDefinitionId,
                        EvaluationPeriodId = months[i].Id,
                        ParentAssignmentId = quarterTarget.Id,
                        SplitDimension = TargetSplitDimension.Period,
                        TargetValue = monthValues[i],
                        Version = NextVersion(versions, months[i].Id),
                        Status = TargetAssignmentStatus.Active,
                        AssignedAt = DateTimeOffset.UtcNow,
                        ApprovedAt = DateTimeOffset.UtcNow,
                        EffectiveFrom = months[i].StartDate,
                    };
                    await uow.TargetAssignments.AddAsync(monthTarget, transaction);

                    // DB rules: DAY sits directly under MONTH and ACTIVE children must sum exactly to their parent,
                    // so only days are stored under a month. Week totals are derived from the day rows.
                    await AddDaysAsync(deptYearTarget, monthTarget, months[i].StartDate, months[i].EndDate, versions, transaction);
                }
            }
        }, ct);

        var tree = await GetTreeAsync(branchYearTarget!.DepartmentId!.Value, int.Parse(branchYearTarget.EvaluationPeriod!.Code, System.Globalization.CultureInfo.InvariantCulture), null, userId, ct);
        return FindNode(tree, deptYearTarget.Id) ?? throw new NotFoundException("Department-year target not found.");
    }

    public async Task<List<TargetNodeDto>> GetTreeAsync(Guid branchId, int year, int? compareYear, Guid userId, CancellationToken ct = default)
    {
        // Callers whose subtree holds the branch see the whole tree; a department inside the branch
        // sees the branch root plus its own subtree; anyone else is refused.
        var visible = await ResolveVisibleAsync(userId, ct);
        if (visible is not null && visible.Contains(branchId))
            visible = null;
        if (visible is not null
            && !visible.Overlaps(await uow.TargetAssignments.GetDescendantDepartmentIdsAsync(branchId, ct)))
            throw new ForbiddenException(DepartmentAccessGuard.AccessDenied);

        var yearPeriod = await uow.EvaluationPeriods.GetYearAsync(year, ct);
        if (yearPeriod == null)
            return [];

        var rows = (await uow.TargetAssignments.GetTreeAsync(branchId, yearPeriod.Id, ct))
            .Where(r => IsVisibleRow(r, visible))
            .ToList();

        var compareTree = new Dictionary<(Guid, Guid?, int), (decimal Target, decimal Actual)>();
        if (compareYear.HasValue)
        {
            var compareYearPeriod = await uow.EvaluationPeriods.GetYearAsync(compareYear.Value, ct);
            if (compareYearPeriod != null)
            {
                var compareRows = await uow.TargetAssignments.GetTreeAsync(branchId, compareYearPeriod.Id, ct);
                foreach (var row in compareRows.Where(r => r.Status != TargetAssignmentStatus.Superseded && IsVisibleRow(r, visible)))
                {
                    var key = (row.KpiDefinitionId, row.DepartmentId, GetPeriodPosition(row.EvaluationPeriod?.Code ?? ""));
                    var actual = row.ActualProgresses?.Sum(ap => ap.Value) ?? 0;
                    compareTree[key] = (row.TargetValue, actual);
                }
            }
        }

        var rootIds = rows.Where(r => r.ParentAssignmentId == null && r.Status != TargetAssignmentStatus.Superseded)
            .Select(r => r.Id)
            .ToList();

        var nodes = new List<TargetNodeDto>();
        foreach (var rootId in rootIds)
        {
            var node = BuildNode(rootId, rows, compareTree);
            if (node != null)
                nodes.Add(node);
        }

        return nodes;
    }

    private static TargetNodeDto? BuildNode(Guid id, IReadOnlyList<TargetAssignment> allRows, Dictionary<(Guid, Guid?, int), (decimal Target, decimal Actual)> compareTree)
    {
        var row = allRows.FirstOrDefault(r => r.Id == id);
        if (row == null || row.Status == TargetAssignmentStatus.Superseded)
            return null;

        var children = allRows
            .Where(r => r.ParentAssignmentId == id && r.Status != TargetAssignmentStatus.Superseded)
            .Select(c => BuildNode(c.Id, allRows, compareTree))
            .Where(c => c != null)
            .Cast<TargetNodeDto>()
            .ToList();

        // A node can hold both its own period split and same-period allocations to sub-units; count only the
        // allocations when present so the two are not summed together.
        var periodType = row.EvaluationPeriod?.PeriodType.ToEnumString() ?? "";
        var allocations = children.Where(c => c.PeriodType == periodType).ToList();
        var allocatedValue = (allocations.Count > 0 ? allocations : children.Where(c => c.PeriodType != PeriodType.Week.ToEnumString()))
            .Sum(c => c.TargetValue);
        var actualValue = row.ActualProgresses?.Sum(ap => ap.Value) ?? 0m;

        var compareKey = (row.KpiDefinitionId, row.DepartmentId, GetPeriodPosition(row.EvaluationPeriod?.Code ?? ""));
        decimal? compareTarget = null, compareActual = null;
        if (compareTree.TryGetValue(compareKey, out var compareValues))
        {
            compareTarget = compareValues.Target;
            compareActual = compareValues.Actual;
        }

        return new TargetNodeDto(
            row.Id,
            row.ParentAssignmentId,
            row.KpiDefinitionId,
            row.KpiDefinition?.Name ?? "",
            row.DepartmentId ?? Guid.Empty,
            row.Department?.Name ?? "",
            row.EvaluationPeriodId,
            row.EvaluationPeriod?.Code ?? "",
            row.EvaluationPeriod?.PeriodType.ToEnumString() ?? "",
            row.TargetValue,
            allocatedValue,
            actualValue > 0 ? actualValue : null,
            row.Version,
            row.Status.ToEnumString(),
            compareTarget,
            compareActual,
            children);
    }

    private static TargetNodeDto? FindNode(IEnumerable<TargetNodeDto> nodes, Guid id)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id)
                return node;
            var found = FindNode(node.Children, id);
            if (found != null)
                return found;
        }

        return null;
    }

    private static int NextVersion(Dictionary<Guid, int> versions, Guid periodId) =>
        versions.TryGetValue(periodId, out var v) ? v + 1 : 1;

    private static TargetAssignment NewPeriodRow(TargetAssignment deptYearTarget, Guid periodId, Guid parentId, decimal value, int version, DateOnly effectiveFrom) => new()
    {
        Id = Guid.NewGuid(),
        DepartmentId = deptYearTarget.DepartmentId,
        KpiDefinitionId = deptYearTarget.KpiDefinitionId,
        BusinessObjectiveId = deptYearTarget.BusinessObjectiveId,
        EvaluationPeriodId = periodId,
        ParentAssignmentId = parentId,
        SplitDimension = TargetSplitDimension.Period,
        TargetValue = value,
        Version = version,
        Status = TargetAssignmentStatus.Active,
        AssignedAt = DateTimeOffset.UtcNow,
        ApprovedAt = DateTimeOffset.UtcNow,
        EffectiveFrom = effectiveFrom,
    };

    private async Task AddDaysAsync(TargetAssignment deptYearTarget, TargetAssignment parent, DateOnly start, DateOnly end, Dictionary<Guid, int> versions, CancellationToken ct)
    {
        var days = await uow.EvaluationPeriods.GetInRangeAsync(PeriodType.Day, start, end, ct);
        if (days.Count == 0)
            return;

        var values = SplitByWorkingDays(parent.TargetValue, days.Select(d => (d.StartDate, d.EndDate)).ToList());
        for (int k = 0; k < days.Count; k++)
            await uow.TargetAssignments.AddAsync(NewPeriodRow(deptYearTarget, days[k].Id, parent.Id, values[k], NextVersion(versions, days[k].Id), days[k].StartDate), ct);
    }

    private static int GetPeriodPosition(string code)
    {
        if (int.TryParse(code, System.Globalization.CultureInfo.InvariantCulture, out var year))
            return 0;
        if (code.Contains("-Q"))
            return int.Parse(code.Split("-Q")[1], System.Globalization.CultureInfo.InvariantCulture) * 100;
        if (code.Contains('-'))
            return int.Parse(code.Split("-")[1], System.Globalization.CultureInfo.InvariantCulture) * 10;
        return 0;
    }

    internal static List<decimal> SplitByWorkingDays(decimal total, IReadOnlyList<(DateOnly Start, DateOnly End)> parts)
    {
        if (parts.Count == 0)
            return [];

        var workingDaysByPart = parts.Select(p => CountWorkingDays(p.Start, p.End)).ToList();
        var totalWorkingDays = workingDaysByPart.Sum();

        if (totalWorkingDays == 0)
        {
            var result = new List<decimal>();
            var perPart = total / parts.Count;
            for (int i = 0; i < parts.Count - 1; i++)
                result.Add(Math.Round(perPart, 2));
            result.Add(total - result.Sum());
            return result;
        }

        // The rounding remainder goes to the last part that has working days, so no part can go negative.
        var lastIdx = workingDaysByPart.FindLastIndex(w => w > 0);
        var split = new List<decimal>();
        var assigned = 0m;
        for (int i = 0; i < parts.Count; i++)
        {
            if (i == lastIdx)
            {
                split.Add(total - assigned);
                assigned = total;
                continue;
            }

            var value = workingDaysByPart[i] == 0 ? 0m : Math.Round(total * workingDaysByPart[i] / totalWorkingDays, 2);
            split.Add(value);
            assigned += value;
        }

        return split;
    }

    private static int CountWorkingDays(DateOnly start, DateOnly end)
    {
        int count = 0;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                count++;
        }
        return count;
    }
}
