using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Workflow;
using G54.BLL.Dtos.Evaluation;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed partial class EvaluationService(
    AppDbContext db,
    ILogger<EvaluationService> logger,
    IPermissionService permissions,
    IPerformanceScoreCalculator calculator) : IEvaluationService
{
    /// <summary>
    /// An evaluation that was never sent for review (still a draft or only calculated) cannot be locked. One definition
    /// shared by the approval overview's <c>CanFinalize</c> flag and the finalize action that enforces it.
    /// </summary>
    private static readonly Expression<Func<PerformanceEvaluation, bool>> NotReadyToLock =
        pe => pe.Status == PerformanceEvaluationStatus.Draft || pe.Status == PerformanceEvaluationStatus.Calculated;

    private static readonly Func<PerformanceEvaluation, bool> NotReadyToLockCompiled = NotReadyToLock.Compile();

    private static readonly Action<ILogger, Guid, Guid, decimal?, string, Exception?> LogEvaluationSaved =
        LoggerMessage.Define<Guid, Guid, decimal?, string>(
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

    private static readonly Action<ILogger, Guid, Guid, int, Exception?> LogCalibrationApproved =
        LoggerMessage.Define<Guid, Guid, int>(
            LogLevel.Information,
            new EventId(5, "CalibrationApproved"),
            "Department calibration approved for Dept {DepartmentId} in Period {PeriodId}: TotalApproved={TotalApproved}");

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

        var existingEvaluation = await db.PerformanceEvaluations
            .AsNoTracking()
            .FirstOrDefaultAsync(pe => pe.EmployeeId == employeeId && pe.EvaluationPeriodId == periodId, cancellationToken);

        var stored = ParseStoredComment(existingEvaluation?.Comment, logger);

        // The same calculator scores the form, the form save and the calculation endpoint.
        var scored = (await calculator.CalculateAsync(
            [new PerformanceScoreSubject(employee.Id, employee.DepartmentId, profile, stored.QualitativeScores)],
            period,
            cancellationToken))[employee.Id];

        var riskDtos = scored.RiskEvents.Select(re => new EvaluationRiskEventDto(
            re.Id,
            re.Code,
            re.EventType,
            re.Description,
            re.Severity.ToString().ToUpperInvariant(),
            RiskPenalty(re),
            re.OccurredAt)).ToList();

        var totalRiskPenalty = scored.RiskPenalty;

        var criteriaGroups = new List<EvaluationCriterionGroupDto>();
        var (businessScore, serviceQualityScore, operationsScore) = scored.ComponentScores();

        foreach (var group in scored.Groups)
        {
            // A KPI without a target or progress has no value to show (N/A, never a zero); it keeps its configured share.
            var kpiItemDtos = group.Kpis
                .Select(kpi => new EvaluationKpiItemDto(
                    kpi.KpiDefinitionId,
                    kpi.Code,
                    kpi.Name,
                    kpi.Unit,
                    Math.Round(kpi.SharePercent, 2),
                    kpi.TargetValue is > 0m ? kpi.TargetValue : null,
                    kpi.ResolvedActualValue ?? kpi.ActualValue,
                    kpi.AchievementPercentCapped,
                    kpi.ComponentScore))
                .ToList();

            criteriaGroups.Add(new EvaluationCriterionGroupDto(
                group.EvaluationProfileItemId,
                group.CatalogGroupCode,
                group.Name,
                group.WeightPercent,
                group.Score,
                kpiItemDtos,
                group.QualitativeScore,
                group.QualitativeNotes,
                group.IsQualitative));
        }

        // A draft or calculated evaluation shows the score just calculated, so every group adds up to the total. One that
        // was sent for review or went further keeps the score stored with it: a stored score is never replaced.
        var keepsStoredScore = existingEvaluation is not null
            && existingEvaluation.Status is not (PerformanceEvaluationStatus.Draft or PerformanceEvaluationStatus.Calculated);
        var totalScore = keepsStoredScore ? existingEvaluation!.TotalScore : scored.TotalScore;
        var downgradeConfiguration = await LoadDowngradeConfigurationAsync(cancellationToken);
        var proposedTier = totalScore.HasValue
            ? TierDeterminer.DetermineTier(totalScore.Value, scored.RiskEvents, downgradeConfiguration)
            : null;

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
            totalScore,
            proposedTier,
            scored.WeightedScore);

        var currentEvaluationDto = new PerformanceEvaluationStateDto(
            existingEvaluation?.Id,
            existingEvaluation?.TotalScore,
            (existingEvaluation?.Status ?? PerformanceEvaluationStatus.Draft).ToString().ToUpperInvariant(),
            null,
            null,
            existingEvaluation?.CalculatedAt,
            existingEvaluation?.ApprovedAt,
            stored.Comment,
            stored.CoachingPlan,
            !string.IsNullOrWhiteSpace(stored.CoachingPlan),
            stored.GeneralQualitativeScore);

        var pendingSubmissionCount = (await LoadPendingSubmissionCountsAsync([employeeId], period, cancellationToken))
            .GetValueOrDefault(employeeId);

        var openAppealCount = existingEvaluation is null
            ? 0
            : await db.Appeals
                .AsNoTracking()
                .CountAsync(a => a.PerformanceEvaluationId == existingEvaluation.Id
                    && (a.Status == AppealStatus.Open || a.Status == AppealStatus.UnderReview), cancellationToken);
        var hasOpenDispute = existingEvaluation?.IsDisputedPending() == true || openAppealCount > 0;

        var previousPeriod = await LoadPreviousPeriodSummaryAsync(
            employeeId, period, totalScore, downgradeConfiguration, cancellationToken);

        // The reviewer needs evaluation:submit over the employee's department and cannot evaluate themselves or a disputed score.
        var canEdit = period.Status is PeriodStatus.Open or PeriodStatus.InReview
            && (existingEvaluation == null || existingEvaluation.Status is PerformanceEvaluationStatus.Draft or PerformanceEvaluationStatus.Calculated)
            && employee.Id != reviewer.Id
            && !hasOpenDispute
            && await IsDepartmentInScopeAsync(currentUserId, "submit", reviewer, employee.DepartmentId, cancellationToken);

        return new EvaluationFormDto(
            employeeDto,
            periodDto,
            profileDto,
            criteriaGroups,
            riskDtos,
            summaryDto,
            currentEvaluationDto,
            canEdit,
            pendingSubmissionCount,
            openAppealCount,
            hasOpenDispute,
            previousPeriod);
    }

    /// <summary>
    /// The latest earlier period of the same type in which the employee has a scored evaluation that is not a draft, with
    /// the score gap to the current total. No such period means no comparison (null), never a zero. The stored score and the
    /// stored calibrated tier of an earlier period are not recalculated (the tier is recalculated only when none was
    /// stored), so the gap may mix two scoring formulas.
    /// </summary>
    private async Task<PreviousPeriodSummaryDto?> LoadPreviousPeriodSummaryAsync(
        Guid employeeId,
        EvaluationPeriod period,
        decimal? currentTotalScore,
        DowngradeConfiguration downgradeConfiguration,
        CancellationToken cancellationToken)
    {
        var previous = await db.PerformanceEvaluations
            .AsNoTracking()
            .Include(pe => pe.EvaluationPeriod)
            .Where(pe => pe.EmployeeId == employeeId
                && pe.TotalScore != null
                && pe.Status != PerformanceEvaluationStatus.Draft
                && pe.EvaluationPeriod.PeriodType == period.PeriodType
                && pe.EvaluationPeriod.EndDate < period.StartDate)
            .OrderByDescending(pe => pe.EvaluationPeriod.EndDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (previous?.TotalScore is not { } previousScore)
        {
            return null;
        }

        // The tier stored by calibration is the final one; it is recalculated only when none was stored.
        var tier = ParseStoredComment(previous.Comment, logger).CalibrationData?.CalibratedTier;
        if (tier is null)
        {
            var previousRisks = await LoadRiskEventsByEmployeeAsync([employeeId], previous.EvaluationPeriod, cancellationToken);
            tier = TierDeterminer.DetermineTier(
                previousScore,
                previousRisks.GetValueOrDefault(employeeId),
                downgradeConfiguration);
        }

        return new PreviousPeriodSummaryDto(
            previous.EvaluationPeriod.Code,
            previousScore,
            tier,
            currentTotalScore.HasValue ? Math.Round(currentTotalScore.Value - previousScore, 2) : null);
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
                ErrorCodes.MakerCannotBeChecker,
                "Theo quy tắc BR-02 (Maker != Checker), cấp quản lý không được tự thực hiện đánh giá hiệu suất cho chính mình.");
        }

        var employee = await db.Employees
            .AsNoTracking()
            .Include(e => e.JobPosition)
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId, cancellationToken)
            ?? throw new NotFoundException("Nhân viên", request.EmployeeId);

        await EnsureDepartmentInScopeAsync(currentUserId, "submit", reviewer, employee.DepartmentId, cancellationToken);

        ValidateSubmitText(request);

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

        var roots = EvaluationProfileTree.Build(profile.EvaluationProfileItems);
        var qualInputsDict = BuildQualitativeInputs(request.QualitativeScores, roots);

        var existingEvaluation = await db.PerformanceEvaluations
            .FirstOrDefaultAsync(pe => pe.EmployeeId == request.EmployeeId && pe.EvaluationPeriodId == request.PeriodId, cancellationToken);

        // BR-31: a score under dispute is frozen; re-submitting the form must not silently clear Disputed_Pending.
        if (existingEvaluation?.IsDisputedPending() == true)
        {
            throw PerformanceEvaluationExtensions.DisputePendingConflict(
                "Điểm đánh giá đang tranh chấp (Disputed_Pending) nên không thể cập nhật cho đến khi khiếu nại được xử lý hoặc rút lại.");
        }

        // Only a draft or a calculated evaluation can be saved again; one already sent for review, approved or
        // finalized is not overwritten (the form's CanEdit says the same, but a direct call bypasses it).
        if (existingEvaluation is not null
            && existingEvaluation.Status is not (PerformanceEvaluationStatus.Draft or PerformanceEvaluationStatus.Calculated))
        {
            throw new ConflictException(
                $"Đánh giá đang ở trạng thái {existingEvaluation.Status.ToEnumString()}, không thể lưu hoặc cập nhật.",
                ErrorCodes.PerformanceEvaluationInvalidState);
        }

        // A group the reviewer has not scored yet counts as 0, never as a full score; completing is refused.
        var unscoredQualitativeGroups = roots
            .Where(root => EvaluationProfileTree.IsQualitative(root) && !qualInputsDict.ContainsKey(root.Item.Id))
            .Select(EvaluationProfileTree.NodeName)
            .ToList();
        if (!request.IsDraft && unscoredQualitativeGroups.Count > 0)
        {
            throw new ValidationException(
                $"Chưa có điểm định tính cho nhóm: {string.Join(", ", unscoredQualitativeGroups)}. Cần chấm đủ trước khi hoàn tất đánh giá.");
        }

        // The same calculator scores the form and the calculation endpoint, so the stored score and its KPI detail rows
        // are the ones the form shows.
        var scored = (await calculator.CalculateAsync(
            [new PerformanceScoreSubject(employee.Id, employee.DepartmentId, profile, qualInputsDict)],
            period,
            cancellationToken))[employee.Id];

        if (!request.IsDraft && !scored.TotalScore.HasValue)
        {
            throw new ValidationException(
                "Hồ sơ đánh giá của vị trí chưa có nhóm tiêu chí nào để chấm điểm. Hãy cấu hình hồ sơ đánh giá trước khi hoàn tất đánh giá.");
        }

        var stored = ParseStoredComment(existingEvaluation?.Comment, logger);
        var storedComment = BuildStoredComment(
            request.Comment,
            request.CoachingPlan,
            request.QualitativeScores,
            stored.CalibrationData,
            stored.RecalibrationReason,
            stored.ApprovalComments,
            request.GeneralQualitativeScore);
        var now = DateTimeOffset.UtcNow;

        var evaluation = existingEvaluation;
        if (evaluation is null)
        {
            evaluation = new PerformanceEvaluation
            {
                Id = Guid.NewGuid(),
                EmployeeId = request.EmployeeId,
                EvaluationPeriodId = request.PeriodId,
                EvaluationProfileId = profile.Id,
            };
            await db.PerformanceEvaluations.AddAsync(evaluation, cancellationToken);
        }

        evaluation.Comment = storedComment;
        await PerformanceEvaluationService.StageAsync(db, evaluation, scored, request.IsDraft, now, cancellationToken);

        // Two reviewers saving the evaluation of the employee for this period at the same time.
        await SaveEvaluationChangesAsync(
            "Đánh giá của nhân viên trong kỳ này vừa được lưu bởi một yêu cầu khác. Hãy tải lại biểu mẫu rồi thử lại.",
            cancellationToken);

        LogEvaluationSaved(logger, request.EmployeeId, request.PeriodId, evaluation.TotalScore, evaluation.Status.ToString(), null);

        var proposedTier = evaluation.TotalScore.HasValue
            ? TierDeterminer.DetermineTier(
                evaluation.TotalScore.Value, scored.RiskEvents, await LoadDowngradeConfigurationAsync(cancellationToken))
            : null;

        var message = request.IsDraft
            ? "Bản nháp đánh giá hiệu suất đã được lưu thành công."
            : "Đánh giá hiệu suất đã được tính toán và lưu thành công.";

        return new EvaluationResultDto(
            evaluation.Id,
            request.EmployeeId,
            request.PeriodId,
            evaluation.TotalScore,
            evaluation.Status.ToString().ToUpperInvariant(),
            proposedTier,
            evaluation.CalculatedAt,
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

        // The roster covers the target department and every department beneath it, like the employee list.
        var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), cancellationToken);
        var rosterDepartments = hierarchy.Nodes.ContainsKey(targetDeptId)
            ? hierarchy.Subtree(targetDeptId)
            : [targetDeptId];

        var employees = await db.Employees
            .AsNoTracking()
            .Include(e => e.Department)
            .Include(e => e.JobPosition)
            .Where(e => rosterDepartments.Contains(e.DepartmentId) && e.Status == EmployeeStatus.Active)
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
        var downgradeConfiguration = await LoadDowngradeConfigurationAsync(cancellationToken);

        var evaluationIds = evaluations.Select(pe => pe.Id).ToList();
        var employeesWithOpenAppeal = (await db.Appeals
            .AsNoTracking()
            .Where(a => a.PerformanceEvaluationId.HasValue
                && evaluationIds.Contains(a.PerformanceEvaluationId.Value)
                && (a.Status == AppealStatus.Open || a.Status == AppealStatus.UnderReview))
            .Select(a => a.EmployeeId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var pendingSubmissions = await LoadPendingSubmissionCountsAsync(employeeIds, period, cancellationToken);
        var quantitativeScores = await LoadQuantitativeScoresAsync(employees, period, cancellationToken);
        var previousQuantitativeScores = await LoadPreviousQuantitativeScoresAsync(employees, period, cancellationToken);

        var periodAcceptsEvaluation = period?.Status is PeriodStatus.Open or PeriodStatus.InReview;
        var submitDepartments = periodAcceptsEvaluation
            ? await DepartmentsInSubmitScopeAsync(currentUserId, reviewer, hierarchy, cancellationToken)
            : null;

        return employees.Select(emp =>
        {
            evalDict.TryGetValue(emp.Id, out var pe);
            var status = pe != null ? pe.Status.ToString().ToUpperInvariant() : "NOT_STARTED";
            risksByEmployee.TryGetValue(emp.Id, out var empRisks);
            var tier = pe?.TotalScore != null
                ? TierDeterminer.DetermineTier(pe.TotalScore.Value, empRisks, downgradeConfiguration)
                : null;

            decimal? qualitativeScore = null;
            if (pe is not null)
            {
                var (_, _, qualScores, _, _, _, generalQualitativeScore) = ParseStoredComment(pe.Comment, logger);
                if (generalQualitativeScore.HasValue)
                {
                    qualitativeScore = Math.Round(generalQualitativeScore.Value, 2);
                }
                else if (qualScores.Count > 0)
                {
                    qualitativeScore = Math.Round(qualScores.Values.Average(q => q.Score), 2);
                }
            }

            var quantitativeScoreChange = quantitativeScores.GetValueOrDefault(emp.Id) is { } currentQuantitative
                && previousQuantitativeScores.GetValueOrDefault(emp.Id) is { } previousQuantitative
                    ? Math.Round(currentQuantitative - previousQuantitative, 2)
                    : (decimal?)null;

            var hasOpenDispute = pe?.IsDisputedPending() == true || employeesWithOpenAppeal.Contains(emp.Id);
            var canEvaluate = periodAcceptsEvaluation
                && (submitDepartments is null || submitDepartments.Contains(emp.DepartmentId))
                && emp.Id != reviewer.Id
                && !hasOpenDispute
                && (pe is null || pe.Status is PerformanceEvaluationStatus.Draft or PerformanceEvaluationStatus.Calculated);

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
                pe?.CalculatedAt,
                quantitativeScores.GetValueOrDefault(emp.Id),
                qualitativeScore,
                hasOpenDispute,
                pendingSubmissions.GetValueOrDefault(emp.Id),
                canEvaluate,
                pe?.UpdatedAt,
                quantitativeScoreChange);
        }).ToList();
    }

    /// <summary>
    /// Quantitative scores of the latest earlier period of the same type, scored by the same calculation as the current
    /// period so the two are comparable. No earlier period means no comparison (empty), never a zero.
    /// </summary>
    private async Task<Dictionary<Guid, decimal?>> LoadPreviousQuantitativeScoresAsync(
        List<Employee> employees,
        EvaluationPeriod? period,
        CancellationToken cancellationToken)
    {
        if (period is null)
        {
            return [];
        }

        var previousPeriod = await db.EvaluationPeriods
            .AsNoTracking()
            .Where(p => p.PeriodType == period.PeriodType && p.EndDate < period.StartDate)
            .OrderByDescending(p => p.EndDate)
            .FirstOrDefaultAsync(cancellationToken);

        return previousPeriod is null
            ? []
            : await LoadQuantitativeScoresAsync(employees, previousPeriod, cancellationToken);
    }

    /// <summary>Submitted or under-review result submissions of each employee that were submitted inside the period.</summary>
    private async Task<Dictionary<Guid, int>> LoadPendingSubmissionCountsAsync(
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

        var submitters = await db.ResultSubmissions
            .AsNoTracking()
            .Where(rs => employeeIds.Contains(rs.SubmitterEmployeeId)
                && (rs.Status == SubmissionStatus.Submitted || rs.Status == SubmissionStatus.UnderReview)
                && rs.SubmittedAt >= periodStart
                && rs.SubmittedAt <= periodEnd)
            .Select(rs => rs.SubmitterEmployeeId)
            .ToListAsync(cancellationToken);

        return submitters.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Weighted mean (by profile weight) of the achievement-based score of the profile groups that own active KPIs.
    /// An employee without a profile or without a scorable KPI group has no value (N/A, never 0).
    /// </summary>
    private async Task<Dictionary<Guid, decimal?>> LoadQuantitativeScoresAsync(
        List<Employee> employees,
        EvaluationPeriod? period,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, decimal?>();
        if (period is null || employees.Count == 0)
        {
            return result;
        }

        var profilesByPosition = await LoadProfilesByPositionAsync(employees.Select(e => e.JobPositionId), period, cancellationToken);

        // Scored by the shared calculator; the qualitative scores do not take part in the quantitative score.
        var subjects = employees
            .Where(e => profilesByPosition.ContainsKey(e.JobPositionId))
            .Select(e => new PerformanceScoreSubject(
                e.Id,
                e.DepartmentId,
                profilesByPosition[e.JobPositionId],
                new Dictionary<Guid, QualitativeScoreInputDto>()))
            .ToList();

        var scored = await calculator.CalculateAsync(subjects, period, cancellationToken);
        foreach (var (employeeId, score) in scored.Where(s => s.Value.QuantitativeScore.HasValue))
        {
            result[employeeId] = score.QuantitativeScore;
        }

        return result;
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
        var risksGroup = await LoadRiskEventsByEmployeeAsync(employeeIds, period, cancellationToken);

        // An evaluation already sent for review keeps its stored score; every other employee is scored by the shared
        // calculator in one batch. Without a profile there is nothing to score.
        var disputed = evaluations.DisputedEmployeeIds();
        var toScore = employees
            .Where(e => !disputed.Contains(e.Id)
                && profilesByPosition.ContainsKey(e.JobPositionId)
                && !KeepsStoredScore(evalDict.GetValueOrDefault(e.Id)))
            .Select(e => new PerformanceScoreSubject(
                e.Id,
                e.DepartmentId,
                profilesByPosition[e.JobPositionId],
                ParseStoredComment(evalDict.GetValueOrDefault(e.Id)?.Comment, logger).QualitativeScores))
            .ToList();
        var scoredByEmployee = await calculator.CalculateAsync(toScore, period, cancellationToken);

        var downgradeConfiguration = await LoadDowngradeConfigurationAsync(cancellationToken);
        var unrankedRows = new List<CalibrationEmployeeRowDto>();
        var disputedEmployeeIds = evaluations.DisputedEmployeeIds();

        foreach (var emp in employees)
        {
            // BR-31: a score under dispute (Disputed_Pending) is hidden from the ranking, quota and bell curve.
            if (disputedEmployeeIds.Contains(emp.Id))
            {
                continue;
            }

            evalDict.TryGetValue(emp.Id, out var pe);

            risksGroup.TryGetValue(emp.Id, out var empRisks);

            var (_, coachingPlan, _, calibData, _, _, _) = ParseStoredComment(pe?.Comment, logger);

            var (businessScore, serviceQualityScore, operationsScore, qualitativeScore, totalScore) =
                ComputeEmployeeScores(pe, scoredByEmployee.GetValueOrDefault(emp.Id));

            // An employee whose score cannot be determined has no proposed tier (N/A), never a tier of a made-up score.
            var proposedTier = totalScore.HasValue
                ? TierDeterminer.DetermineTier(totalScore.Value, empRisks, downgradeConfiguration)
                : null;
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
                    var penalty = empRisks.Sum(RiskPenalty);
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

        // Only an employee with a score is ranked; those without one follow the ranked ones (null sorts last) and have no rank.
        var rank = 0;
        var rankedRoster = sortedRows
            .Select(row => row with { Rank = row.TotalScore.HasValue ? ++rank : null })
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
            overallStatus,
            disputedEmployeeIds.Count);
    }

    public async Task<CalibrationSubmitResultDto> SubmitDepartmentCalibrationAsync(
        SubmitCalibrationRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var reviewer = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        await EnsureDepartmentInScopeAsync(currentUserId, "calibrate", reviewer, request.DepartmentId, cancellationToken);

        EnsureNoMarkerSyntax(request.Notes, "Notes");

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
        var periodEmployeeIds = employees.Select(e => e.Id).ToList();
        var existingEvaluations = await db.PerformanceEvaluations
            .Where(pe => pe.EvaluationPeriodId == request.PeriodId && periodEmployeeIds.Contains(pe.EmployeeId))
            .ToListAsync(cancellationToken);

        // BR-31: a score under dispute (Disputed_Pending) is outside the ranking, so it neither counts toward the
        // quota base nor accepts calibration until the dispute ends.
        var disputedEmployeeIds = existingEvaluations.DisputedEmployeeIds();

        var totalEmployees = employees.Count(e => !disputedEmployeeIds.Contains(e.Id));
        var maxTierAQuota = totalEmployees > 0 ? Math.Max(1, (int)Math.Ceiling(totalEmployees * 0.20m)) : 0;

        var adjustmentMap = (request.Adjustments ?? new())
            .Where(a => !disputedEmployeeIds.Contains(a.EmployeeId))
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

        var evalDict = existingEvaluations.ToDictionary(pe => pe.EmployeeId, pe => pe);
        var now = DateTimeOffset.UtcNow;
        var targetStatus = request.IsDraft
            ? PerformanceEvaluationStatus.Calculated
            : PerformanceEvaluationStatus.InReview;

        // An employee without an evaluation gets one scored by the shared calculator, like a form save. Without a profile
        // there is nothing to score, so the request is refused before anything is changed.
        var withoutEvaluation = adjustmentMap.Keys
            .Where(id => employeeMap.ContainsKey(id) && !evalDict.ContainsKey(id))
            .Select(id => employeeMap[id])
            .ToList();
        var withoutProfile = withoutEvaluation.Where(e => !profilesByPosition.ContainsKey(e.JobPositionId)).ToList();
        if (withoutProfile.Count > 0)
        {
            throw new ValidationException(
                $"Không thể hiệu chuẩn cho nhân viên chưa có đánh giá và chưa có hồ sơ đánh giá của vị trí: {string.Join(", ", withoutProfile.Select(e => e.EmployeeCode))}.");
        }

        var scoredNew = await calculator.CalculateAsync(
            withoutEvaluation
                .Select(e => new PerformanceScoreSubject(
                    e.Id,
                    e.DepartmentId,
                    profilesByPosition[e.JobPositionId],
                    new Dictionary<Guid, QualitativeScoreInputDto>()))
                .ToList(),
            period,
            cancellationToken);
        var withoutScore = withoutEvaluation.Where(e => !scoredNew[e.Id].TotalScore.HasValue).ToList();
        if (withoutScore.Count > 0)
        {
            throw new ValidationException(
                $"Không xác định được điểm của nhân viên chưa có đánh giá: {string.Join(", ", withoutScore.Select(e => e.EmployeeCode))}.");
        }

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
                // Only the calibration marker is owned here; every other stored part (including qualitative
                // entries the reader skips) is preserved as is.
                var (existingText, _, _, _, _, _, _) = ParseStoredComment(existingPe.Comment, logger);
                var baseComment = existingText is null && !string.IsNullOrWhiteSpace(request.Notes)
                    ? $"{request.Notes.Trim()}\n\n{existingPe.Comment}"
                    : existingPe.Comment;
                existingPe.Comment = SetStoredMarker(
                    baseComment,
                    CalibrationMarkerRegex(),
                    "CALIBRATION_JSON",
                    JsonSerializer.Serialize(calibData, JsonOptions));
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
                    EvaluationProfileId = profilesByPosition[emp.JobPositionId].Id,
                    Comment = newComment,
                };
                await db.PerformanceEvaluations.AddAsync(newPe, cancellationToken);

                // Stores the calculated score and its KPI rows; the calibration status replaces the one it picks.
                await PerformanceEvaluationService.StageAsync(db, newPe, scoredNew[empId], isDraft: true, now, cancellationToken);
                newPe.Status = targetStatus;
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

        await SaveEvaluationChangesAsync(
            "Bảng hiệu chuẩn của phòng ban vừa được lưu bởi một yêu cầu khác. Hãy tải lại bảng hiệu chuẩn rồi thử lại.",
            cancellationToken);

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
        var disputedEmployeeIds = evaluations.DisputedEmployeeIds();

        var risksByEmployee = await LoadRiskEventsByEmployeeAsync(allEmpIds, period, cancellationToken);
        var downgradeConfiguration = await LoadDowngradeConfigurationAsync(cancellationToken);

        var deptSummaries = new List<DepartmentApprovalSummaryDto>();

        foreach (var dept in departments)
        {
            empDeptMap.TryGetValue(dept.Id, out var deptEmps);
            deptEmps ??= new();

            // BR-31: scores under dispute (Disputed_Pending) are hidden from ranking, averages and tier counts.
            var activeDeptEmps = deptEmps.Where(e => !disputedEmployeeIds.Contains(e.Id)).ToList();

            var totalDeptEmps = activeDeptEmps.Count;
            var deptEvaluations = activeDeptEmps
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
                var (_, _, _, calib, recalibReason, _, _) = ParseStoredComment(pe.Comment, logger);
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
                        ? TierDeterminer.DetermineTier(pe.TotalScore.Value, empRisks, downgradeConfiguration)
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

        var disputedPendingCount = disputedEmployeeIds.Count;
        var totalBranchEmployees = employees.Count - disputedPendingCount;
        var totalBranchEvaluated = deptSummaries.Sum(d => d.EvaluatedEmployees);
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

        // The same condition FinalizeAndLockPeriodAsync enforces: every evaluation of the period, not only those of the
        // departments listed above, must have been sent for review.
        var notReadyToLock = await db.PerformanceEvaluations
            .AsNoTracking()
            .Where(pe => pe.EvaluationPeriodId == periodId)
            .Where(NotReadyToLock)
            .AnyAsync(cancellationToken);

        var canFinalize = period.Status is PeriodStatus.Open or PeriodStatus.InReview
            && disputedPendingCount == 0
            && !notReadyToLock
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
            deptSummaries,
            disputedPendingCount);
    }

    public async Task<CalibrationDecisionResultDto> RejectDepartmentCalibrationAsync(
        Guid departmentId,
        RejectCalibrationRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await ResolveEmployeeAsync(currentUserId, cancellationToken);

        EnsureNoMarkerSyntax(request.Reason, "Reason");

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

        foreach (var pe in evaluations.Where(e => !e.IsDisputedPending()))
        {
            pe.Status = PerformanceEvaluationStatus.Calculated;
            pe.Comment = SetStoredMarker(pe.Comment, RecalibrationReasonMarkerRegex(), "RECALIBRATION_REASON", reasonTrimmed);
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

    public async Task<DepartmentApprovalResultDto> ApproveDepartmentCalibrationAsync(
        Guid departmentId,
        ApproveDepartmentRankingRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await ResolveEmployeeAsync(currentUserId, cancellationToken);

        EnsureNoMarkerSyntax(request.Note, "Note");

        var period = await db.EvaluationPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PeriodId, cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", request.PeriodId);

        if (period.Status is PeriodStatus.Locked or PeriodStatus.Closed)
        {
            throw new BusinessRuleException(
                ErrorCodes.Conflict,
                "Kỳ đánh giá đã khóa hoặc đóng, không thể phê duyệt kết quả xếp hạng.");
        }

        var reviewer = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        await EnsureDepartmentInScopeAsync(currentUserId, "finalize", reviewer, departmentId, cancellationToken);

        var department = await db.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == departmentId, cancellationToken)
            ?? throw new NotFoundException("Phòng ban", departmentId);

        var deptEmployeeIds = await db.Employees
            .AsNoTracking()
            .Where(e => e.DepartmentId == departmentId && e.Status == EmployeeStatus.Active)
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

        var disputedPending = evaluations.DisputedEmployeeIds().Count;
        if (disputedPending > 0)
        {
            throw PerformanceEvaluationExtensions.DisputePendingConflict(
                $"Còn {disputedPending} điểm đánh giá trong phòng ban đang tranh chấp (Disputed_Pending). Hãy xử lý khiếu nại trước khi phê duyệt.");
        }

        var now = DateTimeOffset.UtcNow;
        var noteTrimmed = request.Note?.Trim();
        var approvedCount = 0;

        foreach (var pe in evaluations)
        {
            if (pe.Status is PerformanceEvaluationStatus.InReview or PerformanceEvaluationStatus.Calculated)
            {
                pe.Status = PerformanceEvaluationStatus.Approved;
                if (!string.IsNullOrWhiteSpace(noteTrimmed))
                {
                    pe.Comment = SetStoredMarker(pe.Comment, ApprovalCommentsMarkerRegex(), "APPROVAL_COMMENTS", noteTrimmed);
                }
                pe.UpdatedAt = now;
                approvedCount++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        LogCalibrationApproved(logger, departmentId, request.PeriodId, approvedCount, null);

        return new DepartmentApprovalResultDto(
            departmentId,
            request.PeriodId,
            "APPROVED",
            approvedCount,
            "Đã phê duyệt kết quả xếp hạng phòng ban thành công.");
    }

    public async Task<PreCloseCheckOverviewDto> GetPreCloseCheckAsync(
        Guid periodId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var reviewer = await ResolveEmployeeAsync(currentUserId, cancellationToken);

        var period = await db.EvaluationPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", periodId);

        var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), cancellationToken);
        var granted = await permissions.GetEffectivePermissionsAsync(currentUserId, cancellationToken);
        var inScope = AccessScope.DepartmentsInScope(
            AccessScope.LevelFor(granted, "evaluation", "read"), reviewer.DepartmentId, hierarchy);

        var departments = await db.Departments
            .AsNoTracking()
            .Where(d => d.Status == RecordStatus.Active && d.LevelType == DepartmentLevelType.Department)
            .Where(d => inScope == null || inScope.Contains(d.Id))
            .ToListAsync(cancellationToken);

        var deptIds = departments.Select(d => d.Id).ToList();

        var employees = await db.Employees
            .AsNoTracking()
            .Where(e => deptIds.Contains(e.DepartmentId) && e.Status == EmployeeStatus.Active)
            .ToListAsync(cancellationToken);
        var empIds = employees.Select(e => e.Id).ToList();

        var periodStart = period.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var periodEnd = period.EndDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        // 1. Result submissions in review / un-reconciled
        var inReviewCount = await db.ResultSubmissions
            .AsNoTracking()
            .Where(rs => empIds.Contains(rs.SubmitterEmployeeId)
                && (rs.Status == SubmissionStatus.Submitted || rs.Status == SubmissionStatus.UnderReview)
                && rs.SubmittedAt >= periodStart
                && rs.SubmittedAt <= periodEnd)
            .CountAsync(cancellationToken);

        // 2. Ad-hoc work without WorkType pending manual verification
        var manualWorkCount = await db.Works
            .AsNoTracking()
            .Where(w => empIds.Contains(w.AssigneeEmployeeId)
                && w.WorkTypeId == null
                && (w.Status == WorkStatus.Submitted || w.Status == WorkStatus.InProgress || w.Status == WorkStatus.New)
                && ((w.PlannedStartDate != null && w.PlannedStartDate <= period.EndDate && (w.PlannedEndDate == null || w.PlannedEndDate >= period.StartDate))
                    || (w.PlannedStartDate == null && w.PlannedEndDate == null)))
            .CountAsync(cancellationToken);

        // 3. Open disputes & appeals
        var openAppealsCount = await db.Appeals
            .AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId)
                && (a.Status == AppealStatus.Open || a.Status == AppealStatus.UnderReview))
            .CountAsync(cancellationToken);

        var disputedEvaluationsCount = await db.PerformanceEvaluations
            .AsNoTracking()
            .Where(pe => pe.EvaluationPeriodId == periodId
                && empIds.Contains(pe.EmployeeId)
                && pe.Status == PerformanceEvaluationStatus.Disputed)
            .CountAsync(cancellationToken);

        var totalDisputesCount = Math.Max(openAppealsCount, disputedEvaluationsCount);

        // 4. Departments not yet approved
        var evaluations = await db.PerformanceEvaluations
            .AsNoTracking()
            .Where(pe => pe.EvaluationPeriodId == periodId && empIds.Contains(pe.EmployeeId))
            .ToListAsync(cancellationToken);

        var empDeptMap = employees.ToDictionary(e => e.Id, e => e.DepartmentId);
        var evalByDept = evaluations
            .GroupBy(pe => empDeptMap.GetValueOrDefault(pe.EmployeeId))
            .ToDictionary(g => g.Key, g => g.ToList());

        var unapprovedDepts = 0;
        foreach (var dept in departments)
        {
            if (!evalByDept.TryGetValue(dept.Id, out var deptEvals) || deptEvals.Count == 0)
            {
                unapprovedDepts++;
            }
            else if (!deptEvals.All(pe => pe.Status is PerformanceEvaluationStatus.Approved or PerformanceEvaluationStatus.Final))
            {
                unapprovedDepts++;
            }
        }

        // 5. Core Banking data import completeness up to period.EndDate
        var maxProgressDate = await db.ActualProgresses
            .AsNoTracking()
            .Where(ap => ap.TargetAssignment.EvaluationPeriodId == periodId)
            .MaxAsync(ap => (DateOnly?)ap.ProgressDate, cancellationToken);

        var missingImportDays = 0;
        if (maxProgressDate == null || maxProgressDate < period.EndDate)
        {
            missingImportDays = maxProgressDate == null
                ? (period.EndDate.DayNumber - period.StartDate.DayNumber + 1)
                : Math.Max(0, period.EndDate.DayNumber - maxProgressDate.Value.DayNumber);
        }

        var items = new List<PreCloseCheckItemDto>
        {
            new(
                "IN_REVIEW_SUBMISSIONS",
                "Kết quả In Review (chưa khớp giao dịch import)",
                inReviewCount,
                inReviewCount == 0,
                "SCR-REV-03",
                "/results/verify"),
            new(
                "MANUAL_WORK_ITEMS",
                "Việc ngoài WorkType chờ duyệt tay",
                manualWorkCount,
                manualWorkCount == 0,
                "SCR-REV-04",
                "/works/unclassified-approvals"),
            new(
                "OPEN_DISPUTES",
                "Dispute / kháng nghị còn mở",
                totalDisputesCount,
                totalDisputesCount == 0,
                "SCR-APL-03",
                "/appeals/resolution"),
            new(
                "UNAPPROVED_DEPARTMENTS",
                "Phòng chưa được duyệt xếp hạng",
                unapprovedDepts,
                unapprovedDepts == 0,
                "SCR-EVL-03-D1",
                null),
            new(
                "CORE_BANKING_IMPORT",
                $"File import Core Banking đã đủ tới {period.EndDate:dd/MM/yyyy}",
                missingImportDays,
                missingImportDays == 0,
                null,
                null)
        };

        var canPublish = items.All(i => i.IsPassed);
        var totalUnresolved = items.Count(i => !i.IsPassed);

        var deadline = period.EndDate.AddDays(12).ToDateTime(new TimeOnly(17, 0), DateTimeKind.Utc);

        return new PreCloseCheckOverviewDto(
            period.Id,
            period.Code,
            period.Name,
            period.StartDate,
            period.EndDate,
            deadline,
            canPublish,
            totalUnresolved,
            items);
    }

    public async Task<FinalizeResultDto> FinalizeAndLockPeriodAsync(
        FinalizeEvaluationPeriodRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await ResolveEmployeeAsync(currentUserId, cancellationToken);

        EnsureNoMarkerSyntax(request.ApprovalComments, "ApprovalComments");

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

        // BR-31: a score under dispute cannot be locked as FINAL before the appeal is resolved or withdrawn.
        var disputedPending = evaluations.DisputedEmployeeIds().Count;
        if (disputedPending > 0)
        {
            throw PerformanceEvaluationExtensions.DisputePendingConflict(
                $"Còn {disputedPending} điểm đánh giá đang tranh chấp (Disputed_Pending). Hãy xử lý hoặc chờ nhân viên rút khiếu nại trước khi khóa kỳ đánh giá.");
        }

        // Only an evaluation that was sent for review (or already approved or final) can be locked; a draft or a
        // calculated one has not been through calibration, so the whole period is refused and nothing is changed.
        var notReady = evaluations.Count(NotReadyToLockCompiled);
        if (notReady > 0)
        {
            throw new ConflictException(
                $"Còn {notReady} đánh giá chưa được trình duyệt (trạng thái DRAFT hoặc CALCULATED). Hãy hoàn tất hiệu chuẩn và trình duyệt trước khi khóa kỳ đánh giá.",
                ErrorCodes.PerformanceEvaluationInvalidState);
        }

        var now = DateTimeOffset.UtcNow;
        period.Status = PeriodStatus.Locked;
        period.UpdatedAt = now;

        var comments = request.ApprovalComments?.Trim();

        foreach (var pe in evaluations.Where(e => e.Status != PerformanceEvaluationStatus.Final))
        {
            pe.Status = PerformanceEvaluationStatus.Final;
            pe.ApprovedAt = now;
            pe.UpdatedAt = now;

            if (!string.IsNullOrWhiteSpace(comments))
            {
                pe.Comment = SetStoredMarker(pe.Comment, ApprovalCommentsMarkerRegex(), "APPROVAL_COMMENTS", comments);
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

    private static readonly string[] BranchPeriodDisplayStatuses =
        ["IN_PROGRESS", "EVALUATING", "IN_REVIEW", "PUBLISHED", "LOCKED"];

    public async Task<BranchEvaluationPeriodsOverviewDto> GetBranchEvaluationPeriodsOverviewAsync(
        int? year,
        string? periodType,
        string? status,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var reviewer = await ResolveEmployeeAsync(currentUserId, cancellationToken);

        var typeFilter = string.Equals(periodType?.Trim(), "ALL", StringComparison.OrdinalIgnoreCase)
            ? null
            : EnumApiFormat.ParseEnum<PeriodType>(periodType, "periodType");

        string? displayStatusFilter = null;
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            displayStatusFilter = BranchPeriodDisplayStatuses
                .FirstOrDefault(s => string.Equals(s, status.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ValidationException($"Giá trị '{status}' không hợp lệ cho tham số 'status'.");
        }

        var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), cancellationToken);
        var granted = await permissions.GetEffectivePermissionsAsync(currentUserId, cancellationToken);
        var inScope = AccessScope.DepartmentsInScope(
            AccessScope.LevelFor(granted, "evaluation", "read"), reviewer.DepartmentId, hierarchy);

        DepartmentNode? branch = null;
        if (hierarchy.Nodes.ContainsKey(reviewer.DepartmentId)
            && hierarchy.Nodes[hierarchy.BranchRoot(reviewer.DepartmentId)] is { Level: DepartmentLevelType.Branch } ownBranch)
        {
            branch = ownBranch;
        }

        branch ??= hierarchy.Nodes.Values
            .Where(n => n.Level == DepartmentLevelType.Branch && n.Status == RecordStatus.Active)
            .OrderBy(n => n.Name)
            .FirstOrDefault()
            ?? throw new NotFoundException("Không tìm thấy chi nhánh đang hoạt động.");

        var branchDepartmentIds = hierarchy.Subtree(branch.Id).ToHashSet();
        var departments = hierarchy.Nodes.Values
            .Where(n => n.Level == DepartmentLevelType.Department
                && n.Status == RecordStatus.Active
                && branchDepartmentIds.Contains(n.Id)
                && (inScope is null || inScope.Contains(n.Id)))
            .OrderBy(n => n.Name)
            .ToList();

        var totalDepts = departments.Count;
        var deptIds = departments.Select(d => d.Id).ToList();

        var query = db.EvaluationPeriods
            .AsNoTracking()
            .Where(p => p.PeriodType != PeriodType.Day);

        var periodBounds = await query
            .Select(p => new { p.StartDate, p.EndDate })
            .ToListAsync(cancellationToken);
        var availableYears = periodBounds
            .SelectMany(p => new[] { p.StartDate.Year, p.EndDate.Year })
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

        if (year.HasValue)
        {
            query = query.Where(p => p.StartDate.Year == year.Value || p.EndDate.Year == year.Value);
        }

        if (typeFilter.HasValue)
        {
            query = query.Where(p => p.PeriodType == typeFilter.Value);
        }

        var periods = await query
            .OrderByDescending(p => p.StartDate)
            .ToListAsync(cancellationToken);

        var periodIds = periods.Select(p => p.Id).ToList();

        var deptEmployees = await db.Employees
            .AsNoTracking()
            .Where(e => deptIds.Contains(e.DepartmentId) && e.Status == EmployeeStatus.Active)
            .Select(e => new { e.Id, e.DepartmentId })
            .ToListAsync(cancellationToken);

        var empIds = deptEmployees.Select(e => e.Id).ToList();
        var empDeptMap = deptEmployees.ToDictionary(e => e.Id, e => e.DepartmentId);

        var evaluations = await db.PerformanceEvaluations
            .AsNoTracking()
            .Where(pe => periodIds.Contains(pe.EvaluationPeriodId) && empIds.Contains(pe.EmployeeId))
            .Select(pe => new { pe.EvaluationPeriodId, pe.EmployeeId, pe.Status })
            .ToListAsync(cancellationToken);

        var evalsByPeriod = evaluations
            .GroupBy(pe => pe.EvaluationPeriodId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var resultList = new List<BranchEvaluationPeriodSummaryDto>();

        foreach (var p in periods)
        {
            evalsByPeriod.TryGetValue(p.Id, out var periodEvals);
            periodEvals ??= [];

            var evalsByDept = periodEvals
                .GroupBy(e => empDeptMap[e.EmployeeId])
                .ToDictionary(g => g.Key, g => g.ToList());

            var submittedDepts = 0;
            var approvedDepts = 0;
            foreach (var dept in departments)
            {
                if (!evalsByDept.TryGetValue(dept.Id, out var dEvals) || dEvals.Count == 0)
                {
                    continue;
                }

                if (dEvals.Any(e => e.Status is PerformanceEvaluationStatus.InReview
                    or PerformanceEvaluationStatus.Approved or PerformanceEvaluationStatus.Final))
                {
                    submittedDepts++;
                }

                if (dEvals.All(e => e.Status is PerformanceEvaluationStatus.Approved or PerformanceEvaluationStatus.Final))
                {
                    approvedDepts++;
                }
            }

            var isLocked = p.Status is PeriodStatus.Locked or PeriodStatus.Closed;
            var hasDispute = periodEvals.Any(e => e.Status == PerformanceEvaluationStatus.Disputed);
            var readyToLock = !isLocked && !hasDispute && totalDepts > 0 && approvedDepts == totalDepts;

            string displayStatus;
            if (isLocked)
            {
                displayStatus = "LOCKED";
            }
            else if (readyToLock)
            {
                displayStatus = "PUBLISHED";
            }
            else if (submittedDepts > 0 || p.Status == PeriodStatus.InReview)
            {
                displayStatus = "IN_REVIEW";
            }
            else if (periodEvals.Count > 0)
            {
                displayStatus = "EVALUATING";
            }
            else
            {
                displayStatus = "IN_PROGRESS";
            }

            if (displayStatusFilter is not null && displayStatus != displayStatusFilter)
            {
                continue;
            }

            resultList.Add(new BranchEvaluationPeriodSummaryDto(
                p.Id,
                p.Code,
                p.Name,
                p.StartDate,
                p.EndDate,
                p.PeriodType.ToEnumString(),
                p.Status.ToEnumString(),
                displayStatus,
                QualityRatio: null,
                submittedDepts,
                approvedDepts,
                totalDepts,
                AppealWindow: null,
                CanOpen: displayStatus == "IN_REVIEW",
                CanLock: readyToLock,
                CanViewHistory: isLocked));
        }

        return new BranchEvaluationPeriodsOverviewDto(branch.Name, branch.Code, availableYears, resultList);
    }

    /// <summary>
    /// Saves evaluations together with their KPI detail rows. A request that lost a race with another one (the same
    /// evaluation or KPI row inserted twice, or a row changed or removed meanwhile) is a 409, not a server error.
    /// </summary>
    private async Task SaveEvaluationChangesAsync(string conflictMessage, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(conflictMessage);
        }
        catch (DbUpdateException ex) when (PostgresConflict.IsUniqueViolation(ex))
        {
            throw new ConflictException(conflictMessage);
        }
    }

    /// <summary>
    /// The calibration roster shows the score stored with an evaluation that has one and is not a draft: a stored score is
    /// never replaced by a recalculation. A draft, or an employee without an evaluation, is scored by the calculator.
    /// </summary>
    private static bool KeepsStoredScore(PerformanceEvaluation? evaluation) =>
        evaluation?.TotalScore is not null && evaluation.Status != PerformanceEvaluationStatus.Draft;

    /// <summary>
    /// The scores shown in the calibration roster. A stored score is used as it is, for every column. Otherwise they come
    /// from the calculator; a score it cannot determine (no profile, or a profile without a root group) is null (N/A, never a default).
    /// </summary>
    private (decimal? BusinessScore, decimal? ServiceQualityScore, decimal? OperationsScore, decimal? QualitativeScore, decimal? TotalScore)
        ComputeEmployeeScores(PerformanceEvaluation? pe, PerformanceScoreResult? scored)
    {
        if (KeepsStoredScore(pe))
        {
            var qualScores = ParseStoredComment(pe!.Comment, logger).QualitativeScores;
            decimal? qualSum = qualScores.Count > 0 ? qualScores.Values.Average(q => q.Score) : null;
            decimal total = pe.TotalScore!.Value;
            return (total, total, total, qualSum, total);
        }

        if (scored is null)
        {
            return (null, null, null, null, null);
        }

        var (businessScore, serviceQualityScore, operationsScore) = scored.ComponentScores();
        var qualitativeScores = scored.Groups
            .Where(group => group.IsQualitative && group.Score.HasValue)
            .Select(group => group.Score!.Value)
            .ToList();
        decimal? qualitativeScore = qualitativeScores.Count > 0 ? qualitativeScores.Average() : null;

        return (businessScore, serviceQualityScore, operationsScore, qualitativeScore, scored.TotalScore);
    }

    private static GaussianDistributionDto ComputeGaussianBellCurve(List<CalibrationEmployeeRowDto> roster)
    {
        // Only employees with a score take part in the distribution.
        var scores = roster.Where(r => r.TotalScore.HasValue).Select(r => r.TotalScore!.Value).ToList();
        var count = scores.Count;
        var mean = count > 0 ? Math.Round(scores.Average(), 2) : 50m;

        decimal stdDev = 15m;
        if (count > 1)
        {
            var variance = (double)scores.Average(s => (s - mean) * (s - mean));
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
        // Only an employee with a tier counts toward the quota base; one without a score has no tier.
        var count = roster.Count(r => r.CalibratedTier is not null);
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
                .ThenInclude(epi => epi.KpiDefinition)
                    .ThenInclude(kd => kd!.KpiGroup)
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
        if (!await IsDepartmentInScopeAsync(actorAccountId, action, reviewer, departmentId, cancellationToken))
        {
            throw new ForbiddenException("Bạn không có quyền thao tác đánh giá trên phòng ban/nhân sự này.");
        }
    }

    private async Task<bool> IsDepartmentInScopeAsync(
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
            return true;
        }

        var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), cancellationToken);
        var covered = AccessScope.DepartmentsInScope(level, reviewer.DepartmentId, hierarchy);
        return covered is not null && covered.Contains(departmentId);
    }

    /// <summary>
    /// Departments the caller may submit evaluations for: null for every department (All), otherwise the Dept or
    /// Branch subtree, and an empty set when evaluation:submit is absent or only Own.
    /// </summary>
    private async Task<HashSet<Guid>?> DepartmentsInSubmitScopeAsync(
        Guid actorAccountId,
        Employee reviewer,
        DepartmentHierarchy hierarchy,
        CancellationToken cancellationToken)
    {
        var granted = await permissions.GetEffectivePermissionsAsync(actorAccountId, cancellationToken);
        var level = AccessScope.LevelFor(granted, "evaluation", "submit");
        return AccessScope.DepartmentsInScope(level, reviewer.DepartmentId, hierarchy);
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

    /// <summary>The penalty of one risk event: the points QA entered, otherwise the amount of its severity.</summary>
    internal static decimal RiskPenalty(RiskEvent riskEvent) => riskEvent.PenaltyPoints ?? MapRiskPenalty(riskEvent.Severity);

    internal static decimal MapRiskPenalty(RiskEventSeverity severity) => severity switch
    {
        RiskEventSeverity.Critical => 20m,
        RiskEventSeverity.High => 10m,
        RiskEventSeverity.Medium => 5m,
        RiskEventSeverity.Low => 2m,
        _ => 0m,
    };

    /// <summary>Reads the tier thresholds (best first) and the downgrade setting. Missing configuration is an error, never a guess.</summary>
    private async Task<DowngradeConfiguration> LoadDowngradeConfigurationAsync(CancellationToken cancellationToken)
    {
        var tiers = await db.RankingTierThresholds
            .AsNoTracking()
            .OrderBy(t => t.RankOrder)
            .ToListAsync(cancellationToken);
        var downgrade = await db.Downgrades
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (tiers.Count == 0 || downgrade is null)
        {
            throw new InvalidOperationException(
                "Chưa cấu hình xếp loại (ranking_tier_threshold, downgrade). Hãy chạy DbMigrator.");
        }

        return new DowngradeConfiguration(tiers, downgrade.TriggerSeverity, downgrade.CapTierCode);
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

    /// <summary>
    /// Content check of the free-text fields of the evaluation form, run before anything is read or saved. Their length is
    /// limited by the validation attributes of <see cref="SubmitEvaluationRequestDto"/>.
    /// </summary>
    private static void ValidateSubmitText(SubmitEvaluationRequestDto request)
    {
        EnsureNoMarkerSyntax(request.Comment, "Comment");
        EnsureNoMarkerSyntax(request.CoachingPlan, "CoachingPlan");
    }

    /// <summary>
    /// Qualitative scores keyed by the root group they score. Rejects an empty id (a client still sending the old
    /// catalog group id), a duplicate id, an id that is not a root group of the profile, and a group that owns an active
    /// KPI (its score comes from the KPIs, a reviewer's score never overrides it).
    /// </summary>
    private static Dictionary<Guid, QualitativeScoreInputDto> BuildQualitativeInputs(
        IEnumerable<QualitativeScoreInputDto>? inputs,
        IReadOnlyList<ProfileNode> roots)
    {
        var rootsById = roots.ToDictionary(root => root.Item.Id);
        var result = new Dictionary<Guid, QualitativeScoreInputDto>();

        foreach (var input in inputs ?? [])
        {
            if (input.EvaluationProfileItemId == Guid.Empty)
            {
                throw new ValidationException("Điểm định tính thiếu evaluationProfileItemId (mã nhóm gốc của hồ sơ đánh giá).");
            }

            if (!rootsById.TryGetValue(input.EvaluationProfileItemId, out var root))
            {
                throw new ValidationException(
                    $"evaluationProfileItemId {input.EvaluationProfileItemId} không phải nhóm gốc của hồ sơ đánh giá.");
            }

            if (!EvaluationProfileTree.IsQualitative(root))
            {
                throw new ValidationException(
                    $"Nhóm \"{EvaluationProfileTree.NodeName(root)}\" có KPI đang hoạt động nên điểm của nhóm lấy từ KPI, không nhận điểm định tính.");
            }

            if (!result.TryAdd(input.EvaluationProfileItemId, input))
            {
                throw new ValidationException(
                    $"Điểm định tính bị trùng cho evaluationProfileItemId {input.EvaluationProfileItemId}.");
            }
        }

        return result;
    }

    // ----- Stored comment: the reviewer's text and the system markers share one column ----------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [GeneratedRegex(@"<!--\s*COACHING_PLAN:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex CoachingPlanMarkerRegex();

    [GeneratedRegex(@"<!--\s*QUALITATIVE_JSON:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex QualitativeMarkerRegex();

    [GeneratedRegex(@"<!--\s*GENERAL_QUALITATIVE_SCORE:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex GeneralQualitativeScoreMarkerRegex();

    [GeneratedRegex(@"<!--\s*CALIBRATION_JSON:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex CalibrationMarkerRegex();

    [GeneratedRegex(@"<!--\s*RECALIBRATION_REASON:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex RecalibrationReasonMarkerRegex();

    [GeneratedRegex(@"<!--\s*APPROVAL_COMMENTS:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex ApprovalCommentsMarkerRegex();

    [GeneratedRegex(@"<!--\s*ACKNOWLEDGED_AT:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex AcknowledgedAtMarkerRegex();

    internal sealed record InternalCalibrationData(
        string CalibratedTier,
        string? Reason,
        bool CoachingFlag);

    internal sealed record StoredComment(
        string? Comment,
        string? CoachingPlan,
        Dictionary<Guid, QualitativeScoreInputDto> QualitativeScores,
        InternalCalibrationData? CalibrationData,
        string? RecalibrationReason,
        string? ApprovalComments,
        decimal? GeneralQualitativeScore = null)
    {
        public DateTimeOffset? AcknowledgedAt { get; init; }
    }

    private static readonly Action<ILogger, Guid, Exception?> LogQualitativeEntryIgnored =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(5, "QualitativeEntryIgnored"),
            "Stored qualitative score ignored (empty or duplicate evaluation profile item id {EvaluationProfileItemId})");

    private static readonly Action<ILogger, Exception?> LogQualitativeJsonUnreadable =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(6, "QualitativeJsonUnreadable"),
            "Stored qualitative scores could not be read and were ignored");

    internal static string? BuildStoredComment(
        string? comment,
        string? coachingPlan,
        List<QualitativeScoreInputDto>? qualitativeScores,
        InternalCalibrationData? calibrationData = null,
        string? recalibrationReason = null,
        string? approvalComments = null,
        decimal? generalQualitativeScore = null)
    {
        var trimmedComment = comment?.Trim();
        var markers = new List<string>();

        if (!string.IsNullOrWhiteSpace(coachingPlan))
        {
            markers.Add($"<!-- COACHING_PLAN: {coachingPlan.Trim()} -->");
        }

        if (generalQualitativeScore.HasValue)
        {
            markers.Add($"<!-- GENERAL_QUALITATIVE_SCORE: {generalQualitativeScore.Value.ToString(CultureInfo.InvariantCulture)} -->");
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

    /// <summary>
    /// Replaces (or removes, when <paramref name="value"/> is blank) a single marker and returns every other part of
    /// the stored comment untouched. Unlike a parse/rebuild round trip, it keeps stored data the reader skips
    /// (legacy or malformed qualitative entries), so a write that does not own a marker never discards it.
    /// </summary>
    internal static string? SetStoredMarker(string? storedComment, Regex markerRegex, string markerName, string? value)
    {
        var working = storedComment ?? string.Empty;

        foreach (var match in markerRegex.Matches(working).Reverse())
        {
            var start = match.Index > 0 && working[match.Index - 1] == '\n' ? match.Index - 1 : match.Index;
            working = working.Remove(start, match.Index + match.Length - start);
        }

        working = working.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            return working.Length == 0 ? null : working;
        }

        var marker = $"<!-- {markerName}: {value.Trim()} -->";
        if (working.Length == 0)
        {
            return marker;
        }

        return working.EndsWith("-->", StringComparison.Ordinal)
            ? $"{working}\n{marker}"
            : $"{working}\n\n{marker}";
    }

    /// <summary>
    /// Free text is stored in the same column as the system markers, so text that contains the marker delimiters is
    /// refused: a reviewer's words can never be read back as a calibration tier, reason or approval comment.
    /// </summary>
    internal static void EnsureNoMarkerSyntax(string? text, string fieldName)
    {
        if (text is not null && (text.Contains("<!--", StringComparison.Ordinal) || text.Contains("-->", StringComparison.Ordinal)))
        {
            throw new ValidationException($"{fieldName} không được chứa ký tự \"<!--\" hoặc \"-->\".");
        }
    }

    internal static StoredComment ParseStoredComment(string? storedComment, ILogger logger)
    {
        // Qualitative scores saved before the profile became a tree are keyed by the catalog group id, which the
        // tree no longer knows; they deserialize to an empty id and are skipped here (and logged), never scored.
        // Skipping is read-only: writers other than the form save must use SetStoredMarker, not a parse/rebuild.
        if (string.IsNullOrWhiteSpace(storedComment))
        {
            return new StoredComment(null, null, new(), null, null, null);
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
                foreach (var item in items)
                {
                    if (item.EvaluationProfileItemId == Guid.Empty || !qualDict.TryAdd(item.EvaluationProfileItemId, item))
                    {
                        LogQualitativeEntryIgnored(logger, item.EvaluationProfileItemId, null);
                    }
                }
            }
            catch (JsonException ex)
            {
                LogQualitativeJsonUnreadable(logger, ex);
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

        decimal? generalQualitativeScore = null;
        var generalMatch = GeneralQualitativeScoreMarkerRegex().Match(working);
        if (generalMatch.Success)
        {
            if (decimal.TryParse(generalMatch.Groups[1].Value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                generalQualitativeScore = parsed;
            }

            working = working.Replace(generalMatch.Value, "");
        }

        DateTimeOffset? acknowledgedAt = null;
        var ackMatch = AcknowledgedAtMarkerRegex().Match(working);
        if (ackMatch.Success)
        {
            if (DateTimeOffset.TryParse(ackMatch.Groups[1].Value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedAck))
            {
                acknowledgedAt = parsedAck;
            }

            working = working.Replace(ackMatch.Value, "");
        }

        var clean = working.Trim();
        return new StoredComment(
            string.IsNullOrEmpty(clean) ? null : clean,
            coachingPlan,
            qualDict,
            calibData,
            recalibrationReason,
            approvalComments,
            generalQualitativeScore)
        {
            AcknowledgedAt = acknowledgedAt
        };
    }

    public async Task<PersonalEvaluationViewDto> GetPersonalEvaluationAsync(
        Guid? periodId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var userEmployee = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        var employee = await db.Employees
            .AsNoTracking()
            .Include(e => e.Department)
            .Include(e => e.JobPosition)
            .FirstOrDefaultAsync(e => e.Id == userEmployee.Id, cancellationToken)
            ?? throw new NotFoundException("Nhân viên", userEmployee.Id);

        EvaluationPeriod? period = null;
        if (periodId.HasValue)
        {
            period = await db.EvaluationPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == periodId.Value, cancellationToken)
                ?? throw new NotFoundException("Kỳ đánh giá", periodId.Value);
        }
        else
        {
            var latestEvalPeriodId = await db.PerformanceEvaluations
                .AsNoTracking()
                .Where(pe => pe.EmployeeId == employee.Id)
                .OrderByDescending(pe => pe.EvaluationPeriod.StartDate)
                .Select(pe => (Guid?)pe.EvaluationPeriodId)
                .FirstOrDefaultAsync(cancellationToken);

            if (latestEvalPeriodId.HasValue)
            {
                period = await db.EvaluationPeriods
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == latestEvalPeriodId.Value, cancellationToken);
            }

            period ??= await db.EvaluationPeriods
                .AsNoTracking()
                .Where(p => p.PeriodType != PeriodType.Day)
                .OrderByDescending(p => p.StartDate)
                .FirstOrDefaultAsync(cancellationToken);

            if (period is null)
            {
                throw new NotFoundException("Kỳ đánh giá", Guid.Empty);
            }
        }

        var profile = await FindProfileAsync(employee.JobPositionId, period, cancellationToken);

        var existingEvaluation = await db.PerformanceEvaluations
            .AsNoTracking()
            .FirstOrDefaultAsync(pe => pe.EmployeeId == employee.Id && pe.EvaluationPeriodId == period.Id, cancellationToken);

        var stored = ParseStoredComment(existingEvaluation?.Comment, logger);

        PerformanceScoreResult? scored = null;
        if (profile is not null)
        {
            var subjects = new[]
            {
                new PerformanceScoreSubject(employee.Id, employee.DepartmentId, profile, stored.QualitativeScores)
            };
            var scoredMap = await calculator.CalculateAsync(subjects, period, cancellationToken);
            scoredMap.TryGetValue(employee.Id, out scored);
        }

        var keepsStoredScore = existingEvaluation is not null
            && existingEvaluation.Status is not (PerformanceEvaluationStatus.Draft or PerformanceEvaluationStatus.Calculated);
        var totalPeScore = keepsStoredScore ? existingEvaluation!.TotalScore : scored?.TotalScore;

        var downgradeConfig = await LoadDowngradeConfigurationAsync(cancellationToken);
        var proposedTier = totalPeScore.HasValue && scored is not null
            ? TierDeterminer.DetermineTier(totalPeScore.Value, scored.RiskEvents, downgradeConfig)
            : null;

        var tier = stored.CalibrationData?.CalibratedTier ?? proposedTier;
        var allTiers = downgradeConfig.Tiers.Select(t => t.TierCode).ToList();

        // Anonymous Peer Ranking (BR-36, NFR-07)
        var peerEmployees = await db.Employees
            .AsNoTracking()
            .Where(e => e.DepartmentId == employee.DepartmentId
                && e.JobPositionId == employee.JobPositionId
                && e.Status == EmployeeStatus.Active)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        var peerEvaluations = await db.PerformanceEvaluations
            .AsNoTracking()
            .Where(pe => pe.EvaluationPeriodId == period.Id && peerEmployees.Contains(pe.EmployeeId))
            .Select(pe => new { pe.EmployeeId, pe.TotalScore })
            .ToListAsync(cancellationToken);

        var peerScoreMap = new Dictionary<Guid, decimal>(peerEmployees.Count);
        foreach (var pe in peerEvaluations)
        {
            peerScoreMap[pe.EmployeeId] = pe.TotalScore ?? 0m;
        }
        if (totalPeScore.HasValue)
        {
            peerScoreMap[employee.Id] = totalPeScore.Value;
        }

        var totalPeers = Math.Max(1, peerEmployees.Count);
        var myScore = peerScoreMap.TryGetValue(employee.Id, out var resolvedScore) ? resolvedScore : 0m;
        var higherScoreCount = peerEmployees
            .Where(empId => empId != employee.Id)
            .Count(empId => (peerScoreMap.TryGetValue(empId, out var peerScore) ? peerScore : 0m) > myScore);
        var rank = higherScoreCount + 1;
        var percentile = (int)Math.Max(1, Math.Ceiling((decimal)rank / totalPeers * 100m));

        var rankingPercentile = $"Top {percentile}%";
        var rankingRoleTitle = employee.JobPosition?.Title ?? "Nhân viên";

        // Previous period comparison
        var previousPeriod = await db.EvaluationPeriods
            .AsNoTracking()
            .Where(p => p.PeriodType == period.PeriodType && p.StartDate < period.StartDate)
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        decimal? previousPeriodScore = null;
        string? previousPeriodName = null;
        decimal? scoreDelta = null;

        if (previousPeriod is not null)
        {
            var prevPe = await db.PerformanceEvaluations
                .AsNoTracking()
                .FirstOrDefaultAsync(pe => pe.EmployeeId == employee.Id && pe.EvaluationPeriodId == previousPeriod.Id, cancellationToken);
            if (prevPe?.TotalScore.HasValue == true)
            {
                previousPeriodScore = prevPe.TotalScore.Value;
                previousPeriodName = previousPeriod.Name;
                if (totalPeScore.HasValue)
                {
                    scoreDelta = Math.Round(totalPeScore.Value - previousPeriodScore.Value, 1);
                }
            }
        }

        // Criteria mapping
        var criteria = new List<PersonalEvaluationCriteriaItemDto>();
        if (scored is not null)
        {
            foreach (var group in scored.Groups)
            {
                if (group.Kpis.Count == 0)
                {
                    criteria.Add(new PersonalEvaluationCriteriaItemDto(
                        group.EvaluationProfileItemId,
                        group.Name,
                        group.WeightPercent,
                        "—",
                        group.QualitativeScore ?? group.Score,
                        group.Score.HasValue ? Math.Round(group.Score.Value * group.WeightPercent / 100m, 1) : null,
                        HasDrilldown: false,
                        KpiDefinitionId: null,
                        TargetDisplay: "—",
                        ActualDisplay: "—"));
                }
                else
                {
                    foreach (var kpi in group.Kpis)
                    {
                        var effectiveWeight = group.Kpis.Count == 1
                            ? group.WeightPercent
                            : Math.Round(group.WeightPercent * kpi.SharePercent / 100m, 1);

                        var displayedTarget = kpi.TargetValue is > 0m ? kpi.TargetValue : null;
                        var displayedActual = kpi.ResolvedActualValue ?? kpi.ActualValue;
                        var targetDisplay = displayedTarget.HasValue
                            ? FormatMetricDisplay(displayedTarget.Value, kpi.Unit)
                            : "—";
                        var actualDisplay = displayedActual.HasValue
                            ? FormatMetricDisplay(displayedActual.Value, kpi.Unit)
                            : "—";
                        var actualTargetStr = displayedTarget.HasValue || displayedActual.HasValue
                            ? $"{(displayedActual.HasValue ? FormatMetricValue(displayedActual.Value) : "—")} / {(displayedTarget.HasValue ? FormatMetricValue(displayedTarget.Value) : "—")} {kpi.Unit}".Trim()
                            : "—";

                        var kpiScore = kpi.ComponentScore;
                        var weightedScore = kpiScore.HasValue
                            ? Math.Round(kpiScore.Value * effectiveWeight / 100m, 1)
                            : (decimal?)null;

                        criteria.Add(new PersonalEvaluationCriteriaItemDto(
                            kpi.KpiDefinitionId,
                            kpi.Name,
                            effectiveWeight,
                            actualTargetStr,
                            kpiScore,
                            weightedScore,
                            HasDrilldown: kpi.ActualValue.HasValue,
                            KpiDefinitionId: kpi.KpiDefinitionId,
                            TargetDisplay: targetDisplay,
                            ActualDisplay: actualDisplay));
                    }
                }
            }
        }

        // Appeal deadline & status
        var now = DateTimeOffset.UtcNow;
        var deadlineDate = period.EndDate.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);
        var remainingDays = Math.Max(0, (int)Math.Ceiling((deadlineDate - now.UtcDateTime).TotalDays));
        var evalStatus = (existingEvaluation?.Status ?? PerformanceEvaluationStatus.Draft).ToString().ToUpperInvariant();
        var canAppeal = remainingDays > 0 && existingEvaluation?.Status is PerformanceEvaluationStatus.Approved or PerformanceEvaluationStatus.Final;
        var appealDeadlineText = remainingDays > 0
            ? $"23:59 {period.EndDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}"
            : "—";

        var isAcknowledged = stored.AcknowledgedAt.HasValue;
        var managerFeedback = stored.Comment;
        var riskPenalty = scored?.RiskPenalty ?? 0m;

        return new PersonalEvaluationViewDto(
            PeriodId: period.Id,
            PeriodName: period.Name,
            PeriodCode: period.Code,
            Status: evalStatus,
            AppealDeadlineText: appealDeadlineText,
            AppealRemainingDays: remainingDays,
            CanAppeal: canAppeal,
            TotalPeScore: totalPeScore,
            ScoreDelta: scoreDelta,
            PreviousPeriodName: previousPeriodName,
            PreviousPeriodScore: previousPeriodScore,
            Tier: tier,
            AllTiers: allTiers,
            RankingPercentile: rankingPercentile,
            RankingPosition: rank,
            RankingTotalEmployees: totalPeers,
            RankingRoleTitle: rankingRoleTitle,
            Criteria: criteria,
            QualityRatio: null,
            RiskPenalty: riskPenalty,
            ManagerFeedback: managerFeedback,
            IsAcknowledged: isAcknowledged,
            AcknowledgedAt: stored.AcknowledgedAt);
    }

    public async Task<AcknowledgePersonalEvaluationResultDto> AcknowledgePersonalEvaluationAsync(
        AcknowledgePersonalEvaluationRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var employee = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        var pe = await db.PerformanceEvaluations
            .FirstOrDefaultAsync(p => p.EmployeeId == employee.Id && p.EvaluationPeriodId == request.PeriodId, cancellationToken)
            ?? throw new NotFoundException("Kết quả đánh giá trong kỳ này", request.PeriodId);

        var stored = ParseStoredComment(pe.Comment, logger);
        if (stored.AcknowledgedAt.HasValue)
        {
            return new AcknowledgePersonalEvaluationResultDto(
                Success: true,
                AcknowledgedAt: stored.AcknowledgedAt.Value,
                Message: "Bạn đã ghi nhận kết quả đánh giá này rồi.");
        }

        var now = DateTimeOffset.UtcNow;
        pe.Comment = SetStoredMarker(pe.Comment, AcknowledgedAtMarkerRegex(), "ACKNOWLEDGED_AT", now.ToString("O"));
        pe.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return new AcknowledgePersonalEvaluationResultDto(
            Success: true,
            AcknowledgedAt: now,
            Message: "Ghi nhận kết quả đánh giá thành công.");
    }

    public async Task<IReadOnlyList<PersonalCriteriaLedgerItemDto>> GetPersonalCriteriaLedgerAsync(
        Guid periodId,
        Guid criteriaId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var employee = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        var period = await db.EvaluationPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", periodId);

        var actualProgresses = await db.ActualProgresses
            .AsNoTracking()
            .Where(a => a.TargetAssignment.EmployeeId == employee.Id
                && (a.TargetAssignment.KpiDefinitionId == criteriaId || a.TargetAssignmentId == criteriaId)
                && a.Status == ActualProgressStatus.Valid
                && a.ProgressDate >= period.StartDate
                && a.ProgressDate <= period.EndDate)
            .OrderByDescending(a => a.ProgressDate)
            .ThenBy(a => a.Id)
            .Include(a => a.WorkResultItems)
                .ThenInclude(wri => wri.VerifiedWorkResult)
                    .ThenInclude(vwr => vwr!.WorkTransaction)
                        .ThenInclude(wt => wt!.ImportedTransaction)
            .Include(a => a.WorkResultItems)
                .ThenInclude(wri => wri.VerifiedWorkResult)
                    .ThenInclude(vwr => vwr!.WorkTransaction)
                        .ThenInclude(wt => wt!.Work)
            .Include(a => a.WorkResultItems)
                .ThenInclude(wri => wri.VerifiedWorkResult)
                    .ThenInclude(vwr => vwr!.Verification)
                        .ThenInclude(v => v!.ResultSubmission)
                            .ThenInclude(rs => rs.Work)
            .ToListAsync(cancellationToken);

        var items = new List<PersonalCriteriaLedgerItemDto>();

        foreach (var ap in actualProgresses)
        {
            if (ap.WorkResultItems.Count > 0)
            {
                foreach (var wri in ap.WorkResultItems)
                {
                    var wt = wri.VerifiedWorkResult?.WorkTransaction;
                    var it = wt?.ImportedTransaction;
                    var work = wt?.Work ?? wri.VerifiedWorkResult?.Verification?.ResultSubmission.Work;

                    var txCode = wt?.TransactionCode
                        ?? $"VR-{wri.VerifiedWorkResultId.ToString()[..8].ToUpperInvariant()}";

                    var occurredOn = it?.TransactionDate
                        ?? (wri.VerifiedWorkResult is { } vwr ? DateOnly.FromDateTime(vwr.VerifiedAt.UtcDateTime) : ap.ProgressDate);

                    var amount = it is not null
                        ? $"{it.Amount.ToString("N0", CultureInfo.InvariantCulture)} VND"
                        : wri.Value.ToString("N0", CultureInfo.InvariantCulture);

                    items.Add(new PersonalCriteriaLedgerItemDto(
                        Id: wri.Id,
                        TxCode: txCode,
                        Date: occurredOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                        Amount: amount,
                        KpiContribution: wri.Value,
                        Status: wt?.Status ?? "VALID",
                        OccurredOn: occurredOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        WorkId: work?.Id,
                        WorkCode: work?.Code,
                        WorkTitle: work?.Title));
                }
            }
            else
            {
                items.Add(new PersonalCriteriaLedgerItemDto(
                    Id: ap.Id,
                    TxCode: $"AP-{ap.Id.ToString()[..8].ToUpperInvariant()}",
                    Date: ap.ProgressDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    Amount: ap.Value.ToString("N0", CultureInfo.InvariantCulture),
                    KpiContribution: ap.Value,
                    Status: "VALID",
                    OccurredOn: ap.ProgressDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }
        }

        return items;
    }

    private static string FormatMetricValue(decimal value) =>
        Math.Round(value, 2, MidpointRounding.ToEven).ToString("#,##0.##", CultureInfo.InvariantCulture);

    private static string FormatMetricDisplay(decimal value, string? unit) =>
        string.IsNullOrWhiteSpace(unit)
            ? FormatMetricValue(value)
            : $"{FormatMetricValue(value)} {unit.Trim()}";
}
