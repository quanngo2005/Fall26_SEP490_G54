using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Common.Workflow;
using G54.BLL.Dtos.Evaluation;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

/// <summary>
/// Ranking records for HR (UC110, UC111): the final evaluations of a locked period, ranked inside their department and
/// job position. Rank and tier are computed over the whole group first and filtered afterwards, so a filter never changes them.
/// </summary>
public sealed class RankingRecordService(
    AppDbContext db,
    IRankingPolicyService rankingPolicies,
    ILogger<RankingRecordService> logger,
    IOptions<SystemParametersOptions>? parameters = null) : IRankingRecordService
{
    private const int MaxPageSize = 100;

    public async Task<PagedResult<RankingRecordDto>> GetRecordsAsync(
        RankingRecordFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var periodId = filter.PeriodId is { } id && id != Guid.Empty
            ? id
            : throw new ValidationException("Tham số 'periodId' là bắt buộc.");
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        var period = await LoadLockedPeriodAsync(periodId, cancellationToken);
        var (records, tiers) = await BuildRecordsAsync(period, cancellationToken);

        var tier = ParseTier(filter.Tier, tiers);
        var departments = await ResolveDepartmentsAsync(filter.DepartmentId, cancellationToken);

        var query = records.Select(r => r.Record);
        if (departments is not null)
        {
            query = query.Where(r => departments.Contains(r.DepartmentId));
        }

        if (filter.JobPositionId is { } jobPositionId)
        {
            query = query.Where(r => r.JobPositionId == jobPositionId);
        }

        if (tier is not null)
        {
            query = query.Where(r => r.Tier == tier);
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim();
            query = query.Where(r => r.EmployeeCode.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || r.FullName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        var matched = query
            .OrderBy(r => r.DepartmentName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.JobPositionTitle, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Rank)
            .ThenBy(r => r.EmployeeCode, StringComparer.Ordinal)
            .ToList();

        // Computed as long: a huge page number would overflow int.
        var offset = (long)(page - 1) * pageSize;
        var items = offset >= matched.Count ? [] : matched.Skip((int)offset).Take(pageSize).ToList();
        return new PagedResult<RankingRecordDto>(items, matched.Count, page, pageSize);
    }

    public async Task<RankingRecordDetailDto> GetRecordAsync(Guid evaluationId, CancellationToken cancellationToken = default)
    {
        var periodId = await db.PerformanceEvaluations
            .AsNoTracking()
            .Where(pe => pe.Id == evaluationId)
            .Select(pe => (Guid?)pe.EvaluationPeriodId)
            .FirstOrDefaultAsync(cancellationToken);

        var period = periodId is { } id
            ? await db.EvaluationPeriods.AsNoTracking().FirstAsync(p => p.Id == id, cancellationToken)
            : null;

        // The same answer for an unknown id, a record that is not final and a period that is not locked: nothing to show.
        if (period is null || period.Status != PeriodStatus.Locked)
        {
            throw new NotFoundException("Bản ghi xếp hạng", evaluationId);
        }

        var (records, _) = await BuildRecordsAsync(period, cancellationToken);
        var row = records.FirstOrDefault(r => r.Record.EvaluationId == evaluationId)
            ?? throw new NotFoundException("Bản ghi xếp hạng", evaluationId);

        return new RankingRecordDetailDto(
            row.Record,
            period.Id,
            period.Code,
            period.Name,
            row.Stored.CalibrationData?.Reason,
            row.Stored.CoachingPlan,
            row.Stored.ApprovalComments,
            row.Stored.AcknowledgedAt);
    }

    private sealed record RankedRow(RankingRecordDto Record, EvaluationService.StoredComment Stored);

    private async Task<EvaluationPeriod> LoadLockedPeriodAsync(Guid periodId, CancellationToken cancellationToken)
    {
        var period = await db.EvaluationPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken)
            ?? throw new NotFoundException("Kỳ đánh giá", periodId);

        return period.Status == PeriodStatus.Locked
            ? period
            : throw new ConflictException(
                "Kỳ đánh giá chưa khóa nên chưa có bản ghi xếp hạng chính thức.",
                ErrorCodes.EvaluationPeriodInvalidState);
    }

    /// <summary>Every final, scored evaluation of the period with its rank and tier, plus the tier codes of the period's policy.</summary>
    private async Task<(List<RankedRow> Records, IReadOnlyList<string> TierCodes)> BuildRecordsAsync(
        EvaluationPeriod period,
        CancellationToken cancellationToken)
    {
        // A locked period is ranked by the policy frozen when it was locked, the same as the calibration screen.
        var policy = await rankingPolicies.LoadEffectiveDefinitionAsync(period, cancellationToken);
        var criteria = await EvaluationSettingsService.LoadCriteriaAsync(db, cancellationToken);

        var evaluations = await db.PerformanceEvaluations
            .AsNoTracking()
            .Include(pe => pe.Employee).ThenInclude(e => e.Department)
            .Include(pe => pe.Employee).ThenInclude(e => e.JobPosition)
            .Where(pe => pe.EvaluationPeriodId == period.Id
                && pe.Status == PerformanceEvaluationStatus.Final
                && pe.TotalScore != null)
            .ToListAsync(cancellationToken);

        var risksByEmployee = await RankingInputs.LoadRiskEventsByEmployeeAsync(db, evaluations.Select(pe => pe.EmployeeId).ToList(), period, cancellationToken);

        var stored = evaluations.ToDictionary(pe => pe.Id, pe => EvaluationService.ParseStoredComment(pe.Comment, logger));

        // The calibration screen evaluates the policy over a whole department, so its quotas and department average do too.
        var proposed = evaluations
            .GroupBy(pe => pe.Employee.DepartmentId)
            .SelectMany(department => RankingRuleEngine.EvaluateRoster(
                department.Select(pe => ToSubject(pe, stored[pe.Id], risksByEmployee)).ToList(),
                policy,
                criteria))
            .ToDictionary(entry => entry.Key, entry => entry.Value.ProposedTier!);

        var rows = new List<RankedRow>(evaluations.Count);
        foreach (var group in evaluations.GroupBy(pe => (pe.Employee.DepartmentId, pe.Employee.JobPositionId)))
        {
            var ordered = group
                .OrderByDescending(pe => pe.TotalScore)
                .ThenBy(pe => pe.Employee.EmployeeCode, StringComparer.Ordinal)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                var evaluation = ordered[i];
                rows.Add(ToRow(evaluation, stored[evaluation.Id], i + 1, ordered.Count, proposed[evaluation.EmployeeId]));
            }
        }

        return (rows, policy.Tiers.OrderBy(t => t.RankOrder).Select(t => t.TierCode).ToList());
    }

    /// <summary>A final evaluation keeps its stored score for every column, as the calibration roster shows it.</summary>
    private RankingSubjectInput ToSubject(
        PerformanceEvaluation evaluation,
        EvaluationService.StoredComment stored,
        Dictionary<Guid, List<RiskEvent>> risksByEmployee)
    {
        var score = evaluation.TotalScore!.Value;
        decimal? qualitative = stored.QualitativeScores.Count > 0 ? stored.QualitativeScores.Values.Average(q => q.Score) : null;
        var risks = risksByEmployee.GetValueOrDefault(evaluation.EmployeeId);

        return new RankingSubjectInput(
            evaluation.EmployeeId,
            evaluation.Employee.EmployeeCode,
            evaluation.Employee.JobPosition.Code,
            score,
            score,
            score,
            qualitative,
            score,
            score,
            score,
            risks?.Sum(r => EvaluationService.RiskPenalty(r, parameters?.Value)) ?? 0m,
            RiskEvents: risks);
    }

    private static RankedRow ToRow(
        PerformanceEvaluation evaluation,
        EvaluationService.StoredComment stored,
        int rank,
        int groupSize,
        string proposedTier)
    {
        var tier = stored.CalibrationData?.CalibratedTier ?? proposedTier;
        var employee = evaluation.Employee;

        return new RankedRow(
            new RankingRecordDto(
                evaluation.Id,
                employee.Id,
                employee.EmployeeCode,
                employee.FullName,
                employee.DepartmentId,
                employee.Department.Name,
                employee.JobPositionId,
                employee.JobPosition.Title,
                evaluation.TotalScore!.Value,
                rank,
                groupSize,
                proposedTier,
                tier,
                !string.Equals(tier, proposedTier, StringComparison.Ordinal),
                evaluation.ApprovedAt),
            stored);
    }
    private static string? ParseTier(string? value, IReadOnlyList<string> tierCodes)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return tierCodes.FirstOrDefault(code => string.Equals(code, value.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException($"Giá trị '{value}' không hợp lệ cho tham số 'tier'.");
    }

    /// <summary>The requested department with its sub-departments (null = every department).</summary>
    private async Task<HashSet<Guid>?> ResolveDepartmentsAsync(Guid? departmentId, CancellationToken cancellationToken)
    {
        if (departmentId is not { } id)
        {
            return null;
        }

        var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), cancellationToken);
        return hierarchy.Nodes.ContainsKey(id)
            ? hierarchy.Subtree(id).ToHashSet()
            : throw new NotFoundException("Phòng ban", id);
    }
}
