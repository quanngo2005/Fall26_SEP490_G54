using System.Text.Json;
using System.Text.RegularExpressions;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Evaluation;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed partial class EvaluationService(
    AppDbContext db,
    ILogger<EvaluationService> logger,
    IPermissionService permissions) : IEvaluationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [GeneratedRegex(@"<!--\s*COACHING_PLAN:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex CoachingPlanMarkerRegex();

    [GeneratedRegex(@"<!--\s*QUALITATIVE_JSON:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex QualitativeMarkerRegex();

    [GeneratedRegex(@"<!--\s*CALIBRATION_JSON:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex CalibrationMarkerRegex();

    [GeneratedRegex(@"<!--\s*RECALIBRATION_REASON:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex RecalibrationReasonMarkerRegex();

    [GeneratedRegex(@"<!--\s*APPROVAL_COMMENTS:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex ApprovalCommentsMarkerRegex();

    private sealed record InternalCalibrationData(
        string CalibratedTier,
        string? Reason,
        bool CoachingFlag);

    private static readonly Action<ILogger, Guid, Guid, decimal, string, Exception?> LogEvaluationSaved =
        LoggerMessage.Define<Guid, Guid, decimal, string>(
            LogLevel.Information,
            new EventId(1, "EvaluationSaved"),
            "Performance evaluation saved for Employee {EmployeeId} in Period {PeriodId}: TotalScore={TotalScore}, Status={Status}");

    private static readonly Action<ILogger, Guid, Guid, int, Exception?> LogCalibrationSubmitted =
        LoggerMessage.Define<Guid, Guid, int>(
            LogLevel.Information,
            new EventId(2, "CalibrationSubmitted"),
            "Department calibration submitted for Dept {DepartmentId} in Period {PeriodId}: TotalAdjusted={TotalAdjusted}");

    private static readonly Action<ILogger, Guid, Guid, string, Exception?> LogCalibrationRejected =
        LoggerMessage.Define<Guid, Guid, string>(
            LogLevel.Information,
            new EventId(3, "CalibrationRejected"),
            "Department calibration rejected for Dept {DepartmentId} in Period {PeriodId}: Reason={Reason}");

    private static readonly Action<ILogger, Guid, int, Exception?> LogPeriodFinalized =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Information,
            new EventId(4, "PeriodFinalized"),
            "Evaluation period {PeriodId} finalized and hard-locked: TotalFinalized={TotalFinalized}");

    public async Task<EvaluationFormDto> GetEvaluationFormAsync(
        Guid employeeId,
        Guid periodId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var reviewer = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        var employee = await db.Employees
            .AsNoTracking()
            .Include(e => e.Department)
            .Include(e => e.JobPosition)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
            ?? throw new NotFoundException("Nhân viên", employeeId);

        // An employee reads only their own form; anyone else needs evaluation:read covering the employee's department.
        if (employee.Id != reviewer.Id)
        {
            await EnsureDepartmentInScopeAsync(currentUserId, "read", reviewer, employee.DepartmentId, cancellationToken);
        }

        var period = await db.EvaluationPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", periodId);

        var profile = await FindProfileAsync(employee.JobPositionId, period, cancellationToken)
            ?? throw new NotFoundException("Hồ sơ đánh giá của vị trí công việc", employee.JobPosition.Id);

        var targetAssignments = await db.TargetAssignments
            .AsNoTracking()
            .Where(ta => ta.EmployeeId == employeeId
                && ta.EvaluationPeriodId == periodId
                && ta.Status == TargetAssignmentStatus.Active)
            .ToListAsync(cancellationToken);

        var actualProgresses = await db.ActualProgresses
            .AsNoTracking()
            .Include(ap => ap.TargetAssignment)
            .Where(ap => ap.TargetAssignment.EmployeeId == employeeId
                && ap.TargetAssignment.EvaluationPeriodId == periodId
                && ap.Status == ActualProgressStatus.Valid)
            .ToListAsync(cancellationToken);

        var periodStart = period.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var periodEnd = period.EndDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var riskEvents = await db.RiskEvents
            .AsNoTracking()
            .Where(re => re.EmployeeId == employeeId
                && re.OccurredAt >= periodStart
                && re.OccurredAt <= periodEnd
                && re.Status != RiskEventStatus.Dismissed)
            .OrderByDescending(re => re.OccurredAt)
            .ToListAsync(cancellationToken);

        var existingEvaluation = await db.PerformanceEvaluations
            .AsNoTracking()
            .FirstOrDefaultAsync(pe => pe.EmployeeId == employeeId && pe.EvaluationPeriodId == periodId, cancellationToken);

        var (cleanComment, coachingPlan, qualitativeSaved, _, _, _) = ParseStoredComment(existingEvaluation?.Comment);

        var actualsDict = actualProgresses
            .GroupBy(ap => ap.TargetAssignment.KpiDefinitionId)
            .ToDictionary(g => g.Key, g => g.Sum(ap => ap.Value));

        var targetsDict = targetAssignments
            .GroupBy(ta => ta.KpiDefinitionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(ta => ta.Version).First().TargetValue);

        var riskDtos = riskEvents.Select(re =>
        {
            var penalty = MapRiskPenalty(re.Severity);
            return new EvaluationRiskEventDto(
                re.Id,
                re.Code,
                re.EventType,
                re.Description,
                re.Severity.ToString().ToUpperInvariant(),
                penalty,
                re.OccurredAt);
        }).ToList();

        var totalRiskPenalty = riskDtos.Sum(r => r.PenaltyPoints);

        var criteriaGroups = new List<EvaluationCriterionGroupDto>();
        decimal businessScore = 0m;
        decimal serviceQualityScore = 0m;
        decimal operationsScore = 0m;
        decimal totalWeightedScore = 0m;

        var profileItems = profile.EvaluationProfileItems
            .OrderBy(pi => pi.DisplayOrder)
            .ToList();

        foreach (var item in profileItems)
        {
            var kpiGroup = item.KpiGroup;
            var groupKpis = kpiGroup.KpiDefinitions
                .Where(kd => kd.Status == KpiStatus.Active)
                .ToList();

            var kpiItemDtos = new List<EvaluationKpiItemDto>();
            foreach (var kpi in groupKpis)
            {
                targetsDict.TryGetValue(kpi.Id, out var targetVal);
                actualsDict.TryGetValue(kpi.Id, out var actualVal);

                decimal achievement = 0m;
                if (targetVal > 0)
                {
                    if (kpi.Direction == KpiDirection.LowerBetter)
                    {
                        achievement = actualVal > 0
                            ? Math.Round((targetVal / actualVal) * 100m, 2)
                            : 100m;
                    }
                    else
                    {
                        achievement = Math.Round((actualVal / targetVal) * 100m, 2);
                    }
                }
                else if (actualVal > 0)
                {
                    achievement = 100m;
                }

                if (kpi.MaxAchievementPercent.HasValue && achievement > kpi.MaxAchievementPercent.Value)
                {
                    achievement = kpi.MaxAchievementPercent.Value;
                }

                var componentScore = achievement;

                kpiItemDtos.Add(new EvaluationKpiItemDto(
                    kpi.Id,
                    kpi.Code,
                    kpi.Name,
                    kpi.Unit,
                    targetVal,
                    actualVal,
                    achievement,
                    componentScore));
            }

            decimal groupScore;
            decimal? qualitativeScore = null;
            string? qualitativeNotes = null;

            if (qualitativeSaved.TryGetValue(kpiGroup.Id, out var savedQual))
            {
                qualitativeScore = savedQual.Score;
                qualitativeNotes = savedQual.Notes;
            }

            if (kpiItemDtos.Count > 0)
            {
                groupScore = Math.Round(kpiItemDtos.Average(k => k.ComponentScore), 2);
            }
            else
            {
                groupScore = qualitativeScore ?? 100m;
            }

            totalWeightedScore += (groupScore * item.WeightPercent) / 100m;

            var codeUpper = kpiGroup.Code.ToUpperInvariant();
            if (codeUpper.Contains("KD") || codeUpper.Contains("SALES") || codeUpper.Contains("BUSINESS"))
            {
                businessScore = groupScore;
            }
            else if (codeUpper.Contains("CLDV") || codeUpper.Contains("SERVICE") || codeUpper.Contains("CSAT"))
            {
                serviceQualityScore = groupScore;
            }
            else if (codeUpper.Contains("TT") || codeUpper.Contains("OPER") || codeUpper.Contains("VAN_HANH"))
            {
                operationsScore = groupScore;
            }

            criteriaGroups.Add(new EvaluationCriterionGroupDto(
                kpiGroup.Id,
                kpiGroup.Code,
                kpiGroup.Name,
                item.WeightPercent,
                groupScore,
                kpiItemDtos,
                qualitativeScore,
                qualitativeNotes));
        }

        var totalCalculated = Math.Max(0m, Math.Round(totalWeightedScore - totalRiskPenalty, 2));
        var proposedTier = RankingRules.DetermineTier(
            totalCalculated, riskEvents, await LoadRankingConfigurationAsync(cancellationToken));

        var employeeDto = new EvaluationEmployeeDto(
            employee.Id,
            employee.EmployeeCode,
            employee.FullName,
            employee.Department?.Name ?? string.Empty,
            employee.DepartmentId,
            employee.JobPosition?.Title ?? string.Empty,
            employee.JobPositionId);

        var periodDto = new EvaluationPeriodDto(
            period.Id,
            period.Code,
            period.Name,
            period.StartDate,
            period.EndDate,
            period.PeriodType.ToString().ToUpperInvariant(),
            period.Status.ToString().ToUpperInvariant());

        var profileDto = new EvaluationProfileSummaryDto(
            profile.Id,
            profile.Code,
            profile.Name,
            profile.Version);

        var summaryDto = new EvaluationCalculationSummaryDto(
            businessScore,
            serviceQualityScore,
            operationsScore,
            totalRiskPenalty,
            existingEvaluation?.TotalScore ?? totalCalculated,
            proposedTier);

        var currentEvaluationDto = new PerformanceEvaluationStateDto(
            existingEvaluation?.Id,
            existingEvaluation?.TotalScore,
            (existingEvaluation?.Status ?? PerformanceEvaluationStatus.Draft).ToString().ToUpperInvariant(),
            null,
            null,
            existingEvaluation?.CalculatedAt,
            existingEvaluation?.ApprovedAt,
            cleanComment,
            coachingPlan,
            !string.IsNullOrWhiteSpace(coachingPlan));

        var canEdit = period.Status is PeriodStatus.Open or PeriodStatus.InReview
            && (existingEvaluation == null || existingEvaluation.Status is PerformanceEvaluationStatus.Draft or PerformanceEvaluationStatus.Calculated);

        return new EvaluationFormDto(
            employeeDto,
            periodDto,
            profileDto,
            criteriaGroups,
            riskDtos,
            summaryDto,
            currentEvaluationDto,
            canEdit);
    }

    public async Task<EvaluationResultDto> SubmitEvaluationAsync(
        SubmitEvaluationRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var reviewer = await ResolveEmployeeAsync(currentUserId, cancellationToken);

        // BR-02: Maker != Checker rule: Manager cannot evaluate self
        if (reviewer.Id == request.EmployeeId)
        {
            throw new BusinessRuleException(
                ErrorCodes.MSG10,
                "Theo quy tắc BR-02 (Maker != Checker), cấp quản lý không được tự thực hiện đánh giá hiệu suất cho chính mình.");
        }

        var employee = await db.Employees
            .AsNoTracking()
            .Include(e => e.JobPosition)
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId, cancellationToken)
            ?? throw new NotFoundException("Nhân viên", request.EmployeeId);

        await EnsureDepartmentInScopeAsync(currentUserId, "submit", reviewer, employee.DepartmentId, cancellationToken);

        var period = await db.EvaluationPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PeriodId, cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", request.PeriodId);

        if (period.Status is PeriodStatus.Locked or PeriodStatus.Closed)
        {
            throw new BusinessRuleException(
                ErrorCodes.Conflict,
                "Kỳ đánh giá đã khóa hoặc đóng, không thể lưu hoặc cập nhật đánh giá.");
        }

        var profile = await FindProfileAsync(employee.JobPositionId, period, cancellationToken)
            ?? throw new NotFoundException("Hồ sơ đánh giá của vị trí công việc", employee.JobPosition.Id);

        var targetAssignments = await db.TargetAssignments
            .AsNoTracking()
            .Where(ta => ta.EmployeeId == request.EmployeeId
                && ta.EvaluationPeriodId == request.PeriodId
                && ta.Status == TargetAssignmentStatus.Active)
            .ToListAsync(cancellationToken);

        var actualProgresses = await db.ActualProgresses
            .AsNoTracking()
            .Include(ap => ap.TargetAssignment)
            .Where(ap => ap.TargetAssignment.EmployeeId == request.EmployeeId
                && ap.TargetAssignment.EvaluationPeriodId == request.PeriodId
                && ap.Status == ActualProgressStatus.Valid)
            .ToListAsync(cancellationToken);

        var periodStart = period.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var periodEnd = period.EndDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var riskEvents = await db.RiskEvents
            .AsNoTracking()
            .Where(re => re.EmployeeId == request.EmployeeId
                && re.OccurredAt >= periodStart
                && re.OccurredAt <= periodEnd
                && re.Status != RiskEventStatus.Dismissed)
            .ToListAsync(cancellationToken);

        var actualsDict = actualProgresses
            .GroupBy(ap => ap.TargetAssignment.KpiDefinitionId)
            .ToDictionary(g => g.Key, g => g.Sum(ap => ap.Value));

        var targetsDict = targetAssignments
            .GroupBy(ta => ta.KpiDefinitionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(ta => ta.Version).First().TargetValue);

        var qualInputsDict = (request.QualitativeScores ?? new List<QualitativeScoreInputDto>())
            .ToDictionary(q => q.KpiGroupId, q => q);

        decimal totalWeightedScore = 0m;
        foreach (var item in profile.EvaluationProfileItems)
        {
            var kpiGroup = item.KpiGroup;
            var groupKpis = kpiGroup.KpiDefinitions.Where(kd => kd.Status == KpiStatus.Active).ToList();

            decimal groupScore;
            if (qualInputsDict.TryGetValue(kpiGroup.Id, out var qualInput))
            {
                groupScore = qualInput.Score;
            }
            else if (groupKpis.Count > 0)
            {
                var scores = groupKpis.Select(kpi =>
                {
                    targetsDict.TryGetValue(kpi.Id, out var targetVal);
                    actualsDict.TryGetValue(kpi.Id, out var actualVal);

                    decimal achievement = 0m;
                    if (targetVal > 0)
                    {
                        if (kpi.Direction == KpiDirection.LowerBetter)
                        {
                            achievement = actualVal > 0
                                ? Math.Round((targetVal / actualVal) * 100m, 2)
                                : 100m;
                        }
                        else
                        {
                            achievement = Math.Round((actualVal / targetVal) * 100m, 2);
                        }
                    }
                    else if (actualVal > 0)
                    {
                        achievement = 100m;
                    }

                    if (kpi.MaxAchievementPercent.HasValue && achievement > kpi.MaxAchievementPercent.Value)
                    {
                        achievement = kpi.MaxAchievementPercent.Value;
                    }

                    return achievement;
                });

                groupScore = Math.Round(scores.Average(), 2);
            }
            else
            {
                groupScore = 100m;
            }

            totalWeightedScore += (groupScore * item.WeightPercent) / 100m;
        }

        var totalRiskPenalty = riskEvents.Sum(re => MapRiskPenalty(re.Severity));
        var finalScore = Math.Max(0m, Math.Round(totalWeightedScore - totalRiskPenalty, 2));
        var proposedTier = RankingRules.DetermineTier(
            finalScore, riskEvents, await LoadRankingConfigurationAsync(cancellationToken));

        var existingEvaluation = await db.PerformanceEvaluations
            .FirstOrDefaultAsync(pe => pe.EmployeeId == request.EmployeeId && pe.EvaluationPeriodId == request.PeriodId, cancellationToken);

        var (_, _, _, existingCalib, existingRecalib, existingAppr) = ParseStoredComment(existingEvaluation?.Comment);
        var storedComment = BuildStoredComment(request.Comment, request.CoachingPlan, request.QualitativeScores, existingCalib, existingRecalib, existingAppr);
        var now = DateTimeOffset.UtcNow;
        var targetStatus = request.IsDraft
            ? PerformanceEvaluationStatus.Draft
            : PerformanceEvaluationStatus.Calculated;

        Guid evaluationId;
        if (existingEvaluation is not null)
        {
            existingEvaluation.TotalScore = finalScore;
            existingEvaluation.Comment = storedComment;
            existingEvaluation.Status = targetStatus;
            existingEvaluation.CalculatedAt = now;
            existingEvaluation.UpdatedAt = now;
            evaluationId = existingEvaluation.Id;
        }
        else
        {
            var newEvaluation = new PerformanceEvaluation
            {
                Id = Guid.NewGuid(),
                EmployeeId = request.EmployeeId,
                EvaluationPeriodId = request.PeriodId,
                EvaluationProfileId = profile.Id,
                TotalScore = finalScore,
                Status = targetStatus,
                CalculatedAt = now,
                Comment = storedComment,
                UpdatedAt = now,
            };

            await db.PerformanceEvaluations.AddAsync(newEvaluation, cancellationToken);
            evaluationId = newEvaluation.Id;
        }

        await db.SaveChangesAsync(cancellationToken);

        LogEvaluationSaved(logger, request.EmployeeId, request.PeriodId, finalScore, targetStatus.ToString(), null);

        var message = request.IsDraft
            ? "Bản nháp đánh giá hiệu suất đã được lưu thành công."
            : "Đánh giá hiệu suất đã được tính toán và lưu thành công.";

        return new EvaluationResultDto(
            evaluationId,
            request.EmployeeId,
            request.PeriodId,
            finalScore,
            targetStatus.ToString().ToUpperInvariant(),
            proposedTier,
            now,
            message);
    }

    public async Task<IReadOnlyList<EvaluationPeriodOptionDto>> GetEvaluationPeriodsAsync(
        CancellationToken cancellationToken = default)
    {
        var periods = await db.EvaluationPeriods
            .AsNoTracking()
            .Where(p => p.PeriodType != PeriodType.Day)
            .OrderByDescending(p => p.StartDate)
            .ToListAsync(cancellationToken);

        return periods.Select(p => new EvaluationPeriodOptionDto(
            p.Id,
            p.Code,
            p.Name,
            p.StartDate,
            p.EndDate,
            p.PeriodType.ToString().ToUpperInvariant(),
            p.Status.ToString().ToUpperInvariant(),
            p.Status is PeriodStatus.Open or PeriodStatus.InReview)).ToList();
    }

    public async Task<IReadOnlyList<DepartmentEmployeeEvaluationSummaryDto>> GetDepartmentEmployeesForEvaluationAsync(
        Guid periodId,
        Guid? departmentId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var reviewer = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        var targetDeptId = departmentId ?? reviewer.DepartmentId;
        await EnsureDepartmentInScopeAsync(currentUserId, "read", reviewer, targetDeptId, cancellationToken);

        var employees = await db.Employees
            .AsNoTracking()
            .Include(e => e.Department)
            .Include(e => e.JobPosition)
            .Where(e => e.DepartmentId == targetDeptId && e.Status == EmployeeStatus.Active)
            .OrderBy(e => e.FullName)
            .ToListAsync(cancellationToken);

        var employeeIds = employees.Select(e => e.Id).ToList();

        var evaluations = await db.PerformanceEvaluations
            .AsNoTracking()
            .Where(pe => pe.EvaluationPeriodId == periodId && employeeIds.Contains(pe.EmployeeId))
            .ToListAsync(cancellationToken);

        var evalDict = evaluations.ToDictionary(pe => pe.EmployeeId, pe => pe);

        var period = await db.EvaluationPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken);
        var risksByEmployee = await LoadRiskEventsByEmployeeAsync(employeeIds, period, cancellationToken);
        var rankingConfiguration = await LoadRankingConfigurationAsync(cancellationToken);

        return employees.Select(emp =>
        {
            evalDict.TryGetValue(emp.Id, out var pe);
            var status = pe != null ? pe.Status.ToString().ToUpperInvariant() : "NOT_STARTED";
            risksByEmployee.TryGetValue(emp.Id, out var empRisks);
            var tier = pe?.TotalScore != null
                ? RankingRules.DetermineTier(pe.TotalScore.Value, empRisks, rankingConfiguration)
                : null;

            return new DepartmentEmployeeEvaluationSummaryDto(
                emp.Id,
                emp.EmployeeCode,
                emp.FullName,
                emp.JobPosition?.Title ?? string.Empty,
                emp.JobPositionId,
                emp.DepartmentId,
                emp.Department?.Name ?? string.Empty,
                pe?.Id,
                pe?.TotalScore,
                status,
                tier,
                pe?.CalculatedAt);
        }).ToList();
    }

    public async Task<CalibrationOverviewDto> GetDepartmentCalibrationAsync(
        Guid periodId,
        Guid? departmentId,
        Guid? positionId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var reviewer = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        var targetDeptId = departmentId ?? reviewer.DepartmentId;
        await EnsureDepartmentInScopeAsync(currentUserId, "read", reviewer, targetDeptId, cancellationToken);

        var department = await db.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == targetDeptId, cancellationToken)
            ?? throw new NotFoundException("Phòng ban", targetDeptId);

        var period = await db.EvaluationPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", periodId);

        var query = db.Employees
            .AsNoTracking()
            .Include(e => e.Department)
            .Include(e => e.JobPosition)
            .Where(e => e.DepartmentId == targetDeptId && e.Status == EmployeeStatus.Active);

        if (positionId.HasValue)
        {
            query = query.Where(e => e.JobPositionId == positionId.Value);
        }

        var employees = await query
            .OrderBy(e => e.FullName)
            .ToListAsync(cancellationToken);

        var employeeIds = employees.Select(e => e.Id).ToList();

        var evaluations = await db.PerformanceEvaluations
            .AsNoTracking()
            .Where(pe => pe.EvaluationPeriodId == periodId && employeeIds.Contains(pe.EmployeeId))
            .ToListAsync(cancellationToken);
        var evalDict = evaluations.ToDictionary(pe => pe.EmployeeId, pe => pe);
        var profilesByPosition = await LoadProfilesByPositionAsync(employees.Select(e => e.JobPositionId), period, cancellationToken);

        var targetAssignments = await db.TargetAssignments
            .AsNoTracking()
            .Where(ta => ta.EmployeeId.HasValue
                && employeeIds.Contains(ta.EmployeeId.Value)
                && ta.EvaluationPeriodId == periodId
                && ta.Status == TargetAssignmentStatus.Active)
            .ToListAsync(cancellationToken);
        var targetsGroup = targetAssignments
            .Where(ta => ta.EmployeeId.HasValue)
            .GroupBy(ta => ta.EmployeeId!.Value)
            .ToDictionary(g => g.Key, g => g.GroupBy(t => t.KpiDefinitionId)
                .ToDictionary(tg => tg.Key, tg => tg.OrderByDescending(t => t.Version).First().TargetValue));

        var actualProgresses = await db.ActualProgresses
            .AsNoTracking()
            .Include(ap => ap.TargetAssignment)
            .Where(ap => ap.TargetAssignment.EmployeeId.HasValue
                && employeeIds.Contains(ap.TargetAssignment.EmployeeId.Value)
                && ap.TargetAssignment.EvaluationPeriodId == periodId
                && ap.Status == ActualProgressStatus.Valid)
            .ToListAsync(cancellationToken);
        var actualsGroup = actualProgresses.GroupBy(ap => ap.TargetAssignment.EmployeeId!.Value)
            .ToDictionary(g => g.Key, g => g.GroupBy(a => a.TargetAssignment.KpiDefinitionId)
                .ToDictionary(ag => ag.Key, ag => ag.Sum(a => a.Value)));

        var periodStart = period.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var periodEnd = period.EndDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var riskEvents = await db.RiskEvents
            .AsNoTracking()
            .Where(re => employeeIds.Contains(re.EmployeeId)
                && re.OccurredAt >= periodStart
                && re.OccurredAt <= periodEnd
                && re.Status != RiskEventStatus.Dismissed)
            .ToListAsync(cancellationToken);
        var risksGroup = riskEvents.GroupBy(re => re.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rankingConfiguration = await LoadRankingConfigurationAsync(cancellationToken);
        var unrankedRows = new List<CalibrationEmployeeRowDto>();

        foreach (var emp in employees)
        {
            evalDict.TryGetValue(emp.Id, out var pe);
            targetsGroup.TryGetValue(emp.Id, out var empTargets);
            actualsGroup.TryGetValue(emp.Id, out var empActuals);
            risksGroup.TryGetValue(emp.Id, out var empRisks);

            var (_, coachingPlan, _, calibData, _, _) = ParseStoredComment(pe?.Comment);

            var (businessScore, serviceQualityScore, operationsScore, qualitativeScore, totalScore) =
                ComputeEmployeeScores(emp, profilesByPosition.GetValueOrDefault(emp.JobPositionId), empTargets, empActuals, empRisks, pe);

            var proposedTier = RankingRules.DetermineTier(totalScore, empRisks, rankingConfiguration);
            var calibratedTier = calibData?.CalibratedTier ?? proposedTier;
            var isAdjusted = !string.Equals(calibratedTier, proposedTier, StringComparison.OrdinalIgnoreCase);
            var coachingFlag = calibData?.CoachingFlag ?? (!string.IsNullOrWhiteSpace(coachingPlan) || proposedTier is "C" or "D");

            string? disciplinaryFlag = null;
            if (empRisks is { Count: > 0 })
            {
                if (empRisks.Any(r => r.Severity == RiskEventSeverity.Critical))
                {
                    disciplinaryFlag = "Vi phạm nghiêm trọng (hạ bậc)";
                }
                else if (empRisks.Any(r => r.Severity == RiskEventSeverity.High))
                {
                    disciplinaryFlag = "Cảnh cáo mức cao (-10đ)";
                }
                else
                {
                    var penalty = empRisks.Sum(r => MapRiskPenalty(r.Severity));
                    disciplinaryFlag = $"Trừ điểm rủi ro (-{penalty}đ)";
                }
            }

            var evalStatus = pe != null ? pe.Status.ToString().ToUpperInvariant() : "NOT_STARTED";

            unrankedRows.Add(new CalibrationEmployeeRowDto(
                emp.Id,
                emp.EmployeeCode,
                emp.FullName,
                emp.JobPositionId,
                emp.JobPosition?.Title ?? string.Empty,
                emp.DepartmentId,
                emp.Department?.Name ?? string.Empty,
                businessScore,
                serviceQualityScore,
                operationsScore,
                qualitativeScore,
                totalScore,
                0,
                proposedTier,
                calibratedTier,
                isAdjusted,
                calibData?.Reason,
                coachingFlag,
                disciplinaryFlag,
                evalStatus,
                pe?.Id,
                pe?.CalculatedAt));
        }

        // BR-34: Rank by TotalScore DESC, tie-break by BusinessScore DESC, then ServiceQualityScore DESC, then EmployeeCode
        var sortedRows = unrankedRows
            .OrderByDescending(r => r.TotalScore)
            .ThenByDescending(r => r.BusinessScore)
            .ThenByDescending(r => r.ServiceQualityScore)
            .ThenBy(r => r.EmployeeCode)
            .ToList();

        var rankedRoster = sortedRows
            .Select((row, idx) => row with { Rank = idx + 1 })
            .ToList();

        var quota = ComputeCalibrationQuota(rankedRoster);
        var bellCurve = ComputeGaussianBellCurve(rankedRoster);

        var deptDto = new CalibrationDepartmentDto(
            department.Id,
            department.Code,
            department.Name,
            reviewer.FullName);

        var periodDto = new EvaluationPeriodDto(
            period.Id,
            period.Code,
            period.Name,
            period.StartDate,
            period.EndDate,
            period.PeriodType.ToString().ToUpperInvariant(),
            period.Status.ToString().ToUpperInvariant());

        string overallStatus = "NOT_STARTED";
        if (rankedRoster.Count > 0)
        {
            if (rankedRoster.All(r => r.EvaluationStatus == "FINAL"))
            {
                overallStatus = "FINAL";
            }
            else if (rankedRoster.All(r => r.EvaluationStatus == "APPROVED"))
            {
                overallStatus = "APPROVED";
            }
            else if (rankedRoster.Any(r => r.EvaluationStatus == "IN_REVIEW"))
            {
                overallStatus = "IN_REVIEW";
            }
            else if (rankedRoster.Any(r => r.EvaluationStatus == "CALCULATED"))
            {
                overallStatus = "CALCULATED";
            }
        }

        var canSubmit = period.Status is PeriodStatus.Open or PeriodStatus.InReview
            && overallStatus is not "FINAL" and not "APPROVED";

        return new CalibrationOverviewDto(
            deptDto,
            periodDto,
            quota,
            bellCurve,
            rankedRoster,
            canSubmit,
            overallStatus);
    }

    public async Task<CalibrationSubmitResultDto> SubmitDepartmentCalibrationAsync(
        SubmitCalibrationRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var reviewer = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        await EnsureDepartmentInScopeAsync(currentUserId, "calibrate", reviewer, request.DepartmentId, cancellationToken);

        var period = await db.EvaluationPeriods
            .FirstOrDefaultAsync(p => p.Id == request.PeriodId, cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", request.PeriodId);

        if (period.Status is PeriodStatus.Locked or PeriodStatus.Closed)
        {
            throw new BusinessRuleException(
                ErrorCodes.Conflict,
                "Kỳ đánh giá đã khóa hoặc đóng, không thể lưu hoặc điều chỉnh hiệu chuẩn.");
        }

        var department = await db.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId, cancellationToken)
            ?? throw new NotFoundException("Phòng ban", request.DepartmentId);

        var employees = await db.Employees
            .AsNoTracking()
            .Include(e => e.JobPosition)
            .Where(e => e.DepartmentId == request.DepartmentId && e.Status == EmployeeStatus.Active)
            .ToListAsync(cancellationToken);

        var employeeMap = employees.ToDictionary(e => e.Id, e => e);
        var profilesByPosition = await LoadProfilesByPositionAsync(employees.Select(e => e.JobPositionId), period, cancellationToken);
        var totalEmployees = employees.Count;
        var maxTierAQuota = totalEmployees > 0 ? Math.Max(1, (int)Math.Ceiling(totalEmployees * 0.20m)) : 0;

        var adjustmentMap = (request.Adjustments ?? new())
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Last());

        var tierACount = adjustmentMap.Values.Count(a => a.CalibratedTier is "A1" or "A2" or "A3");

        // BR-37: Quota validation when submitting to executive
        if (!request.IsDraft)
        {
            if (tierACount > maxTierAQuota)
            {
                var percent = totalEmployees > 0 ? Math.Round(tierACount * 100m / totalEmployees, 1) : 0m;
                throw new BusinessRuleException(
                    ErrorCodes.TierAQuotaExceeded,
                    $"Tỷ lệ xếp loại Tier A ({tierACount}/{totalEmployees} = {percent}%) vượt quá hạn ngạch cho phép tối đa ({maxTierAQuota} suất, tương đương 20%). Vui lòng điều chỉnh lại trước khi trình duyệt.");
            }
        }

        var periodEmployeeIds = employees.Select(e => e.Id).ToList();
        var existingEvaluations = await db.PerformanceEvaluations
            .Where(pe => pe.EvaluationPeriodId == request.PeriodId && periodEmployeeIds.Contains(pe.EmployeeId))
            .ToListAsync(cancellationToken);

        var evalDict = existingEvaluations.ToDictionary(pe => pe.EmployeeId, pe => pe);
        var now = DateTimeOffset.UtcNow;
        var targetStatus = request.IsDraft
            ? PerformanceEvaluationStatus.Calculated
            : PerformanceEvaluationStatus.InReview;

        foreach (var (empId, adj) in adjustmentMap)
        {
            if (!employeeMap.TryGetValue(empId, out var emp))
            {
                continue;
            }

            var calibData = new InternalCalibrationData(
                adj.CalibratedTier,
                adj.Reason,
                adj.CoachingFlag);

            if (evalDict.TryGetValue(empId, out var existingPe))
            {
                var (comment, coachPlan, qualScores, _, recalibReason, apprComments) = ParseStoredComment(existingPe.Comment);
                existingPe.Comment = BuildStoredComment(
                    comment ?? request.Notes,
                    coachPlan,
                    qualScores.Values.ToList(),
                    calibData,
                    recalibReason,
                    apprComments);
                existingPe.Status = targetStatus;
                existingPe.UpdatedAt = now;
            }
            else
            {
                var newComment = BuildStoredComment(
                    request.Notes,
                    adj.CoachingFlag ? "Cần kèm cặp sau hiệu chuẩn xếp loại" : null,
                    null,
                    calibData,
                    null,
                    null);

                var newPe = new PerformanceEvaluation
                {
                    Id = Guid.NewGuid(),
                    EmployeeId = empId,
                    EvaluationPeriodId = request.PeriodId,
                    EvaluationProfileId = profilesByPosition.GetValueOrDefault(emp.JobPositionId)?.Id ?? Guid.Empty,
                    TotalScore = 80m,
                    Status = targetStatus,
                    CalculatedAt = now,
                    Comment = newComment,
                    UpdatedAt = now,
                };
                await db.PerformanceEvaluations.AddAsync(newPe, cancellationToken);
            }
        }

        // If submitting to executive, also ensure any other calculated evaluations in department move to InReview
        if (!request.IsDraft)
        {
            foreach (var pe in existingEvaluations)
            {
                if (pe.Status is PerformanceEvaluationStatus.Calculated or PerformanceEvaluationStatus.Draft)
                {
                    pe.Status = PerformanceEvaluationStatus.InReview;
                    pe.UpdatedAt = now;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        LogCalibrationSubmitted(logger, request.DepartmentId, request.PeriodId, adjustmentMap.Count, null);

        var statusString = targetStatus.ToString().ToUpperInvariant();
        var message = request.IsDraft
            ? "Đã lưu bản nháp điều chỉnh hiệu chuẩn thứ hạng thành công."
            : "Đã lưu và trình duyệt bảng hiệu chuẩn thứ hạng phòng ban lên Ban Giám đốc thành công.";

        return new CalibrationSubmitResultDto(
            request.DepartmentId,
            request.PeriodId,
            statusString,
            adjustmentMap.Count,
            tierACount,
            tierACount <= maxTierAQuota,
            now,
            message);
    }

    public async Task<BranchApprovalOverviewDto> GetBranchApprovalOverviewAsync(
        Guid periodId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await ResolveEmployeeAsync(currentUserId, cancellationToken);

        var period = await db.EvaluationPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", periodId);

        var departments = await db.Departments
            .AsNoTracking()
            .Where(d => d.Status == RecordStatus.Active && d.LevelType == DepartmentLevelType.Department)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        var deptIds = departments.Select(d => d.Id).ToList();

        var employees = await db.Employees
            .AsNoTracking()
            .Where(e => deptIds.Contains(e.DepartmentId) && e.Status == EmployeeStatus.Active)
            .ToListAsync(cancellationToken);

        var empDeptMap = employees.GroupBy(e => e.DepartmentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allEmpIds = employees.Select(e => e.Id).ToList();

        var evaluations = await db.PerformanceEvaluations
            .AsNoTracking()
            .Where(pe => pe.EvaluationPeriodId == periodId && allEmpIds.Contains(pe.EmployeeId))
            .ToListAsync(cancellationToken);

        var evalEmpMap = evaluations.GroupBy(pe => pe.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        var risksByEmployee = await LoadRiskEventsByEmployeeAsync(allEmpIds, period, cancellationToken);
        var rankingConfiguration = await LoadRankingConfigurationAsync(cancellationToken);

        var deptSummaries = new List<DepartmentApprovalSummaryDto>();

        foreach (var dept in departments)
        {
            empDeptMap.TryGetValue(dept.Id, out var deptEmps);
            deptEmps ??= new();

            var totalDeptEmps = deptEmps.Count;
            var deptEvaluations = deptEmps
                .Where(e => evalEmpMap.ContainsKey(e.Id))
                .Select(e => evalEmpMap[e.Id])
                .ToList();

            var evaluatedCount = deptEvaluations.Count(pe => pe.Status != PerformanceEvaluationStatus.Draft);

            decimal avgScore = 0m;
            var scores = deptEvaluations
                .Where(pe => pe.TotalScore.HasValue)
                .Select(pe => pe.TotalScore!.Value)
                .ToList();

            if (scores.Count > 0)
            {
                avgScore = Math.Round(scores.Average(), 1);
            }

            int tierA = 0;
            int tierB = 0;
            int tierC = 0;
            int tierD = 0;
            string? reviewerComment = null;
            DateTimeOffset? latestSubmit = null;

            foreach (var pe in deptEvaluations)
            {
                var (_, _, _, calib, recalibReason, _) = ParseStoredComment(pe.Comment);
                if (recalibReason != null)
                {
                    reviewerComment = recalibReason;
                }

                if (pe.UpdatedAt > (latestSubmit ?? DateTimeOffset.MinValue))
                {
                    latestSubmit = pe.UpdatedAt;
                }

                risksByEmployee.TryGetValue(pe.EmployeeId, out var empRisks);
                var tier = calib?.CalibratedTier
                    ?? (pe.TotalScore.HasValue
                        ? RankingRules.DetermineTier(pe.TotalScore.Value, empRisks, rankingConfiguration)
                        : "B");
                if (tier is "A1" or "A2" or "A3") tierA++;
                else if (tier == "B") tierB++;
                else if (tier == "C") tierC++;
                else tierD++;
            }

            var tierAPercent = totalDeptEmps > 0 ? Math.Round(tierA * 100m / totalDeptEmps, 1) : 0m;
            var maxDeptA = totalDeptEmps > 0 ? Math.Max(1, (int)Math.Ceiling(totalDeptEmps * 0.20m)) : 0;
            var isCompliant = tierA <= maxDeptA;

            string status = "NOT_STARTED";
            if (deptEvaluations.Count > 0)
            {
                if (deptEvaluations.All(pe => pe.Status == PerformanceEvaluationStatus.Final))
                {
                    status = "FINAL";
                }
                else if (deptEvaluations.All(pe => pe.Status == PerformanceEvaluationStatus.Approved))
                {
                    status = "APPROVED";
                }
                else if (deptEvaluations.Any(pe => pe.Status == PerformanceEvaluationStatus.InReview))
                {
                    status = "IN_REVIEW";
                }
                else if (deptEvaluations.Any(pe => pe.Status == PerformanceEvaluationStatus.Calculated))
                {
                    status = "IN_PROGRESS";
                }
            }

            deptSummaries.Add(new DepartmentApprovalSummaryDto(
                dept.Id,
                dept.Code,
                dept.Name,
                null,
                totalDeptEmps,
                evaluatedCount,
                status,
                avgScore,
                tierA,
                tierAPercent,
                tierB,
                tierC,
                tierD,
                isCompliant,
                reviewerComment,
                latestSubmit));
        }

        var totalBranchEmployees = employees.Count;
        var totalBranchEvaluated = evaluations.Count(pe => pe.Status != PerformanceEvaluationStatus.Draft);
        var totalTierACount = deptSummaries.Sum(d => d.TierACount);
        var branchTierAPercent = totalBranchEmployees > 0 ? Math.Round(totalTierACount * 100m / totalBranchEmployees, 1) : 0m;
        var maxBranchA = totalBranchEmployees > 0 ? Math.Max(1, (int)Math.Ceiling(totalBranchEmployees * 0.20m)) : 0;
        var isBranchCompliant = totalTierACount <= maxBranchA;

        var periodDto = new EvaluationPeriodDto(
            period.Id,
            period.Code,
            period.Name,
            period.StartDate,
            period.EndDate,
            period.PeriodType.ToString().ToUpperInvariant(),
            period.Status.ToString().ToUpperInvariant());

        var canFinalize = period.Status is PeriodStatus.Open or PeriodStatus.InReview
            && deptSummaries.Count > 0
            && deptSummaries.All(d => d.Status is "IN_REVIEW" or "APPROVED" or "FINAL");

        return new BranchApprovalOverviewDto(
            periodDto,
            totalBranchEmployees,
            totalBranchEvaluated,
            totalTierACount,
            branchTierAPercent,
            20.0m,
            isBranchCompliant,
            canFinalize,
            deptSummaries);
    }

    public async Task<CalibrationDecisionResultDto> RejectDepartmentCalibrationAsync(
        Guid departmentId,
        RejectCalibrationRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await ResolveEmployeeAsync(currentUserId, cancellationToken);

        var period = await db.EvaluationPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PeriodId, cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", request.PeriodId);

        if (period.Status is PeriodStatus.Locked or PeriodStatus.Closed)
        {
            throw new BusinessRuleException(
                ErrorCodes.Conflict,
                "Kỳ đánh giá đã khóa hoặc đóng, không thể yêu cầu hiệu chuẩn lại.");
        }

        var department = await db.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == departmentId, cancellationToken)
            ?? throw new NotFoundException("Phòng ban", departmentId);

        var deptEmployeeIds = await db.Employees
            .AsNoTracking()
            .Where(e => e.DepartmentId == departmentId)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        var evaluations = await db.PerformanceEvaluations
            .Where(pe => pe.EvaluationPeriodId == request.PeriodId
                && deptEmployeeIds.Contains(pe.EmployeeId))
            .ToListAsync(cancellationToken);

        if (evaluations.Count == 0)
        {
            throw new NotFoundException("Hồ sơ hiệu chuẩn của phòng ban", departmentId);
        }

        var now = DateTimeOffset.UtcNow;
        var reasonTrimmed = request.Reason.Trim();

        foreach (var pe in evaluations)
        {
            pe.Status = PerformanceEvaluationStatus.Calculated;
            var (comment, coachPlan, qualScores, calib, _, appr) = ParseStoredComment(pe.Comment);
            pe.Comment = BuildStoredComment(comment, coachPlan, qualScores.Values.ToList(), calib, reasonTrimmed, appr);
            pe.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        LogCalibrationRejected(logger, departmentId, request.PeriodId, reasonTrimmed, null);

        return new CalibrationDecisionResultDto(
            departmentId,
            request.PeriodId,
            "REVISION_REQUIRED",
            "Đã trả hồ sơ hiệu chuẩn về Trưởng phòng để yêu cầu hiệu chuẩn lại kèm ý kiến chỉ đạo.");
    }

    public async Task<FinalizeResultDto> FinalizeAndLockPeriodAsync(
        FinalizeEvaluationPeriodRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await ResolveEmployeeAsync(currentUserId, cancellationToken);

        var period = await db.EvaluationPeriods
            .FirstOrDefaultAsync(p => p.Id == request.PeriodId, cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", request.PeriodId);

        if (period.Status == PeriodStatus.Locked)
        {
            throw new BusinessRuleException(
                ErrorCodes.Conflict,
                "Kỳ đánh giá đã được khóa trước đó.");
        }

        var evaluations = await db.PerformanceEvaluations
            .Where(pe => pe.EvaluationPeriodId == request.PeriodId)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        period.Status = PeriodStatus.Locked;
        period.UpdatedAt = now;

        var comments = request.ApprovalComments?.Trim();

        foreach (var pe in evaluations)
        {
            pe.Status = PerformanceEvaluationStatus.Final;
            pe.ApprovedAt = now;
            pe.UpdatedAt = now;

            if (!string.IsNullOrWhiteSpace(comments))
            {
                var (comment, coachPlan, qualScores, calib, recalib, _) = ParseStoredComment(pe.Comment);
                pe.Comment = BuildStoredComment(comment, coachPlan, qualScores.Values.ToList(), calib, recalib, comments);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        LogPeriodFinalized(logger, request.PeriodId, evaluations.Count, null);

        return new FinalizeResultDto(
            request.PeriodId,
            "LOCKED",
            now,
            evaluations.Count,
            "Kỳ đánh giá đã được phê chuẩn chính thức và khóa dữ liệu (Hard-Lock) thành công.");
    }

    private static (decimal BusinessScore, decimal ServiceQualityScore, decimal OperationsScore, decimal QualitativeScore, decimal TotalScore)
        ComputeEmployeeScores(
            Employee employee,
            EvaluationProfile? profile,
            Dictionary<Guid, decimal>? targetsDict,
            Dictionary<Guid, decimal>? actualsDict,
            IReadOnlyCollection<RiskEvent>? riskEvents,
            PerformanceEvaluation? pe)
    {
        if (pe?.TotalScore != null && pe.Status != PerformanceEvaluationStatus.Draft)
        {
            var (_, _, qualScores, _, _, _) = ParseStoredComment(pe.Comment);
            decimal qualSum = qualScores.Count > 0 ? qualScores.Values.Average(q => q.Score) : 0m;
            decimal total = pe.TotalScore.Value;
            return (total, total, total, qualSum, total);
        }

        decimal businessScore = 0m;
        decimal serviceQualityScore = 0m;
        decimal operationsScore = 0m;
        decimal totalWeightedScore = 0m;
        decimal qualitativeScore = 0m;

        if (profile?.EvaluationProfileItems is { Count: > 0 })
        {
            targetsDict ??= new();
            actualsDict ??= new();

            foreach (var item in profile.EvaluationProfileItems)
            {
                var kpiGroup = item.KpiGroup;
                var groupKpis = kpiGroup?.KpiDefinitions?.Where(kd => kd.Status == KpiStatus.Active).ToList() ?? new();

                decimal groupScore;
                if (groupKpis.Count > 0)
                {
                    var scores = groupKpis.Select(kpi =>
                    {
                        targetsDict.TryGetValue(kpi.Id, out var targetVal);
                        actualsDict.TryGetValue(kpi.Id, out var actualVal);

                        decimal achievement = 0m;
                        if (targetVal > 0)
                        {
                            if (kpi.Direction == KpiDirection.LowerBetter)
                            {
                                achievement = actualVal > 0
                                    ? Math.Round((targetVal / actualVal) * 100m, 2)
                                    : 100m;
                            }
                            else
                            {
                                achievement = Math.Round((actualVal / targetVal) * 100m, 2);
                            }
                        }
                        else if (actualVal > 0)
                        {
                            achievement = 100m;
                        }

                        if (kpi.MaxAchievementPercent.HasValue && achievement > kpi.MaxAchievementPercent.Value)
                        {
                            achievement = kpi.MaxAchievementPercent.Value;
                        }

                        return achievement;
                    });

                    groupScore = Math.Round(scores.Average(), 2);
                }
                else
                {
                    groupScore = 100m;
                    qualitativeScore = 100m;
                }

                totalWeightedScore += (groupScore * item.WeightPercent) / 100m;

                var codeUpper = kpiGroup?.Code?.ToUpperInvariant() ?? string.Empty;
                if (codeUpper.Contains("KD") || codeUpper.Contains("SALES") || codeUpper.Contains("BUSINESS"))
                {
                    businessScore = groupScore;
                }
                else if (codeUpper.Contains("CLDV") || codeUpper.Contains("SERVICE") || codeUpper.Contains("CSAT"))
                {
                    serviceQualityScore = groupScore;
                }
                else if (codeUpper.Contains("TT") || codeUpper.Contains("OPER") || codeUpper.Contains("VAN_HANH"))
                {
                    operationsScore = groupScore;
                }
            }
        }
        else
        {
            totalWeightedScore = pe?.TotalScore ?? 80m;
            businessScore = totalWeightedScore;
            serviceQualityScore = totalWeightedScore;
            operationsScore = totalWeightedScore;
        }

        var totalRiskPenalty = riskEvents?.Sum(re => MapRiskPenalty(re.Severity)) ?? 0m;
        var finalScore = Math.Max(0m, Math.Round(totalWeightedScore - totalRiskPenalty, 2));

        return (businessScore, serviceQualityScore, operationsScore, qualitativeScore, pe?.TotalScore ?? finalScore);
    }

    private static GaussianDistributionDto ComputeGaussianBellCurve(List<CalibrationEmployeeRowDto> roster)
    {
        var count = roster.Count;
        var mean = count > 0 ? Math.Round(roster.Average(r => r.TotalScore), 2) : 50m;

        decimal stdDev = 15m;
        if (count > 1)
        {
            var variance = (double)roster.Average(r => (r.TotalScore - mean) * (r.TotalScore - mean));
            stdDev = variance > 0 ? Math.Max(1m, Math.Round((decimal)Math.Sqrt(variance), 2)) : 15m;
        }

        var points = new List<GaussianBellCurvePointDto>();
        var mu = (double)mean;
        var sigma = (double)stdDev;
        var sqrt2Pi = Math.Sqrt(2 * Math.PI);

        for (int x = 0; x <= 100; x += 5)
        {
            var diff = x - mu;
            var exponent = -0.5 * Math.Pow(diff / sigma, 2);
            var yVal = (1.0 / (sigma * sqrt2Pi)) * Math.Exp(exponent) * 1000.0;
            points.Add(new GaussianBellCurvePointDto(x, Math.Round((decimal)yVal, 2)));
        }

        return new GaussianDistributionDto(
            mean,
            stdDev,
            points,
            TargetTierAPercent: 20.0m,
            TargetTierBPercent: 60.0m,
            TargetTierCPercent: 15.0m,
            TargetTierDPercent: 5.0m);
    }

    private static CalibrationQuotaDto ComputeCalibrationQuota(List<CalibrationEmployeeRowDto> roster)
    {
        var count = roster.Count;
        var maxTierA = count > 0 ? Math.Max(1, (int)Math.Ceiling(count * 0.20m)) : 0;
        var proposedA = roster.Count(r => r.ProposedTier is "A1" or "A2" or "A3");
        var calibA = roster.Count(r => r.CalibratedTier is "A1" or "A2" or "A3");
        var calibB = roster.Count(r => r.CalibratedTier == "B");
        var calibC = roster.Count(r => r.CalibratedTier == "C");
        var calibD = roster.Count(r => r.CalibratedTier == "D");

        var calibAPercent = count > 0 ? Math.Round(calibA * 100m / count, 1) : 0m;
        var calibBPercent = count > 0 ? Math.Round(calibB * 100m / count, 1) : 0m;
        var calibCPercent = count > 0 ? Math.Round(calibC * 100m / count, 1) : 0m;
        var calibDPercent = count > 0 ? Math.Round(calibD * 100m / count, 1) : 0m;

        var isCompliant = calibA <= maxTierA;
        var note = isCompliant
            ? (count > 0 && calibA == maxTierA ? "Đạt trần định mức phân bổ Tier A (≤ 20%)" : "Tuân thủ hạn ngạch phân bổ Tier A (≤ 20%)")
            : $"Vượt hạn ngạch Tier A: {calibA}/{maxTierA} suất ({calibAPercent}%)";

        return new CalibrationQuotaDto(
            TotalEmployees: count,
            MaxTierAQuota: maxTierA,
            ProposedTierACount: proposedA,
            CalibratedTierACount: calibA,
            CalibratedTierAPercent: calibAPercent,
            CalibratedTierBCount: calibB,
            CalibratedTierBPercent: calibBPercent,
            CalibratedTierCCount: calibC,
            CalibratedTierCPercent: calibCPercent,
            CalibratedTierDCount: calibD,
            CalibratedTierDPercent: calibDPercent,
            IsCompliant: isCompliant,
            ComplianceNote: note);
    }

    private async Task<EvaluationProfile?> FindProfileAsync(
        Guid jobPositionId,
        EvaluationPeriod period,
        CancellationToken cancellationToken)
    {
        var profiles = await LoadProfilesByPositionAsync([jobPositionId], period, cancellationToken);
        return profiles.GetValueOrDefault(jobPositionId);
    }

    /// <summary>
    /// A job position keeps its profile history; pick the one effective in the period, then ACTIVE, then the latest.
    /// </summary>
    private async Task<Dictionary<Guid, EvaluationProfile>> LoadProfilesByPositionAsync(
        IEnumerable<Guid> jobPositionIds,
        EvaluationPeriod period,
        CancellationToken cancellationToken)
    {
        var ids = jobPositionIds.Distinct().ToList();
        var profiles = await db.EvaluationProfiles
            .AsNoTracking()
            .Include(ep => ep.EvaluationProfileItems)
                .ThenInclude(epi => epi.KpiGroup)
                    .ThenInclude(kg => kg.KpiDefinitions)
            .Where(ep => ids.Contains(ep.JobPositionId))
            .ToListAsync(cancellationToken);

        return profiles
            .GroupBy(ep => ep.JobPositionId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(ep => ep.EffectiveFrom <= period.EndDate
                        && (ep.EffectiveTo == null || ep.EffectiveTo >= period.StartDate))
                    .ThenByDescending(ep => ep.Status == EvaluationProfileStatus.Active)
                    .ThenByDescending(ep => ep.EffectiveFrom)
                    .First());
    }

    /// <summary>
    /// Throws unless the caller's evaluation:<paramref name="action"/> scope covers <paramref name="departmentId"/>.
    /// All bypasses the check; Dept/Branch cover the caller's department subtree (or branch subtree); Own and no grant cover nothing.
    /// </summary>
    private async Task EnsureDepartmentInScopeAsync(
        Guid actorAccountId,
        string action,
        Employee reviewer,
        Guid departmentId,
        CancellationToken cancellationToken)
    {
        var granted = await permissions.GetEffectivePermissionsAsync(actorAccountId, cancellationToken);
        var level = AccessScope.LevelFor(granted, "evaluation", action);
        if (level >= AccessScope.AllLevel)
        {
            return;
        }

        var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), cancellationToken);
        var covered = AccessScope.DepartmentsInScope(level, reviewer.DepartmentId, hierarchy);
        if (covered is null || !covered.Contains(departmentId))
        {
            throw new ForbiddenException("Bạn không có quyền thao tác đánh giá trên phòng ban/nhân sự này.");
        }
    }

    private async Task<Employee> ResolveEmployeeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var employee = await db.Employees
            .AsNoTracking()
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == userId || e.Account!.Id == userId, cancellationToken);

        if (employee is null || employee.Status != EmployeeStatus.Active)
        {
            throw new UnauthorizedException("Không tìm thấy thông tin nhân viên hợp lệ hoặc tài khoản đã bị khóa.");
        }

        return employee;
    }

    private static decimal MapRiskPenalty(RiskEventSeverity severity) => severity switch
    {
        RiskEventSeverity.Critical => 20m,
        RiskEventSeverity.High => 10m,
        RiskEventSeverity.Medium => 5m,
        RiskEventSeverity.Low => 2m,
        _ => 0m,
    };

    /// <summary>Reads the tier thresholds (best first) and the veto setting. Missing configuration is an error, never a guess.</summary>
    private async Task<RankingConfiguration> LoadRankingConfigurationAsync(CancellationToken cancellationToken)
    {
        var tiers = await db.RankingTierThresholds
            .AsNoTracking()
            .OrderBy(t => t.RankOrder)
            .ToListAsync(cancellationToken);
        var veto = await db.RankingVetoSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (tiers.Count == 0 || veto is null)
        {
            throw new InvalidOperationException(
                "Chưa cấu hình xếp loại (ranking_tier_threshold, ranking_veto_setting). Hãy chạy DbMigrator.");
        }

        return new RankingConfiguration(tiers, veto.TriggerSeverity, veto.CapTierCode);
    }

    /// <summary>Risk events of the given employees that happened inside the period and were not dismissed, grouped by employee.</summary>
    private async Task<Dictionary<Guid, List<RiskEvent>>> LoadRiskEventsByEmployeeAsync(
        List<Guid> employeeIds,
        EvaluationPeriod? period,
        CancellationToken cancellationToken)
    {
        if (period is null || employeeIds.Count == 0)
        {
            return [];
        }

        var periodStart = period.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var periodEnd = period.EndDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var riskEvents = await db.RiskEvents
            .AsNoTracking()
            .Where(re => employeeIds.Contains(re.EmployeeId)
                && re.OccurredAt >= periodStart
                && re.OccurredAt <= periodEnd
                && re.Status != RiskEventStatus.Dismissed)
            .ToListAsync(cancellationToken);

        return riskEvents.GroupBy(re => re.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
    }

    private static string? BuildStoredComment(
        string? comment,
        string? coachingPlan,
        List<QualitativeScoreInputDto>? qualitativeScores,
        InternalCalibrationData? calibrationData = null,
        string? recalibrationReason = null,
        string? approvalComments = null)
    {
        var trimmedComment = comment?.Trim();
        var markers = new List<string>();

        if (!string.IsNullOrWhiteSpace(coachingPlan))
        {
            markers.Add($"<!-- COACHING_PLAN: {coachingPlan.Trim()} -->");
        }

        if (qualitativeScores is { Count: > 0 })
        {
            var json = JsonSerializer.Serialize(qualitativeScores, JsonOptions);
            markers.Add($"<!-- QUALITATIVE_JSON: {json} -->");
        }

        if (calibrationData is not null)
        {
            var json = JsonSerializer.Serialize(calibrationData, JsonOptions);
            markers.Add($"<!-- CALIBRATION_JSON: {json} -->");
        }

        if (!string.IsNullOrWhiteSpace(recalibrationReason))
        {
            markers.Add($"<!-- RECALIBRATION_REASON: {recalibrationReason.Trim()} -->");
        }

        if (!string.IsNullOrWhiteSpace(approvalComments))
        {
            markers.Add($"<!-- APPROVAL_COMMENTS: {approvalComments.Trim()} -->");
        }

        if (markers.Count == 0)
        {
            return trimmedComment;
        }

        var markerBlock = string.Join("\n", markers);
        return string.IsNullOrWhiteSpace(trimmedComment)
            ? markerBlock
            : $"{trimmedComment}\n\n{markerBlock}";
    }

    private static (
        string? Comment,
        string? CoachingPlan,
        Dictionary<Guid, QualitativeScoreInputDto> QualitativeScores,
        InternalCalibrationData? CalibrationData,
        string? RecalibrationReason,
        string? ApprovalComments) ParseStoredComment(string? storedComment)
    {
        if (string.IsNullOrWhiteSpace(storedComment))
        {
            return (null, null, new(), null, null, null);
        }

        var working = storedComment;
        string? coachingPlan = null;
        var qualDict = new Dictionary<Guid, QualitativeScoreInputDto>();
        InternalCalibrationData? calibData = null;
        string? recalibrationReason = null;
        string? approvalComments = null;

        var coachMatch = CoachingPlanMarkerRegex().Match(working);
        if (coachMatch.Success)
        {
            coachingPlan = coachMatch.Groups[1].Value.Trim();
            working = working.Replace(coachMatch.Value, "");
        }

        var qualMatch = QualitativeMarkerRegex().Match(working);
        if (qualMatch.Success)
        {
            try
            {
                var json = qualMatch.Groups[1].Value.Trim();
                var items = JsonSerializer.Deserialize<List<QualitativeScoreInputDto>>(json, JsonOptions) ?? new();
                qualDict = items.ToDictionary(i => i.KpiGroupId, i => i);
            }
            catch
            {
                // ignore malformed qualitative JSON
            }
            working = working.Replace(qualMatch.Value, "");
        }

        var calibMatch = CalibrationMarkerRegex().Match(working);
        if (calibMatch.Success)
        {
            try
            {
                var json = calibMatch.Groups[1].Value.Trim();
                calibData = JsonSerializer.Deserialize<InternalCalibrationData>(json, JsonOptions);
            }
            catch
            {
                // ignore malformed calibration JSON
            }
            working = working.Replace(calibMatch.Value, "");
        }

        var recalibMatch = RecalibrationReasonMarkerRegex().Match(working);
        if (recalibMatch.Success)
        {
            recalibrationReason = recalibMatch.Groups[1].Value.Trim();
            working = working.Replace(recalibMatch.Value, "");
        }

        var approvalMatch = ApprovalCommentsMarkerRegex().Match(working);
        if (approvalMatch.Success)
        {
            approvalComments = approvalMatch.Groups[1].Value.Trim();
            working = working.Replace(approvalMatch.Value, "");
        }

        var clean = working.Trim();
        return (string.IsNullOrEmpty(clean) ? null : clean, coachingPlan, qualDict, calibData, recalibrationReason, approvalComments);
    }
}
