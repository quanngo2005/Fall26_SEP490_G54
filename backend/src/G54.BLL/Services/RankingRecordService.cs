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

namespace G54.BLL.Services;

/// <summary>
/// Ranking records for HR (UC110, UC111): the final evaluations of a locked period, ranked inside their department and
/// job position. Rank and tier are computed over the whole group first and filtered afterwards, so a filter never changes them.
/// </summary>
public sealed class RankingRecordService(AppDbContext db, ILogger<RankingRecordService> logger) : IRankingRecordService
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

    /// <summary>Every final, scored evaluation of the period with its rank and tier, plus the configured tier codes.</summary>
    private async Task<(List<RankedRow> Records, IReadOnlyList<string> TierCodes)> BuildRecordsAsync(
        EvaluationPeriod period,
        CancellationToken cancellationToken)
    {
        var configuration = await RankingInputs.LoadDowngradeConfigurationAsync(db, cancellationToken);

        var evaluations = await db.PerformanceEvaluations
            .AsNoTracking()
            .Include(pe => pe.Employee).ThenInclude(e => e.Department)
            .Include(pe => pe.Employee).ThenInclude(e => e.JobPosition)
            .Where(pe => pe.EvaluationPeriodId == period.Id
                && pe.Status == PerformanceEvaluationStatus.Final
                && pe.TotalScore != null)
            .ToListAsync(cancellationToken);

        var risksByEmployee = await RankingInputs.LoadRiskEventsByEmployeeAsync(db, evaluations.Select(pe => pe.EmployeeId).ToList(), period, cancellationToken);

        var rows = new List<RankedRow>(evaluations.Count);
        foreach (var group in evaluations.GroupBy(pe => (pe.Employee.DepartmentId, pe.Employee.JobPositionId)))
        {
            var ordered = group
                .OrderByDescending(pe => pe.TotalScore)
                .ThenBy(pe => pe.Employee.EmployeeCode, StringComparer.Ordinal)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                rows.Add(ToRow(ordered[i], i + 1, ordered.Count, risksByEmployee, configuration));
            }
        }

        return (rows, configuration.Tiers.Select(t => t.TierCode).ToList());
    }

    private RankedRow ToRow(
        PerformanceEvaluation evaluation,
        int rank,
        int groupSize,
        Dictionary<Guid, List<RiskEvent>> risksByEmployee,
        DowngradeConfiguration configuration)
    {
        var stored = EvaluationService.ParseStoredComment(evaluation.Comment, logger);
        var score = evaluation.TotalScore!.Value;
        var proposedTier = TierDeterminer.DetermineTier(score, risksByEmployee.GetValueOrDefault(evaluation.EmployeeId), configuration);
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
                score,
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
