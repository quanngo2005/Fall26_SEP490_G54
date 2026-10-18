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
    ILogger<EvaluationService> logger) : IEvaluationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [GeneratedRegex(@"<!--\s*COACHING_PLAN:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex CoachingPlanMarkerRegex();

    [GeneratedRegex(@"<!--\s*QUALITATIVE_JSON:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex QualitativeMarkerRegex();

    private static readonly Action<ILogger, Guid, Guid, decimal, string, Exception?> LogEvaluationSaved =
        LoggerMessage.Define<Guid, Guid, decimal, string>(
            LogLevel.Information,
            new EventId(1, "EvaluationSaved"),
            "Performance evaluation saved for Employee {EmployeeId} in Period {PeriodId}: TotalScore={TotalScore}, Status={Status}");

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
                .ThenInclude(jp => jp.EvaluationProfile)
                    .ThenInclude(ep => ep.EvaluationProfileItems)
                        .ThenInclude(epi => epi.KpiGroup)
                            .ThenInclude(kg => kg.KpiDefinitions)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
            ?? throw new NotFoundException("Nhân viên", employeeId);

        var period = await db.EvaluationPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", periodId);

        var profile = employee.JobPosition.EvaluationProfile
            ?? throw new NotFoundException("Hồ sơ đánh giá của vị trí công việc", employee.JobPosition.Id);

        var targetAssignments = await db.TargetAssignments
            .AsNoTracking()
            .Where(ta => ta.EmployeeId == employeeId
                && ta.EvaluationPeriodId == periodId
                && ta.Status == TargetAssignmentStatus.Active)
            .ToListAsync(cancellationToken);

        var actualProgresses = await db.ActualProgresses
            .AsNoTracking()
            .Where(ap => ap.EmployeeId == employeeId
                && ap.EvaluationPeriodId == periodId
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
            .Include(pe => pe.ReviewerEmployee)
            .FirstOrDefaultAsync(pe => pe.EmployeeId == employeeId && pe.EvaluationPeriodId == periodId, cancellationToken);

        var (cleanComment, coachingPlan, qualitativeSaved) = ParseStoredComment(existingEvaluation?.Comment);

        var actualsDict = actualProgresses
            .GroupBy(ap => ap.KpiDefinitionId)
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
        var proposedTier = DetermineProposedTier(totalCalculated, riskEvents);

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
            existingEvaluation?.ReviewerEmployeeId,
            existingEvaluation?.ReviewerEmployee?.FullName,
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
                .ThenInclude(jp => jp.EvaluationProfile)
                    .ThenInclude(ep => ep.EvaluationProfileItems)
                        .ThenInclude(epi => epi.KpiGroup)
                            .ThenInclude(kg => kg.KpiDefinitions)
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId, cancellationToken)
            ?? throw new NotFoundException("Nhân viên", request.EmployeeId);

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

        var profile = employee.JobPosition.EvaluationProfile
            ?? throw new NotFoundException("Hồ sơ đánh giá của vị trí công việc", employee.JobPosition.Id);

        var targetAssignments = await db.TargetAssignments
            .AsNoTracking()
            .Where(ta => ta.EmployeeId == request.EmployeeId
                && ta.EvaluationPeriodId == request.PeriodId
                && ta.Status == TargetAssignmentStatus.Active)
            .ToListAsync(cancellationToken);

        var actualProgresses = await db.ActualProgresses
            .AsNoTracking()
            .Where(ap => ap.EmployeeId == request.EmployeeId
                && ap.EvaluationPeriodId == request.PeriodId
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
            .GroupBy(ap => ap.KpiDefinitionId)
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
        var proposedTier = DetermineProposedTier(finalScore, riskEvents);

        var storedComment = BuildStoredComment(request.Comment, request.CoachingPlan, request.QualitativeScores);
        var now = DateTimeOffset.UtcNow;
        var targetStatus = request.IsDraft
            ? PerformanceEvaluationStatus.Draft
            : PerformanceEvaluationStatus.Calculated;

        var existingEvaluation = await db.PerformanceEvaluations
            .FirstOrDefaultAsync(pe => pe.EmployeeId == request.EmployeeId && pe.EvaluationPeriodId == request.PeriodId, cancellationToken);

        Guid evaluationId;
        if (existingEvaluation is not null)
        {
            existingEvaluation.TotalScore = finalScore;
            existingEvaluation.ReviewerEmployeeId = reviewer.Id;
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
                ReviewerEmployeeId = reviewer.Id,
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

        return employees.Select(emp =>
        {
            evalDict.TryGetValue(emp.Id, out var pe);
            var status = pe != null ? pe.Status.ToString().ToUpperInvariant() : "NOT_STARTED";
            var tier = pe?.TotalScore != null ? DetermineProposedTier(pe.TotalScore.Value, null) : null;

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

    private static string DetermineProposedTier(decimal totalScore, IReadOnlyCollection<RiskEvent>? riskEvents)
    {
        var hasCriticalRisk = riskEvents?.Any(re => re.Severity == RiskEventSeverity.Critical) ?? false;
        if (hasCriticalRisk)
        {
            return totalScore >= 70m ? "B" : "C";
        }

        if (totalScore >= 95m) return "A1";
        if (totalScore >= 90m) return "A2";
        if (totalScore >= 85m) return "A3";
        if (totalScore >= 70m) return "B";
        return "C";
    }

    private static string? BuildStoredComment(
        string? comment,
        string? coachingPlan,
        List<QualitativeScoreInputDto>? qualitativeScores)
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

        if (markers.Count == 0)
        {
            return trimmedComment;
        }

        var markerBlock = string.Join("\n", markers);
        return string.IsNullOrWhiteSpace(trimmedComment)
            ? markerBlock
            : $"{trimmedComment}\n\n{markerBlock}";
    }

    private static (string? Comment, string? CoachingPlan, Dictionary<Guid, QualitativeScoreInputDto> QualitativeScores) ParseStoredComment(string? storedComment)
    {
        if (string.IsNullOrWhiteSpace(storedComment))
        {
            return (null, null, new());
        }

        var working = storedComment;
        string? coachingPlan = null;
        var qualDict = new Dictionary<Guid, QualitativeScoreInputDto>();

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

        var clean = working.Trim();
        return (string.IsNullOrEmpty(clean) ? null : clean, coachingPlan, qualDict);
    }
}
