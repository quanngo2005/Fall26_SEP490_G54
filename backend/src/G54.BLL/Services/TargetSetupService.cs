using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Validation;
using G54.BLL.Dtos.TargetAllocation;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// Implementation of target quarter split, allocation, and publish (Phase 2, docs/api/TARGET_ALLOCATION_CONTRACT.md, 2.A).
/// </summary>
public sealed class TargetSetupService(IUnitOfWork uow, AppDbContext db, IPermissionService permissions) : ITargetSetupService
{
    private const string AuditEntityName = "BranchYearTargetImport";

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
            .Include(t => t.KpiDefinition)
            .Where(t => t.DepartmentId == branchId
                && t.EvaluationPeriodId == yearPeriodId
                && t.ParentAssignmentId == null
                && t.SplitDimension == null)
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
        if (previousYearPeriod is not null)
        {
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
            var isRatio = kpi.AggregationType == KpiAggregationType.NoAggregation;

            decimal? q1 = null, q2 = null, q3 = null, q4 = null;

            if (isRatio)
            {
                // For ratio KPIs, quarters auto-fill with year value
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
            decimal sum = (q1 ?? 0) + (q2 ?? 0) + (q3 ?? 0) + (q4 ?? 0);
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

            rows.Add(new QuarterSplitRowDto(
                KpiId: branchYearRow.KpiDefinitionId,
                KpiCode: kpi.Code,
                KpiName: kpi.Name,
                Unit: kpi.Unit,
                IsRatio: isRatio,
                YearValue: branchYearRow.TargetValue,
                Q1: q1 ?? 0m,
                Q2: q2 ?? 0m,
                Q3: q3 ?? 0m,
                Q4: q4 ?? 0m,
                PreviousYear: previousYear_,
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
            Status: branchYear.Status.ToString().ToUpperInvariant(),
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

        // 2. Check status is Draft
        if (branchYear.Status != TargetAssignmentStatus.Draft)
        {
            throw new ConflictException("Chỉ tiêu năm của chi nhánh không còn ở trạng thái nháp.", ErrorCodes.TargetInvalidState);
        }

        // 3. Load all branch-year rows for validation (one query)
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

            // Check values >= 0
            if (row.Q1 < 0 || row.Q2 < 0 || row.Q3 < 0 || row.Q4 < 0)
            {
                throw new ValidationException("Quarter values must be >= 0.");
            }

            // If it's a ratio KPI, ignore it (will auto-fill later)
            if (branchYearRow.KpiDefinition.AggregationType == KpiAggregationType.NoAggregation)
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
            if (branchYearRow.KpiDefinition.AggregationType == KpiAggregationType.NoAggregation)
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
                        existingChild.TargetValue = quarterValues[i];
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
                            AssignedAt = now
                        };
                        await uow.TargetAssignments.AddAsync(newChild, ct);
                    }
                }
            }

            // Auto-fill ratio KPI quarter rows (all quarters = year value)
            foreach (var (kpiId, branchYearRow) in ratioKpisInBranchYear)
            {
                var now = DateTimeOffset.UtcNow;

                for (int i = 0; i < 4; i++)
                {
                    var quarter = quarterPeriods[i];
                    var key = (branchYearRow.Id, quarter.Id);

                    if (!childByParentAndPeriod.TryGetValue(key, out var existingChild) || existingChild == null)
                    {
                        // Create
                        var newChild = new TargetAssignment
                        {
                            Id = Guid.NewGuid(),
                            ParentAssignmentId = branchYearRow.Id,
                            DepartmentId = branchId,
                            KpiDefinitionId = kpiId,
                            EvaluationPeriodId = quarter.Id,
                            TargetValue = branchYearRow.TargetValue,
                            SplitDimension = TargetSplitDimension.Period,
                            Status = TargetAssignmentStatus.Draft,
                            Version = 1,
                            CreatedAt = now,
                            AssignedAt = now
                        };
                        await uow.TargetAssignments.AddAsync(newChild, ct);
                    }
                }
            }

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

        var kpi = branchYearKpiRow.KpiDefinition;

        // 3. Get branch values (year and quarters)
        var quarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(
            PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken);

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

            // matched = quarters sum to year value
            var quartersSum = deptQ1 + deptQ2 + deptQ3 + deptQ4;
            var matched = deptYear > 0 && quartersSum == deptYear;

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
                Matched: matched));

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

        // 2. Check status is Draft
        if (branchYear.Status != TargetAssignmentStatus.Draft)
        {
            throw new ConflictException("Chỉ tiêu năm của chi nhánh không còn ở trạng thái nháp.", ErrorCodes.TargetInvalidState);
        }

        // 3. Find the KPI branch-year row and check it's not a ratio
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

        if (branchYearKpiRow.KpiDefinition.AggregationType == KpiAggregationType.NoAggregation)
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

        // Verify all 4 quarters exist and check they sum exactly to year value
        decimal quarterSum = 0m;
        foreach (var quarter in quarterPeriods)
        {
            if (!branchQuarterByPeriod.TryGetValue(quarter.Id, out var qValue))
            {
                throw new ConflictException("Chỉ tiêu này chưa chia đủ 4 quý.", ErrorCodes.TargetQuartersNotReady);
            }
            quarterSum += qValue;
        }

        if (quarterSum != branchYearKpiRow.TargetValue)
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
            var deptQuarterSum = deptReq.Q1 + deptReq.Q2 + deptReq.Q3 + deptReq.Q4;
            if (deptQuarterSum != deptReq.Year)
            {
                var deptName = departmentNameById.GetValueOrDefault(deptReq.DepartmentId, deptReq.DepartmentId.ToString());
                deptQuarterMismatches.Add(
                    $"Phòng {deptName}: tổng 4 quý = {deptQuarterSum}, chỉ tiêu năm = {deptReq.Year}.");
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
                && t.KpiDefinitionId == kpiId)
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
                    deptYearRow.TargetValue = deptReq.Year;
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
                        AssignedAt = now
                    };
                    await uow.TargetAssignments.AddAsync(deptYearRow, ct);
                    deptYearByDeptId[deptReq.DepartmentId] = deptYearRow;
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
                            AssignedAt = now
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
        var nonRatioKpis = branchYearRows.Where(r => r.KpiDefinition.AggregationType != KpiAggregationType.NoAggregation).ToList();
        var ratioKpis = branchYearRows.Where(r => r.KpiDefinition.AggregationType == KpiAggregationType.NoAggregation).ToList();

        int totalNonRatioKpis = nonRatioKpis.Count;

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
            decimal quarterSum = 0m;
            int quarterCount = 0;

            foreach (var quarterPeriod in quarterPeriods)
            {
                var key = (kpiRow.Id, quarterPeriod.Id);
                if (quartersByParentAndPeriod.TryGetValue(key, out var qRow) && qRow != null)
                {
                    quarterSum += qRow.TargetValue;
                    quarterCount++;
                }
            }

            // Check if all 4 quarters exist and sum exactly to year value
            bool isDone = quarterCount == 4 && quarterSum == kpiRow.TargetValue;
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

        foreach (var kpiRow in nonRatioKpis)
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

                // Check dept-year's quarters sum to its value
                decimal deptQuarterSum = 0m;
                int deptQuarterCount = 0;

                foreach (var quarterPeriod in quarterPeriods)
                {
                    var key = (deptYear.Id, quarterPeriod.Id);
                    if (deptQuarterByDeptYearAndPeriod.TryGetValue(key, out var dq) && dq != null)
                    {
                        deptQuarterSum += dq.TargetValue;
                        deptQuarterCount++;
                    }
                }

                if (deptQuarterCount != 4 || deptQuarterSum != deptYear.TargetValue)
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
                    var branchQuarterValue = quartersByParentAndPeriod.TryGetValue((kpiRow.Id, quarterPeriod.Id), out var branchQuarter) && branchQuarter != null
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
            bool allocationDoneForKpi = allocationDoneByKpi.GetValueOrDefault(kpiRow.KpiDefinitionId, false);

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
        else if (allocationDone < totalNonRatioKpis)
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
            Allocation: new TargetSetupAllocationMetadataDto(allocationDone, totalNonRatioKpis),
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

}
