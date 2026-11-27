using ClosedXML.Excel;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Workflow;
using G54.BLL.Dtos.Evaluation;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// Department Evaluation &amp; Performance Dashboard (`/evaluations`) and its Excel export
/// (`docs/api/EVALUATION_CONTRACT.md`).
/// </summary>
public sealed partial class EvaluationService
{
    private const int ExportHeaderRow = 3;
    private static readonly XLColor ExportHeaderFill = XLColor.FromHtml("#1F4E78");
    private static readonly XLColor ExportZebraFill = XLColor.FromHtml("#F2F6FA");
    private static readonly XLColor ExportBorderColor = XLColor.FromHtml("#BFC9D6");
    private static readonly XLColor ExportMutedText = XLColor.FromHtml("#6B7280");
    private static readonly (string Fill, string Font) ExportGoodTone = ("#E2F0D9", "#375623");
    private static readonly (string Fill, string Font) ExportInfoTone = ("#DDEBF7", "#1F4E78");
    private static readonly (string Fill, string Font) ExportWarnTone = ("#FFF2CC", "#7F6000");
    private static readonly (string Fill, string Font) ExportBadTone = ("#FCE4D6", "#C00000");
    private static readonly (string Fill, string Font) ExportNeutralTone = ("#EDEDED", "#404040");

    public async Task<DepartmentEvaluationDashboardDto> GetDepartmentEvaluationDashboardAsync(
        Guid? periodId,
        Guid? comparePeriodId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureDepartmentManagerOnlyAsync(currentUserId, cancellationToken);
        var reviewer = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        return await BuildDepartmentEvaluationDashboardAsync(
            reviewer.DepartmentId,
            periodId,
            comparePeriodId,
            [],
            reviewer,
            currentUserId,
            cancellationToken);
    }

    public async Task<DepartmentEvaluationDashboardDto> GetBranchEvaluationDashboardAsync(
        Guid? periodId,
        Guid? comparePeriodId,
        Guid? departmentId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureBranchManagerOnlyAsync(currentUserId, cancellationToken);
        var reviewer = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), cancellationToken);

        var branchRootId = hierarchy.BranchRoot(reviewer.DepartmentId);
        var branchSubtree = hierarchy.Subtree(branchRootId);
        var availableDepartments = branchSubtree
            .Where(id => hierarchy.Nodes.TryGetValue(id, out var n)
                         && n.Level == DepartmentLevelType.Department
                         && n.Status == RecordStatus.Active)
            .Select(id => hierarchy.Nodes[id])
            .OrderBy(n => n.Code)
            .Select(n => new DepartmentEvaluationSelectOptionDto(n.Id, n.Code, n.Name))
            .ToList();

        Guid targetDepartmentId;
        if (departmentId.HasValue)
        {
            if (!hierarchy.Nodes.TryGetValue(departmentId.Value, out _)
                || !branchSubtree.Contains(departmentId.Value))
            {
                throw new NotFoundException("Phòng ban", departmentId.Value);
            }
            targetDepartmentId = departmentId.Value;
        }
        else
        {
            targetDepartmentId = availableDepartments.FirstOrDefault()?.Id ?? reviewer.DepartmentId;
        }

        return await BuildDepartmentEvaluationDashboardAsync(
            targetDepartmentId,
            periodId,
            comparePeriodId,
            availableDepartments,
            reviewer,
            currentUserId,
            cancellationToken);
    }

    private async Task<DepartmentEvaluationDashboardDto> BuildDepartmentEvaluationDashboardAsync(
        Guid departmentIdToUse,
        Guid? periodId,
        Guid? comparePeriodId,
        List<DepartmentEvaluationSelectOptionDto> availableDepartments,
        Employee reviewer,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), cancellationToken);
        if (!hierarchy.Nodes.TryGetValue(departmentIdToUse, out var deptNode))
        {
            throw new NotFoundException("Phòng ban", departmentIdToUse);
        }

        var subtree = hierarchy.Subtree(departmentIdToUse);
        var today = timeProvider.GetBusinessToday();
        var selectablePeriods = await LoadDashboardPeriodsAsync(cancellationToken);
        var period = await ResolveDashboardPeriodAsync(periodId, selectablePeriods, today, cancellationToken);
        var compareOptions = period is null
            ? []
            : selectablePeriods
                .Where(p => p.PeriodType == period.PeriodType && p.StartDate < period.StartDate)
                .OrderByDescending(p => p.StartDate)
                .ToList();
        var comparePeriod = ResolveDashboardComparePeriod(comparePeriodId, period, compareOptions);

        var activeEmployees = await db.Employees
            .AsNoTracking()
            .Include(e => e.JobPosition)
            .Where(e => subtree.Contains(e.DepartmentId) && e.Status == EmployeeStatus.Active)
            .OrderBy(e => e.EmployeeCode)
            .ToListAsync(cancellationToken);

        var managerName = ResolveDepartmentManagerName(activeEmployees) ?? reviewer.FullName;
        var isTeamDept = deptNode.Level == DepartmentLevelType.Team;
        var departmentDto = new DepartmentEvaluationDepartmentDto(
            deptNode.Id,
            deptNode.Code,
            deptNode.Name,
            managerName,
            deptNode.Level.ToString(),
            isTeamDept);

        var downgradeConfig = await LoadDowngradeConfigurationAsync(cancellationToken);
        var operations = await BuildDashboardOperationsAsync(subtree, period, today, activeEmployees.Count, cancellationToken);
        var previousOperations = period is null || comparePeriod is null
            ? new Dictionary<Guid, DashboardEmployeeOperations>()
            : (await BuildDashboardOperationsAsync(subtree, comparePeriod, today, activeEmployees.Count, cancellationToken)).EmployeeOps;

        if (period is null)
        {
            var emptyTiers = BuildDashboardTierItems(downgradeConfig.Tiers, []);
            return new DepartmentEvaluationDashboardDto(
                departmentDto,
                null,
                null,
                selectablePeriods.Select(ToPeriodOptionDto).ToList(),
                [],
                new DepartmentEvaluationPerformanceDto(
                    null,
                    null,
                    null,
                    0,
                    activeEmployees.Count,
                    activeEmployees.Count,
                    0,
                    0,
                    [],
                    []),
                operations.Overview,
                new DepartmentEvaluationTierDistributionDto(DefaultTierACapPercent, emptyTiers, []),
                [],
                [],
                timeProvider.GetUtcNow(),
                availableDepartments);
        }

        var employeeIds = activeEmployees.Select(e => e.Id).ToList();
        var evaluations = await db.PerformanceEvaluations
            .AsNoTracking()
            .Where(pe => pe.EvaluationPeriodId == period.Id && employeeIds.Contains(pe.EmployeeId))
            .ToListAsync(cancellationToken);
        var evalDict = evaluations.GroupBy(pe => pe.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        var previousScores = comparePeriod is null
            ? []
            : (await db.PerformanceEvaluations
                .AsNoTracking()
                .Where(pe => pe.EvaluationPeriodId == comparePeriod.Id && employeeIds.Contains(pe.EmployeeId))
                .Where(pe => pe.TotalScore != null && pe.Status != PerformanceEvaluationStatus.Draft)
                .Select(pe => new { pe.EmployeeId, Score = pe.TotalScore!.Value })
                .ToListAsync(cancellationToken))
                .GroupBy(x => x.EmployeeId)
                .ToDictionary(g => g.Key, g => g.First().Score);

        var previousEvalDict = comparePeriod is null
            ? new Dictionary<Guid, PerformanceEvaluation>()
            : (await db.PerformanceEvaluations
                .AsNoTracking()
                .Where(pe => pe.EvaluationPeriodId == comparePeriod.Id && employeeIds.Contains(pe.EmployeeId))
                .ToListAsync(cancellationToken))
                .GroupBy(pe => pe.EmployeeId)
                .ToDictionary(g => g.Key, g => g.First());

        var previousProfilesByPosition = comparePeriod is null
            ? new Dictionary<Guid, EvaluationProfile>()
            : await LoadProfilesByPositionAsync(
                activeEmployees.Select(e => e.JobPositionId),
                comparePeriod,
                cancellationToken);

        var profilesByPosition = await LoadProfilesByPositionAsync(
            activeEmployees.Select(e => e.JobPositionId),
            period,
            cancellationToken);
        var profileTreesByPosition = profilesByPosition.ToDictionary(
            kvp => kvp.Key,
            kvp => EvaluationProfileTree.Build(kvp.Value.EvaluationProfileItems));

        var risksByEmployee = await LoadRiskEventsByEmployeeAsync(employeeIds, period, cancellationToken);
        var openAppealSet = (await LoadOpenAppealEmployeesAsync(employeeIds, period, cancellationToken)).ToHashSet();

        var subjects = activeEmployees
            .Where(e => profilesByPosition.ContainsKey(e.JobPositionId))
            .Select(e => new PerformanceScoreSubject(
                e.Id,
                e.DepartmentId,
                profilesByPosition[e.JobPositionId],
                ParseStoredComment(evalDict.GetValueOrDefault(e.Id)?.Comment, logger).QualitativeScores))
            .ToList();
        var scoredMap = await calculator.CalculateAsync(subjects, period, cancellationToken);

        var previousScoredMap = comparePeriod is null
            ? new Dictionary<Guid, PerformanceScoreResult>()
            : (await calculator.CalculateAsync(
                activeEmployees
                    .Where(e => previousProfilesByPosition.ContainsKey(e.JobPositionId))
                    .Select(e => new PerformanceScoreSubject(
                        e.Id,
                        e.DepartmentId,
                        previousProfilesByPosition[e.JobPositionId],
                        ParseStoredComment(previousEvalDict.GetValueOrDefault(e.Id)?.Comment, logger).QualitativeScores))
                    .ToList(),
                comparePeriod,
                cancellationToken)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var profileGroups = BuildDashboardProfileGroupColumns(activeEmployees, profileTreesByPosition);

        var periodAcceptsEvaluation = period.Status is PeriodStatus.Open or PeriodStatus.InReview;
        var submitDepartments = periodAcceptsEvaluation
            ? await DepartmentsInSubmitScopeAsync(currentUserId, reviewer, hierarchy, cancellationToken)
            : null;

        var staffRows = new List<DepartmentEvaluationStaffRowDto>(activeEmployees.Count);
        var rankableCurrentScores = new List<decimal>();
        var rankableTiers = new List<string>();
        var pipCount = 0;
        var appealCount = 0;
        var unscoredCount = 0;
        var evaluatedCount = 0;

        foreach (var emp in activeEmployees)
        {
            evalDict.TryGetValue(emp.Id, out var pe);
            var scored = pe is null ? null : scoredMap.GetValueOrDefault(emp.Id);
            previousScoredMap.TryGetValue(emp.Id, out var previousScored);
            risksByEmployee.TryGetValue(emp.Id, out var empRisks);

            var (_, coachingPlan, _, calibData, _, _, _) = ParseStoredComment(pe?.Comment, logger);
            var hasOpenDispute = pe?.IsDisputedPending() == true || openAppealSet.Contains(emp.Id);
            var keepsStored = KeepsStoredScore(pe);
            var rawTotalScore = keepsStored ? pe!.TotalScore : scored?.TotalScore;
            var totalScore = RoundDashboardOrNull(rawTotalScore);

            var proposedTier = rawTotalScore.HasValue
                ? TierDeterminer.DetermineTier(rawTotalScore.Value, empRisks, downgradeConfig)
                : null;
            var tierCode = calibData?.CalibratedTier ?? proposedTier;
            var isPip = calibData?.CoachingFlag ?? (!string.IsNullOrWhiteSpace(coachingPlan) || tierCode is "C" or "D");

            string rowStatus;
            if (hasOpenDispute)
            {
                rowStatus = "APPEAL";
                appealCount++;
            }
            else if (!totalScore.HasValue || pe is null || pe.Status == PerformanceEvaluationStatus.Draft)
            {
                rowStatus = "PENDING";
                unscoredCount++;
            }
            else if (isPip)
            {
                rowStatus = "PIP";
                pipCount++;
            }
            else
            {
                rowStatus = "FINAL";
            }

            if (totalScore.HasValue)
            {
                rankableCurrentScores.Add(totalScore.Value);
                if (pe is not null && pe.Status != PerformanceEvaluationStatus.Draft)
                {
                    evaluatedCount++;
                }
                if (!string.IsNullOrWhiteSpace(tierCode))
                {
                    rankableTiers.Add(tierCode);
                }
            }

            decimal? prevScore = previousScores.TryGetValue(emp.Id, out var ps) ? RoundDashboard(ps) : null;
            var scoreDelta = ChangeDashboard(totalScore, prevScore);

            var empGroupScores = profileGroups
                .Select(col =>
                {
                    var groupMatch = scored?.Groups.FirstOrDefault(g => string.Equals(g.Name, col.Name, StringComparison.Ordinal));
                    var previousMatch = previousScored?.Groups.FirstOrDefault(g => string.Equals(g.Name, col.Name, StringComparison.Ordinal));
                    return new DepartmentEvaluationStaffGroupScoreDto(
                        col.Name,
                        col.DisplayOrder,
                         groupMatch?.WeightPercent ?? col.WeightPercent,
                        RoundDashboardOrNull(groupMatch?.Score),
                        RoundDashboardOrNull(previousMatch?.Score));
                })
                .ToList();

            var evalStatus = pe != null ? pe.Status.ToEnumString() : "NOT_STARTED";
            var canOpenForm = emp.Id != reviewer.Id
                && (submitDepartments is null || submitDepartments.Contains(emp.DepartmentId));

            var empOps = operations.EmployeeOps.GetValueOrDefault(emp.Id);
            var previousEmpOps = previousOperations.GetValueOrDefault(emp.Id);
            staffRows.Add(new DepartmentEvaluationStaffRowDto(
                emp.Id,
                emp.EmployeeCode,
                emp.FullName,
                emp.JobPositionId,
                emp.JobPosition?.Title ?? string.Empty,
                empGroupScores,
                totalScore,
                prevScore,
                scoreDelta,
                hasOpenDispute ? null : tierCode,
                empOps?.OnTimeSlaPercent,
                rowStatus,
                evalStatus,
                canOpenForm,
                empOps?.OnTimeCompletedCount ?? 0,
                empOps?.InProgressCount ?? 0,
                empOps?.OverdueCount ?? 0,
                previousEmpOps?.OnTimeSlaPercent));
        }

        var average = MeanDashboard(rankableCurrentScores);
        var previousAverage = MeanDashboard(previousScores.Values);
        var kpiGroups = await BuildDashboardKpiGroupsAsync(
            departmentIdToUse,
            subtree,
            period,
            comparePeriod,
            activeEmployees,
            profileTreesByPosition,
            staffRows,
            cancellationToken);
        var trend = await BuildDashboardTrendAsync(period, selectablePeriods, employeeIds, average, cancellationToken);

        var performance = new DepartmentEvaluationPerformanceDto(
            average,
            previousAverage,
            ChangeDashboard(average, previousAverage),
            evaluatedCount,
            activeEmployees.Count,
            unscoredCount,
            pipCount,
            appealCount,
            kpiGroups,
            trend);

        var tierItems = BuildDashboardTierItems(downgradeConfig.Tiers, rankableTiers);
        var actionAlerts = BuildDashboardActionAlerts(staffRows);
        var tierDistribution = new DepartmentEvaluationTierDistributionDto(
            DefaultTierACapPercent,
            tierItems,
            actionAlerts);

        return new DepartmentEvaluationDashboardDto(
            departmentDto,
            ToPeriodOptionDto(period),
            comparePeriod is null ? null : ToPeriodOptionDto(comparePeriod),
            selectablePeriods.Select(ToPeriodOptionDto).ToList(),
            compareOptions.Select(ToPeriodOptionDto).ToList(),
            performance,
            operations.Overview,
            tierDistribution,
            profileGroups,
            staffRows,
            timeProvider.GetUtcNow(),
            availableDepartments);
    }

    public async Task<DepartmentEvaluationDashboardExportDto> ExportDepartmentEvaluationDashboardAsync(
        Guid? periodId,
        Guid? comparePeriodId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var dashboard = await GetDepartmentEvaluationDashboardAsync(
            periodId,
            comparePeriodId,
            currentUserId,
            cancellationToken);

        return GenerateDashboardExport(dashboard);
    }

    public async Task<DepartmentEvaluationDashboardExportDto> ExportBranchEvaluationDashboardAsync(
        Guid? periodId,
        Guid? comparePeriodId,
        Guid? departmentId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var dashboard = await GetBranchEvaluationDashboardAsync(
            periodId,
            comparePeriodId,
            departmentId,
            currentUserId,
            cancellationToken);

        return GenerateDashboardExport(dashboard);
    }

    private static DepartmentEvaluationDashboardExportDto GenerateDashboardExport(DepartmentEvaluationDashboardDto dashboard)
    {
        using var workbook = new XLWorkbook();
        workbook.Style.Font.FontName = "Segoe UI";
        workbook.Style.Font.FontSize = 10;

        WriteDashboardSummarySheet(workbook.AddWorksheet("Tong quan"), dashboard);
        WriteDashboardKpiSheet(workbook.AddWorksheet("KPI"), dashboard);
        WriteDashboardTrendSheet(workbook.AddWorksheet("Xu huong"), dashboard);
        WriteDashboardTierAndAlertSheet(workbook.AddWorksheet("Xep loai"), dashboard);
        WriteDashboardOperationsSheet(workbook.AddWorksheet("Cong viec & SLA"), dashboard);
        WriteDashboardStaffSheet(workbook.AddWorksheet("Can bo"), dashboard);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var periodCode = dashboard.Period?.Code ?? "na";
        return new DepartmentEvaluationDashboardExportDto(
            stream.ToArray(),
            $"danh-gia-phong-{dashboard.Department.Code}-{periodCode}.xlsx");
    }

    /// <summary>
    /// Enforces that only a Department Manager (`evaluation:read` at `Dept` scope without `Branch` or `All` scope)
    /// can open `/evaluations`. `BRANCH_MANAGER` and `ADMIN` / `SYSTEM_ADMIN` are rejected with 403.
    /// </summary>
    private async Task EnsureDepartmentManagerOnlyAsync(Guid actorAccountId, CancellationToken cancellationToken)
    {
        var granted = await permissions.GetEffectivePermissionsAsync(actorAccountId, cancellationToken);
        var level = AccessScope.LevelFor(granted, "evaluation", "read");
        if (level != AccessScope.DeptLevel)
        {
            throw new ForbiddenException("Màn hình Đánh giá & Hiệu suất Phòng ban chỉ dành cho Trưởng phòng.");
        }
    }

    /// <summary>
    /// Enforces that only a Branch Manager (`evaluation:read` at `Branch` or `All` scope)
    /// can open `/branch-performance/evaluations`.
    /// </summary>
    private async Task EnsureBranchManagerOnlyAsync(Guid actorAccountId, CancellationToken cancellationToken)
    {
        var granted = await permissions.GetEffectivePermissionsAsync(actorAccountId, cancellationToken);
        var level = AccessScope.LevelFor(granted, "evaluation", "read");
        if (level < AccessScope.BranchLevel)
        {
            throw new ForbiddenException("Màn hình Đánh giá & Hiệu suất Chi nhánh chỉ dành cho Giám đốc Chi nhánh.");
        }
    }

    private async Task<List<EvaluationPeriod>> LoadDashboardPeriodsAsync(CancellationToken cancellationToken)
    {
        var periods = await db.EvaluationPeriods
            .AsNoTracking()
            .Where(p => p.PeriodType == PeriodType.Year
                || p.PeriodType == PeriodType.Quarter
                || p.PeriodType == PeriodType.Month)
            .ToListAsync(cancellationToken);

        return periods
            .OrderByDescending(p => p.StartDate)
            .ThenByDescending(p => (int)p.PeriodType)
            .ToList();
    }

    private async Task<EvaluationPeriod?> ResolveDashboardPeriodAsync(
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

            var unsupported = await db.EvaluationPeriods
                .AsNoTracking()
                .AnyAsync(p => p.Id == periodId.Value, cancellationToken);
            if (unsupported)
            {
                throw new ValidationException("Chỉ hỗ trợ kỳ đánh giá theo Năm, Quý hoặc Tháng cho màn hình phòng ban.");
            }

            throw new NotFoundException("Kỳ đánh giá", periodId.Value);
        }

        var openOrActive = selectable
            .Where(p => p.StartDate <= today && p.EndDate >= today)
            .OrderByDescending(p => p.Status is PeriodStatus.Open or PeriodStatus.InReview)
            .ThenBy(p => p.PeriodType switch
            {
                PeriodType.Month => 1,
                PeriodType.Quarter => 2,
                PeriodType.Year => 3,
                _ => 4,
            })
            .FirstOrDefault();
        if (openOrActive is not null)
        {
            return openOrActive;
        }

        var latestOpen = selectable
            .Where(p => p.Status is PeriodStatus.Open or PeriodStatus.InReview)
            .OrderBy(p => p.PeriodType switch
            {
                PeriodType.Month => 1,
                PeriodType.Quarter => 2,
                PeriodType.Year => 3,
                _ => 4,
            })
            .FirstOrDefault();
        if (latestOpen is not null)
        {
            return latestOpen;
        }

        return selectable
            .OrderBy(p => p.PeriodType switch
            {
                PeriodType.Month => 1,
                PeriodType.Quarter => 2,
                PeriodType.Year => 3,
                _ => 4,
            })
            .FirstOrDefault();
    }

    private static EvaluationPeriod? ResolveDashboardComparePeriod(
        Guid? comparePeriodId,
        EvaluationPeriod? period,
        List<EvaluationPeriod> compareOptions)
    {
        if (period is null)
        {
            return null;
        }

        if (comparePeriodId.HasValue)
        {
            var matched = compareOptions.FirstOrDefault(p => p.Id == comparePeriodId.Value);
            if (matched is null)
            {
                throw new ValidationException("Kỳ so sánh phải cùng loại và trước kỳ đánh giá đã chọn.");
            }

            return matched;
        }

        return compareOptions.FirstOrDefault();
    }

    private static EvaluationPeriodOptionDto ToPeriodOptionDto(EvaluationPeriod p) =>
        new(
            p.Id,
            p.Code,
            p.Name,
            p.StartDate,
            p.EndDate,
            p.PeriodType.ToEnumString(),
            p.Status.ToEnumString(),
            p.Status is PeriodStatus.Open or PeriodStatus.InReview);

    private static string? ResolveDepartmentManagerName(List<Employee> activeEmployees)
    {
        if (activeEmployees.Count == 0)
        {
            return null;
        }

        var insideIds = activeEmployees.Select(e => e.Id).ToHashSet();
        var directReports = activeEmployees
            .Where(e => e.ManagerId.HasValue && insideIds.Contains(e.ManagerId.Value))
            .GroupBy(e => e.ManagerId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var internalManager = activeEmployees
            .Where(e => (!e.ManagerId.HasValue || !insideIds.Contains(e.ManagerId.Value))
                && directReports.GetValueOrDefault(e.Id) > 0)
            .OrderByDescending(e => directReports[e.Id])
            .ThenBy(e => e.EmployeeCode, StringComparer.Ordinal)
            .FirstOrDefault();

        return internalManager?.FullName;
    }

    private async Task<(DepartmentEvaluationOperationsDto Overview, Dictionary<Guid, DashboardEmployeeOperations> EmployeeOps)> BuildDashboardOperationsAsync(
        List<Guid> subtree,
        EvaluationPeriod? period,
        DateOnly today,
        int staffCount,
        CancellationToken cancellationToken)
    {
        var openWorks = await db.Works
            .AsNoTracking()
            .Where(w => subtree.Contains(w.AssigneeEmployee.DepartmentId) && WorkStatusGroups.Open.Contains(w.Status))
            .Select(w => new { w.AssigneeEmployeeId, w.Status, w.PlannedEndDate })
            .ToListAsync(cancellationToken);

        var inProgressWorks = openWorks.Where(w => WorkStatusGroups.InExecution.Contains(w.Status)).ToList();
        var inProgressCount = inProgressWorks.Count;
        decimal? loadPerStaff = staffCount > 0 ? RoundDashboard((decimal)openWorks.Count / staffCount) : null;

        // Open overdue works with PlannedEndDate != null
        var openOverdueWithDeadline = openWorks
            .Where(w => w.PlannedEndDate.HasValue && (w.Status == WorkStatus.Overdue || w.PlannedEndDate.Value < today))
            .ToList();

        var onTimeByEmployee = new Dictionary<Guid, int>();
        var inProgressByEmployee = inProgressWorks
            .GroupBy(w => w.AssigneeEmployeeId)
            .ToDictionary(g => g.Key, g => g.Count());
        var overdueByEmployee = openOverdueWithDeadline
            .GroupBy(w => w.AssigneeEmployeeId)
            .ToDictionary(g => g.Key, g => g.Count());

        var onTimeCompletedCount = 0;
        var lateCompletedCount = 0;

        if (period is not null)
        {
            var start = StartOfBusinessDay(period.StartDate);
            var endExclusive = StartOfBusinessDay(period.EndDate.AddDays(1));

            var completedWithDeadline = await db.Works
                .AsNoTracking()
                .Where(w => subtree.Contains(w.AssigneeEmployee.DepartmentId)
                    && WorkStatusGroups.Completed.Contains(w.Status)
                    && w.PlannedEndDate != null
                    && w.UpdatedAt >= start
                    && w.UpdatedAt < endExclusive)
                .Select(w => new { w.AssigneeEmployeeId, PlannedEndDate = w.PlannedEndDate!.Value, w.UpdatedAt })
                .ToListAsync(cancellationToken);

            foreach (var work in completedWithDeadline)
            {
                var completedBusinessDate = DateOnly.FromDateTime(
                    work.UpdatedAt.ToOffset(TimeProviderExtensions.BusinessUtcOffset).DateTime);
                if (completedBusinessDate <= work.PlannedEndDate)
                {
                    onTimeCompletedCount++;
                    onTimeByEmployee[work.AssigneeEmployeeId] = onTimeByEmployee.GetValueOrDefault(work.AssigneeEmployeeId) + 1;
                }
                else
                {
                    lateCompletedCount++;
                    overdueByEmployee[work.AssigneeEmployeeId] = overdueByEmployee.GetValueOrDefault(work.AssigneeEmployeeId) + 1;
                }
            }
        }

        var overdueCount = openOverdueWithDeadline.Count + lateCompletedCount;
        var totalSlaTracked = onTimeCompletedCount + overdueCount;
        decimal? onTimeRatePercent = totalSlaTracked > 0
            ? RoundDashboard((decimal)onTimeCompletedCount / totalSlaTracked * 100m)
            : null;

        var employeeOps = new Dictionary<Guid, DashboardEmployeeOperations>();
        foreach (var empId in onTimeByEmployee.Keys
            .Union(inProgressByEmployee.Keys)
            .Union(overdueByEmployee.Keys))
        {
            var empOnTime = onTimeByEmployee.GetValueOrDefault(empId);
            var empInProgress = inProgressByEmployee.GetValueOrDefault(empId);
            var empOverdue = overdueByEmployee.GetValueOrDefault(empId);
            var empSlaTotal = empOnTime + empOverdue;
            decimal? empSlaRate = empSlaTotal > 0
                ? RoundDashboard((decimal)empOnTime / empSlaTotal * 100m)
                : null;
            employeeOps[empId] = new DashboardEmployeeOperations(empOnTime, empInProgress, empOverdue, empSlaRate);
        }

        return (
            new DepartmentEvaluationOperationsDto(
                onTimeCompletedCount,
                overdueCount,
                inProgressCount,
                onTimeRatePercent,
                loadPerStaff),
            employeeOps);
    }

    private async Task<EvaluationSlaSummaryDto> LoadEmployeeSlaSummaryAsync(
        Guid employeeId,
        EvaluationPeriod period,
        CancellationToken cancellationToken)
    {
        var today = timeProvider.GetBusinessToday();
        var openWorks = await db.Works
            .AsNoTracking()
            .Where(w => w.AssigneeEmployeeId == employeeId && WorkStatusGroups.Open.Contains(w.Status))
            .Select(w => new { w.Status, w.PlannedEndDate })
            .ToListAsync(cancellationToken);

        var inProgressCount = openWorks.Count(w => WorkStatusGroups.InExecution.Contains(w.Status));
        var openOverdueCount = openWorks.Count(w =>
            w.PlannedEndDate.HasValue && (w.Status == WorkStatus.Overdue || w.PlannedEndDate.Value < today));

        var start = StartOfBusinessDay(period.StartDate);
        var endExclusive = StartOfBusinessDay(period.EndDate.AddDays(1));

        var completedWithDeadline = await db.Works
            .AsNoTracking()
            .Where(w => w.AssigneeEmployeeId == employeeId
                && WorkStatusGroups.Completed.Contains(w.Status)
                && w.PlannedEndDate != null
                && w.UpdatedAt >= start
                && w.UpdatedAt < endExclusive)
            .Select(w => new { PlannedEndDate = w.PlannedEndDate!.Value, w.UpdatedAt })
            .ToListAsync(cancellationToken);

        var onTimeCompletedCount = 0;
        var lateCompletedCount = 0;
        foreach (var work in completedWithDeadline)
        {
            var completedBusinessDate = DateOnly.FromDateTime(
                work.UpdatedAt.ToOffset(TimeProviderExtensions.BusinessUtcOffset).DateTime);
            if (completedBusinessDate <= work.PlannedEndDate)
            {
                onTimeCompletedCount++;
            }
            else
            {
                lateCompletedCount++;
            }
        }

        var overdueCount = openOverdueCount + lateCompletedCount;
        var totalSlaTracked = onTimeCompletedCount + overdueCount;
        decimal? onTimeRatePercent = totalSlaTracked > 0
            ? RoundDashboard((decimal)onTimeCompletedCount / totalSlaTracked * 100m)
            : null;

        return new EvaluationSlaSummaryDto(
            onTimeCompletedCount,
            overdueCount,
            inProgressCount,
            onTimeRatePercent,
            onTimeCompletedCount + lateCompletedCount);
    }

    private static List<DepartmentEvaluationProfileGroupColumnDto> BuildDashboardProfileGroupColumns(
        List<Employee> employees,
        Dictionary<Guid, IReadOnlyList<ProfileNode>> profileTreesByPosition)
    {
        var roots = new List<(string Name, int DisplayOrder, decimal WeightPercent)>();
        foreach (var positionId in employees.Select(e => e.JobPositionId).Distinct())
        {
            if (!profileTreesByPosition.TryGetValue(positionId, out var positionRoots))
            {
                continue;
            }

            foreach (var root in positionRoots)
            {
                roots.Add((EvaluationProfileTree.NodeName(root), root.Item.DisplayOrder, root.Item.WeightPercent));
            }
        }

        return roots
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .Select(g => new DepartmentEvaluationProfileGroupColumnDto(
                g.Key,
                g.Min(r => r.DisplayOrder),
                RoundDashboard(g.Average(r => r.WeightPercent))))
            .OrderBy(g => g.DisplayOrder)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<List<DepartmentEvaluationKpiGroupDto>> BuildDashboardKpiGroupsAsync(
        Guid departmentId,
        List<Guid> subtree,
        EvaluationPeriod period,
        EvaluationPeriod? comparePeriod,
        List<Employee> employees,
        Dictionary<Guid, IReadOnlyList<ProfileNode>> profileTreesByPosition,
        List<DepartmentEvaluationStaffRowDto> staffRows,
        CancellationToken cancellationToken)
    {
        var currentKpis = await LoadDashboardKpiRowsAsync(departmentId, subtree, period, cancellationToken);
        var previousKpis = comparePeriod is null
            ? []
            : await LoadDashboardKpiRowsAsync(departmentId, subtree, comparePeriod, cancellationToken);
        var previousByKpi = previousKpis.ToDictionary(k => k.KpiDefinitionId);

        var groupOfKpi = new Dictionary<Guid, string>();
        var leafWeightsByKpi = new Dictionary<Guid, List<decimal>>();
        var roots = new List<(string Name, int DisplayOrder, decimal WeightPercent)>();
        foreach (var positionId in employees.Select(e => e.JobPositionId).Distinct())
        {
            if (!profileTreesByPosition.TryGetValue(positionId, out var positionRoots))
            {
                continue;
            }

            foreach (var root in positionRoots)
            {
                var name = EvaluationProfileTree.NodeName(root);
                roots.Add((name, root.Item.DisplayOrder, root.Item.WeightPercent));
                var shares = EvaluationProfileTree.LeafShares(root);
                foreach (var leaf in EvaluationProfileTree.Leaves(root))
                {
                    if (leaf.Item.KpiDefinitionId.HasValue)
                    {
                        var kpiId = leaf.Item.KpiDefinitionId.Value;
                        groupOfKpi.TryAdd(kpiId, name);
                        var share = shares.TryGetValue(kpiId, out var s) ? s : leaf.Item.WeightPercent;
                        var effectiveWeight = root.Item.WeightPercent * share / 100m;
                        if (!leafWeightsByKpi.TryGetValue(kpiId, out var weightList))
                        {
                            weightList = [];
                            leafWeightsByKpi[kpiId] = weightList;
                        }

                        weightList.Add(effectiveWeight);
                    }
                }
            }
        }

        var groups = roots
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .Select(g => new
            {
                Name = (string?)g.Key,
                DisplayOrder = g.Min(r => r.DisplayOrder),
                WeightPercent = RoundDashboard(g.Average(r => r.WeightPercent)),
            })
            .OrderBy(g => g.DisplayOrder)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToList();

        var otherOrder = groups.Count == 0 ? 0 : groups.Max(g => g.DisplayOrder) + 1;
        groups.Add(new { Name = (string?)null, DisplayOrder = otherOrder, WeightPercent = 0m });

        var kpisByGroup = currentKpis.ToLookup(r => groupOfKpi.GetValueOrDefault(r.KpiDefinitionId));
        var result = new List<DepartmentEvaluationKpiGroupDto>();

        foreach (var g in groups)
        {
            var groupKpisRaw = kpisByGroup[g.Name]
                .OrderBy(r => r.Code, StringComparer.Ordinal)
                .ToList();
            var fallbackWeight = groupKpisRaw.Count > 0 && g.WeightPercent > 0m
                ? g.WeightPercent / groupKpisRaw.Count
                : 100m;

            var groupKpis = groupKpisRaw
                .Select(r =>
                {
                    previousByKpi.TryGetValue(r.KpiDefinitionId, out var prev);
                    var prevAch = prev?.AchievementPercent;
                    var weightPercent = leafWeightsByKpi.TryGetValue(r.KpiDefinitionId, out var weights) && weights.Count > 0
                        ? weights.Average()
                        : fallbackWeight;
                    decimal? convertedScore = r.AchievementPercent.HasValue
                        ? RoundDashboard(r.AchievementPercent.Value * weightPercent / 100m)
                        : null;
                    decimal? previousScore = prevAch.HasValue
                        ? RoundDashboard(prevAch.Value * weightPercent / 100m)
                        : null;
                    return new DepartmentEvaluationGroupKpiDto(
                        r.KpiDefinitionId,
                        r.Code,
                        r.Name,
                        r.Unit,
                        r.Direction.ToEnumString(),
                        r.TargetValue,
                        r.ActualValue,
                        r.AchievementPercent,
                        prev?.ActualValue,
                        prevAch,
                        ChangeDashboard(r.AchievementPercent, prevAch),
                        RoundDashboard(weightPercent),
                        convertedScore,
                        previousScore);
                })
                .ToList();

            decimal? groupAverageScore = null;
            if (g.Name is not null)
            {
                var staffGroupScores = staffRows
                    .SelectMany(sr => sr.GroupScores)
                    .Where(gs => string.Equals(gs.GroupName, g.Name, StringComparison.Ordinal) && gs.Score.HasValue)
                    .Select(gs => gs.Score!.Value)
                    .ToList();
                groupAverageScore = MeanDashboard(staffGroupScores);
            }

            if (groupAverageScore is null && groupKpis.Count > 0)
            {
                groupAverageScore = MeanDashboard(
                    groupKpis.Where(k => k.AchievementPercent.HasValue).Select(k => k.AchievementPercent!.Value));
            }

            if (groupKpis.Count > 0 || (g.Name is not null && groupAverageScore.HasValue))
            {
                result.Add(new DepartmentEvaluationKpiGroupDto(
                    g.Name,
                    g.DisplayOrder,
                    g.WeightPercent,
                    groupAverageScore,
                    groupKpis));
            }
        }

        return result;
    }

    private async Task<List<DashboardKpiRow>> LoadDashboardKpiRowsAsync(
        Guid departmentId,
        List<Guid> subtree,
        EvaluationPeriod period,
        CancellationToken cancellationToken)
    {
        var deptAssignments = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.DepartmentId == departmentId
                && t.EvaluationPeriodId == period.Id
                && t.Status == TargetAssignmentStatus.Active)
            .Select(t => new DashboardTargetRow(
                t.KpiDefinitionId,
                t.EmployeeId,
                t.Version,
                t.TargetValue,
                t.KpiDefinition.Code,
                t.KpiDefinition.Name,
                t.KpiDefinition.Unit,
                t.KpiDefinition.Direction,
                t.KpiDefinition.AggregationType,
                t.KpiDefinition.MaxAchievementPercent))
            .ToListAsync(cancellationToken);

        var latestByKpi = deptAssignments
            .GroupBy(a => a.KpiDefinitionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Version).First());

        var empAssignments = await db.TargetAssignments
            .AsNoTracking()
            .Where(t => t.EmployeeId != null
                && t.EvaluationPeriodId == period.Id
                && t.Status == TargetAssignmentStatus.Active
                && t.Employee != null
                && t.Employee.Status != EmployeeStatus.Terminated
                && subtree.Contains(t.Employee.DepartmentId))
            .Select(t => new DashboardTargetRow(
                t.KpiDefinitionId,
                t.EmployeeId,
                t.Version,
                t.TargetValue,
                t.KpiDefinition.Code,
                t.KpiDefinition.Name,
                t.KpiDefinition.Unit,
                t.KpiDefinition.Direction,
                t.KpiDefinition.AggregationType,
                t.KpiDefinition.MaxAchievementPercent))
            .ToListAsync(cancellationToken);

        foreach (var kpiGroup in empAssignments.GroupBy(a => a.KpiDefinitionId))
        {
            if (latestByKpi.ContainsKey(kpiGroup.Key))
            {
                continue;
            }

            var perEmployeeLatest = kpiGroup
                .GroupBy(a => a.EmployeeId)
                .Select(g => g.OrderByDescending(a => a.Version).First())
                .ToList();

            var sample = perEmployeeLatest[0];
            var aggregatedTarget = sample.AggregationType == KpiAggregationType.Sum
                ? RoundDashboard(perEmployeeLatest.Sum(a => a.TargetValue))
                : RoundDashboard(perEmployeeLatest.Average(a => a.TargetValue));

            latestByKpi[kpiGroup.Key] = sample with { TargetValue = aggregatedTarget };
        }

        var latest = latestByKpi.Values
            .OrderBy(a => a.Code, StringComparer.Ordinal)
            .ToList();

        if (latest.Count == 0)
        {
            return [];
        }

        var kpiIds = latest.Select(a => a.KpiDefinitionId).ToList();
        var progress = await db.ActualProgresses
            .AsNoTracking()
            .Where(a => a.Status == ActualProgressStatus.Valid
                && kpiIds.Contains(a.TargetAssignment.KpiDefinitionId)
                && a.ProgressDate >= period.StartDate
                && a.ProgressDate <= period.EndDate
                && a.TargetAssignment.Employee != null
                && a.TargetAssignment.Employee.Status != EmployeeStatus.Terminated
                && subtree.Contains(a.TargetAssignment.Employee.DepartmentId))
            .Select(a => new DashboardProgressRow(a.TargetAssignment.KpiDefinitionId, a.Value, a.Denominator))
            .ToListAsync(cancellationToken);

        var byKpi = progress.ToLookup(p => p.KpiDefinitionId);

        return latest
            .Select(a =>
            {
                var actual = AggregateDashboardActual(a, byKpi[a.KpiDefinitionId].ToList());
                return new DashboardKpiRow(
                    a.KpiDefinitionId,
                    a.Code,
                    a.Name,
                    a.Unit,
                    a.Direction,
                    a.TargetValue,
                    actual,
                    DepartmentService.Achievement(a.Direction, a.TargetValue, actual, a.MaxAchievementPercent));
            })
            .ToList();
    }

    private static decimal? AggregateDashboardActual(DashboardTargetRow target, List<DashboardProgressRow> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        switch (target.AggregationType)
        {
            case KpiAggregationType.Sum:
                return RoundDashboard(rows.Sum(r => r.Value));
            case KpiAggregationType.Avg:
                var entryCount = rows.Sum(r => r.Denominator ?? 1m);
                return entryCount == 0m ? null : RoundDashboard(rows.Sum(r => r.Value) / entryCount);
            case KpiAggregationType.Ratio:
                var denominator = rows.Sum(r => r.Denominator ?? 0m);
                if (denominator == 0m)
                {
                    return null;
                }

                var ratio = rows.Sum(r => r.Value) / denominator;
                return RoundDashboard(target.Unit.Trim() == "%" ? ratio * 100m : ratio);
            default:
                return null;
        }
    }

    private async Task<List<DepartmentEvaluationTrendPointDto>> BuildDashboardTrendAsync(
        EvaluationPeriod period,
        List<EvaluationPeriod> selectable,
        List<Guid> employeeIds,
        decimal? currentAverage,
        CancellationToken cancellationToken)
    {
        var trendPeriods = selectable
            .Where(p => p.PeriodType == period.PeriodType && p.StartDate <= period.StartDate)
            .OrderByDescending(p => p.StartDate)
            .Take(Math.Max(systemParameters.PerfTrendPeriodCount, 1))
            .Reverse()
            .ToList();

        var periodIds = trendPeriods.Select(p => p.Id).ToList();
        var scores = await db.PerformanceEvaluations
            .AsNoTracking()
            .Where(pe => periodIds.Contains(pe.EvaluationPeriodId) && employeeIds.Contains(pe.EmployeeId))
            .Where(pe => pe.TotalScore != null && pe.Status != PerformanceEvaluationStatus.Draft)
            .Select(pe => new { pe.EvaluationPeriodId, Score = pe.TotalScore!.Value })
            .ToListAsync(cancellationToken);
        var byPeriod = scores.ToLookup(s => s.EvaluationPeriodId, s => s.Score);

        return trendPeriods
            .Select(p => new DepartmentEvaluationTrendPointDto(
                p.Id,
                p.Code,
                p.Name,
                p.Id == period.Id ? currentAverage : MeanDashboard(byPeriod[p.Id])))
            .ToList();
    }

    private static List<DepartmentEvaluationTierItemDto> BuildDashboardTierItems(
        IReadOnlyList<RankingTierThreshold> thresholds,
        List<string> rankableTiers)
    {
        var ordered = thresholds.OrderBy(t => t.RankOrder).ToList();
        var totalRanked = rankableTiers.Count;
        var items = new List<DepartmentEvaluationTierItemDto>(ordered.Count);

        for (var i = 0; i < ordered.Count; i++)
        {
            var tier = ordered[i];
            decimal? maxScore = i == 0 ? null : ordered[i - 1].MinScore;
            var count = rankableTiers.Count(t => string.Equals(t, tier.TierCode, StringComparison.OrdinalIgnoreCase));
            var percent = totalRanked > 0 ? Math.Round(count * 100m / totalRanked, 1, MidpointRounding.AwayFromZero) : 0m;

            items.Add(new DepartmentEvaluationTierItemDto(
                tier.TierCode,
                tier.RankOrder,
                tier.MinScore,
                maxScore,
                count,
                percent));
        }

        return items;
    }

    private static List<DepartmentEvaluationActionAlertDto> BuildDashboardActionAlerts(
        List<DepartmentEvaluationStaffRowDto> staffRows)
    {
        var alerts = new List<DepartmentEvaluationActionAlertDto>();

        foreach (var pipRow in staffRows
            .Where(r => r.Status == "PIP")
            .OrderBy(r => r.TotalScore ?? decimal.MaxValue)
            .ThenBy(r => r.EmployeeCode, StringComparer.Ordinal))
        {
            alerts.Add(new DepartmentEvaluationActionAlertDto(
                "PIP",
                pipRow.EmployeeId,
                pipRow.EmployeeCode,
                pipRow.FullName,
                pipRow.TierCode,
                pipRow.TotalScore,
                pipRow.JobPositionTitle));
        }

        foreach (var appealRow in staffRows
            .Where(r => r.Status == "APPEAL")
            .OrderBy(r => r.EmployeeCode, StringComparer.Ordinal))
        {
            alerts.Add(new DepartmentEvaluationActionAlertDto(
                "APPEAL",
                appealRow.EmployeeId,
                appealRow.EmployeeCode,
                appealRow.FullName,
                appealRow.TierCode,
                appealRow.TotalScore,
                appealRow.JobPositionTitle));
        }

        return alerts;
    }

    private static void WriteDashboardSummarySheet(IXLWorksheet sheet, DepartmentEvaluationDashboardDto dashboard)
    {
        var allKpis = dashboard.Performance.KpiGroups.SelectMany(g => g.Kpis).ToList();
        var metKpiCount = allKpis.Count(k => (k.AchievementPercent ?? 0m) >= 100m);
        var totalKpiCount = allKpis.Count;
        decimal? kpiMetRatePercent = totalKpiCount == 0
            ? null
            : Math.Round((decimal)metKpiCount * 100m / totalKpiCount, 1, MidpointRounding.AwayFromZero);

        object?[][] rows =
        [
            ["Đơn vị", $"{dashboard.Department.Code} · {dashboard.Department.Name}"],
            ["Trưởng phòng", dashboard.Department.ManagerName],
            ["Kỳ đánh giá", dashboard.Period?.Name],
            ["Mã kỳ", dashboard.Period?.Code],
            ["Trạng thái kỳ", dashboard.Period?.Status],
            ["So với kỳ", dashboard.ComparePeriod?.Name],
            ["Điểm hiệu suất bình quân phòng", dashboard.Performance.Average],
            ["Điểm kỳ so sánh", dashboard.Performance.PreviousAverage],
            ["Chênh lệch điểm", dashboard.Performance.ChangeFromPrevious],
            ["Tỷ lệ KPI đạt kế hoạch (%)", kpiMetRatePercent],
            ["Số KPI đạt kế hoạch", metKpiCount],
            ["Tổng số KPI", totalKpiCount],
            ["Số KPI cần lưu ý", Math.Max(0, totalKpiCount - metKpiCount)],
            ["Trễ hạn (%)", dashboard.Operations.OnTimeRatePercent],
            ["Việc hoàn thành đúng hạn", dashboard.Operations.OnTimeCompletedCount],
            ["Việc trễ hạn", dashboard.Operations.OverdueCount],
            ["Việc đang xử lý", dashboard.Operations.InProgressCount],
            ["Tải công việc TB (việc/người)", dashboard.Operations.LoadPerStaff],
            ["Cán bộ đã chấm điểm", dashboard.Performance.EvaluatedEmployeeCount],
            ["Tổng số cán bộ", dashboard.Performance.TotalEmployeeCount],
            ["Cán bộ chưa chấm", dashboard.Performance.UnscoredEmployeeCount],
            ["Số cán bộ diện PIP", dashboard.Performance.PipCount],
            ["Số cán bộ khiếu nại", dashboard.Performance.AppealCount],
            ["Hạn mức Loại A (%)", dashboard.TierDistribution.TierACapPercent],
            ["Thời điểm xuất", dashboard.GeneratedAt.ToString("u", System.Globalization.CultureInfo.InvariantCulture)],
        ];
        WriteDashboardTable(
            sheet,
            dashboard,
            "TỔNG QUAN ĐÁNH GIÁ PHÒNG BAN",
            ["Chỉ số", "Giá trị"],
            rows,
            [null, "#,##0.##"]);
    }

    private static void WriteDashboardKpiSheet(IXLWorksheet sheet, DepartmentEvaluationDashboardDto dashboard)
    {
        var rows = dashboard.Performance.KpiGroups
            .SelectMany(group => group.Kpis.Select(kpi => new object?[]
            {
                group.Name ?? "Chỉ tiêu khác",
                group.WeightPercent,
                group.AverageScore,
                kpi.Code,
                kpi.Name,
                kpi.Unit,
                kpi.TargetValue,
                kpi.ActualValue,
                kpi.AchievementPercent,
                kpi.Score,
                kpi.PreviousAchievementPercent,
                kpi.ChangeFromPreviousPercent,
            }))
            .ToArray();
        WriteDashboardTable(
            sheet,
            dashboard,
            "CHỈ TIÊU KPI & ĐIỂM QUY ĐỔI",
            [
                "Nhóm trọng số",
                "Trọng số (%)",
                "Điểm TB nhóm",
                "Mã KPI",
                "Chỉ tiêu",
                "Đơn vị",
                "Kế hoạch",
                "Thực đạt",
                "% Đạt",
                "Điểm quy đổi",
                "% Kỳ so sánh",
                "± Kỳ trước",
            ],
            rows,
            [
                null,
                "0.0",
                "0.0",
                null,
                null,
                null,
                "#,##0.0",
                "#,##0.0",
                "[>=100][Color10]0.0\"%\";[Red]0.0\"%\"",
                "0.0",
                "0.0\"%\"",
                "[Color10]+0.0\"%\";[Red]-0.0\"%\";0.0\"%\"",
            ]);
    }

    private static void WriteDashboardTrendSheet(IXLWorksheet sheet, DepartmentEvaluationDashboardDto dashboard)
    {
        var rows = dashboard.Performance.Trend
            .Select(point => new object?[]
            {
                point.PeriodCode,
                point.PeriodName,
                point.Average,
            })
            .ToArray();
        WriteDashboardTable(
            sheet,
            dashboard,
            "XU HƯỚNG ĐIỂM BÌNH QUÂN PHÒNG",
            ["Mã kỳ", "Kỳ đánh giá", "Điểm bình quân phòng"],
            rows,
            [null, null, "0.0"]);
    }

    private static void WriteDashboardTierAndAlertSheet(IXLWorksheet sheet, DepartmentEvaluationDashboardDto dashboard)
    {
        var tierRows = dashboard.TierDistribution.Tiers
            .Select(tier => new object?[]
            {
                "XẾP LOẠI",
                tier.TierCode,
                tier.MinScore,
                tier.MaxScore,
                tier.Count,
                tier.Percent,
                null,
                null,
                null,
            });

        var alertRows = dashboard.TierDistribution.ActionAlerts
            .Select(alert => new object?[]
            {
                FormatAlertCategoryLabel(alert.Kind),
                alert.TierCode,
                null,
                null,
                null,
                null,
                alert.EmployeeCode,
                alert.FullName,
                alert.TotalScore.HasValue ? $"{alert.TotalScore.Value} · {alert.MetaText}" : alert.MetaText,
            });

        var rows = tierRows.Concat(alertRows).ToArray();
        WriteDashboardTable(
            sheet,
            dashboard,
            "PHÂN BỐ XẾP LOẠI & CẢNH BÁO",
            [
                "Phân mục",
                "Hạng",
                "Điểm tối thiểu",
                "Điểm tối đa",
                "Số cán bộ",
                "Tỷ lệ (%)",
                "Mã cán bộ",
                "Họ tên",
                "Chi tiết",
            ],
            rows,
            [null, null, "0.0", "0.0", "#,##0", "0.0\"%\"", null, null, null]);

        for (var i = 0; i < rows.Length; i++)
        {
            var r = ExportHeaderRow + 1 + i;
            var label = sheet.Cell(r, 1);
            if (label.GetString() == "XẾP LOẠI")
            {
                label.Style.Font.Bold = true;
            }
            else
            {
                StyleBadge(label, AlertTone(label.GetString()));
            }

            var tier = sheet.Cell(r, 2);
            if (tier.GetString().Length > 0)
            {
                StyleBadge(tier, TierTone(tier.GetString()));
            }
        }
    }

    private static void WriteDashboardOperationsSheet(IXLWorksheet sheet, DepartmentEvaluationDashboardDto dashboard)
    {
        var operations = dashboard.Operations;
        var total = operations.OnTimeCompletedCount + operations.InProgressCount + operations.OverdueCount;
        object?[][] rows =
        [
            ["Hoàn thành đúng hạn", operations.OnTimeCompletedCount, DashboardShare(operations.OnTimeCompletedCount, total)],
            ["Đang xử lý", operations.InProgressCount, DashboardShare(operations.InProgressCount, total)],
            ["Trễ hạn", operations.OverdueCount, DashboardShare(operations.OverdueCount, total)],
            ["Tổng việc theo dõi", total, DashboardShare(total, total)],
        ];
        WriteDashboardTable(
            sheet,
            dashboard,
            "CÔNG VIỆC & TRỄ HẠN PHÒNG BAN",
            ["Hạng mục", "Số việc", "Tỷ trọng (%)"],
            rows,
            [null, "#,##0", "0.0\"%\""]);
    }

    private static void WriteDashboardStaffSheet(IXLWorksheet sheet, DepartmentEvaluationDashboardDto dashboard)
    {
        var groupNames = dashboard.ProfileGroups
            .OrderBy(g => g.DisplayOrder)
            .Select(g => g.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        string[] header =
        [
            "Mã cán bộ",
            "Họ tên",
            "Vị trí",
            .. groupNames,
            "Điểm PE",
            "Điểm kỳ so sánh",
            "± Kỳ trước",
            "Xếp loại",
            "Đúng hạn",
            "Đang xử lý",
            "Quá hạn",
            "Tổng việc",
            "Trễ hạn (%)",
            "Trạng thái",
            "Trạng thái phiếu",
        ];

        var rows = dashboard.StaffRows
            .Select(row => new object?[]
            {
                row.EmployeeCode,
                row.FullName,
                row.JobPositionTitle,
            }
            .Concat(groupNames.Select(name => (object?)row.GroupScores.FirstOrDefault(g => g.GroupName == name)?.Score))
            .Concat(new object?[]
            {
                row.TotalScore,
                row.PreviousTotalScore,
                row.ScoreDelta,
                row.TierCode,
                row.OnTimeCompletedCount,
                row.InProgressCount,
                row.OverdueCount,
                row.OnTimeCompletedCount + row.InProgressCount + row.OverdueCount,
                row.OnTimeSlaPercent,
                FormatStaffStatusLabel(row.Status),
                row.EvaluationStatus,
            })
            .ToArray())
            .ToArray();

        string?[] formats =
        [
            null,
            null,
            null,
            .. Enumerable.Repeat<string?>("0.0", groupNames.Count),
            "0.0",
            "0.0",
            "[Color10]+0.0;[Red]-0.0;0.0",
            null,
            "#,##0",
            "#,##0",
            "#,##0",
            "#,##0",
            "[>=90][Color10]0.0\"%\";[<70][Red]0.0\"%\";0.0\"%\"",
            null,
            null,
        ];

        WriteDashboardTable(
            sheet,
            dashboard,
            "BẢNG ĐIỂM & CHỈ SỐ CÁN BỘ",
            header,
            rows,
            formats);

        var tierColumn = Array.IndexOf(header, "Xếp loại") + 1;
        var statusColumn = Array.IndexOf(header, "Trạng thái") + 1;
        for (var i = 0; i < rows.Length; i++)
        {
            var r = ExportHeaderRow + 1 + i;
            var tier = sheet.Cell(r, tierColumn);
            if (tier.GetString().Length > 0)
            {
                StyleBadge(tier, TierTone(tier.GetString()));
            }

            var status = sheet.Cell(r, statusColumn);
            StyleBadge(status, StaffStatusTone(status.GetString()));
        }
    }

    private static string FormatAlertCategoryLabel(string kind) => kind switch
    {
        "PIP" => "CẢNH BÁO · PIP",
        "APPEAL" => "CẢNH BÁO · KHIẾU NẠI",
        "TOP" => "GHI NHẬN · XUẤT SẮC",
        _ => kind,
    };

    private static string FormatStaffStatusLabel(string status) => status switch
    {
        "FINAL" => "Đã chốt",
        "APPEAL" => "Khiếu nại",
        "PIP" => "PIP 30 ngày",
        "PENDING" => "Chờ chấm",
        _ => status,
    };

    private static void WriteDashboardTable(
        IXLWorksheet sheet,
        DepartmentEvaluationDashboardDto dashboard,
        string title,
        string[] header,
        object?[][] rows,
        string?[] formats) =>
        WriteExportTable(
            sheet,
            title,
            BuildDashboardSubtitle(dashboard),
            header,
            rows,
            formats);

    private static void WriteExportTable(
        IXLWorksheet sheet,
        string title,
        string subtitle,
        string[] header,
        object?[][] rows,
        string?[] formats,
        int maxColumnWidth = 48)
    {
        var lastColumn = header.Length;
        var lastRow = ExportHeaderRow + rows.Length;

        sheet.Cell(1, 1).Value = title;
        var titleRange = sheet.Range(1, 1, 1, lastColumn).Merge();
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 14;
        titleRange.Style.Font.FontColor = ExportHeaderFill;
        titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(1).Height = 28;

        sheet.Cell(2, 1).Value = subtitle;
        var subtitleRange = sheet.Range(2, 1, 2, lastColumn).Merge();
        subtitleRange.Style.Font.Italic = true;
        subtitleRange.Style.Font.FontColor = ExportMutedText;

        for (var c = 0; c < lastColumn; c++)
        {
            sheet.Cell(ExportHeaderRow, c + 1).Value = header[c];
        }

        var headerRange = sheet.Range(ExportHeaderRow, 1, ExportHeaderRow, lastColumn);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Fill.BackgroundColor = ExportHeaderFill;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Alignment.WrapText = true;
        sheet.Row(ExportHeaderRow).Height = 32;

        for (var i = 0; i < rows.Length; i++)
        {
            var r = ExportHeaderRow + 1 + i;
            var row = rows[i];
            for (var c = 0; c < row.Length; c++)
            {
                var cell = sheet.Cell(r, c + 1);
                cell.Value = row[c] switch
                {
                    null => Blank.Value,
                    decimal d => d,
                    int n => n,
                    string s => s,
                    var other => SanitizeExcelString(other.ToString()),
                };
                if (row[c] is string text)
                {
                    cell.Value = SanitizeExcelString(text);
                }

                if (c < formats.Length && formats[c] is { } format)
                {
                    cell.Style.NumberFormat.Format = format;
                }
            }

            if (i % 2 == 1)
            {
                sheet.Range(r, 1, r, lastColumn).Style.Fill.BackgroundColor = ExportZebraFill;
            }
        }

        if (rows.Length > 0)
        {
            var body = sheet.Range(ExportHeaderRow, 1, lastRow, lastColumn);
            body.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            body.Style.Border.OutsideBorderColor = ExportBorderColor;
            body.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            body.Style.Border.InsideBorderColor = ExportBorderColor;
            sheet.Range(ExportHeaderRow + 1, 1, lastRow, lastColumn).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        sheet.SheetView.FreezeRows(ExportHeaderRow);
        for (var c = 1; c <= lastColumn; c++)
        {
            var column = sheet.Column(c);
            column.AdjustToContents(ExportHeaderRow, lastRow);
            column.Width = Math.Clamp(column.Width + 2, 14, maxColumnWidth);
        }
    }

    private static string SanitizeExcelString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value[0] is '=' or '+' or '-' or '@'
            ? $"'{value}"
            : value;
    }

    private static string BuildDashboardSubtitle(DepartmentEvaluationDashboardDto dashboard)
    {
        var period = dashboard.Period?.Name ?? "Chưa có kỳ đánh giá";
        var compare = dashboard.ComparePeriod is null ? string.Empty : $" · So với: {dashboard.ComparePeriod.Name}";
        return $"{dashboard.Department.Name} · Kỳ: {period}{compare} · Xuất ngày {dashboard.GeneratedAt:dd/MM/yyyy}";
    }

    private static decimal? DashboardShare(int count, int total) =>
        total == 0 ? null : RoundDashboard((decimal)count * 100m / total);

    private static void StyleBadge(IXLCell cell, (string Fill, string Font) tone)
    {
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(tone.Fill);
        cell.Style.Font.FontColor = XLColor.FromHtml(tone.Font);
        cell.Style.Font.Bold = true;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static (string Fill, string Font) TierTone(string code) => code switch
    {
        _ when code.StartsWith('A') => ExportGoodTone,
        "B" => ExportInfoTone,
        "C" => ExportWarnTone,
        _ => ExportBadTone,
    };

    private static (string Fill, string Font) AlertTone(string label) => label switch
    {
        _ when label.Contains("PIP") => ExportBadTone,
        _ when label.Contains("KHIẾU NẠI") => ExportWarnTone,
        _ => ExportNeutralTone,
    };

    private static (string Fill, string Font) StaffStatusTone(string label) => label switch
    {
        "Đã chốt" => ExportGoodTone,
        "Khiếu nại" => ExportWarnTone,
        "PIP 30 ngày" => ExportBadTone,
        _ => ExportNeutralTone,
    };

    private static decimal RoundDashboard(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal? RoundDashboardOrNull(decimal? value) => value.HasValue ? RoundDashboard(value.Value) : null;

    private static decimal? MeanDashboard(IEnumerable<decimal> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : RoundDashboard(list.Average());
    }

    private static decimal? ChangeDashboard(decimal? current, decimal? previous) =>
        current.HasValue && previous.HasValue ? RoundDashboard(current.Value - previous.Value) : null;

    private sealed record DashboardTargetRow(
        Guid KpiDefinitionId,
        Guid? EmployeeId,
        int Version,
        decimal TargetValue,
        string Code,
        string Name,
        string Unit,
        KpiDirection Direction,
        KpiAggregationType AggregationType,
        decimal? MaxAchievementPercent);

    private sealed record DashboardProgressRow(Guid KpiDefinitionId, decimal Value, decimal? Denominator);

    private sealed record DashboardEmployeeOperations(
        int OnTimeCompletedCount,
        int InProgressCount,
        int OverdueCount,
        decimal? OnTimeSlaPercent);

    private sealed record DashboardKpiRow(
        Guid KpiDefinitionId,
        string Code,
        string Name,
        string Unit,
        KpiDirection Direction,
        decimal TargetValue,
        decimal? ActualValue,
        decimal? AchievementPercent);
}
