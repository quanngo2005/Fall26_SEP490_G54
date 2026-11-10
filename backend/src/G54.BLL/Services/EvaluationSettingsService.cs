using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Workflow;
using G54.BLL.Dtos.SystemParameter;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class EvaluationSettingsService(AppDbContext db) : IEvaluationSettingsService
{
    private const int MaxCriteria = 20;
    private const decimal MaxScore = 100m;
    private const decimal MaxRiskEvents = 1000m;

    /// <summary>The stored baseline, or "previous period" when none was saved yet. Read on every call, so a change applies at once.</summary>
    public static async Task<EvaluationComparisonDto> LoadComparisonAsync(AppDbContext db, CancellationToken ct)
    {
        var row = await db.EvaluationComparisonSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return row is null
            ? new EvaluationComparisonDto(EvaluationComparisonBaselines.PreviousPeriod, EvaluationComparisonBaselines.MinLookback)
            : new EvaluationComparisonDto(row.Baseline, row.LookbackPeriods);
    }

    /// <summary>The criteria with the tiers they are required for, in display order.</summary>
    public static async Task<IReadOnlyList<TierCriterionRule>> LoadCriteriaAsync(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.TierCriteria.AsNoTracking().Include(c => c.Requirements).OrderBy(c => c.SortOrder).ToListAsync(ct);
        return rows
            .Select(c => new TierCriterionRule(c.Code, c.Metric, c.Threshold, c.Requirements.Where(r => r.Required).Select(r => r.TierCode).ToList()))
            .ToList();
    }

    public Task<EvaluationComparisonDto> GetComparisonAsync(CancellationToken ct = default) => LoadComparisonAsync(db, ct);

    public async Task<EvaluationComparisonDto> UpdateComparisonAsync(EvaluationComparisonDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<string>();
        if (!EvaluationComparisonBaselines.All.Contains(request.Baseline))
        {
            errors.Add($"Mốc so sánh phải là một trong: {string.Join(", ", EvaluationComparisonBaselines.All)}.");
        }

        if (request.LookbackPeriods is < EvaluationComparisonBaselines.MinLookback or > EvaluationComparisonBaselines.MaxLookback)
        {
            errors.Add($"Số kỳ trung bình phải từ {EvaluationComparisonBaselines.MinLookback} đến {EvaluationComparisonBaselines.MaxLookback}.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var row = await db.EvaluationComparisonSettings.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            row = new EvaluationComparisonSetting();
            db.EvaluationComparisonSettings.Add(row);
        }

        row.Baseline = request.Baseline;
        row.LookbackPeriods = request.LookbackPeriods;
        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<TierCriteriaMatrixDto> GetTierCriteriaAsync(CancellationToken ct = default)
    {
        var tiers = await LoadTiersAsync(ct);
        var rows = await db.TierCriteria.AsNoTracking().Include(c => c.Requirements).OrderBy(c => c.SortOrder).ToListAsync(ct);
        return ToMatrix(tiers, rows);
    }

    public async Task<TierCriteriaMatrixDto> UpdateTierCriteriaAsync(UpdateTierCriteriaDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tiers = await LoadTiersAsync(ct);
        var tierCodes = tiers.Select(t => t.Code).ToHashSet(StringComparer.Ordinal);
        Validate(request.Criteria, tierCodes);

        var existing = await db.TierCriteria.Include(c => c.Requirements).ToListAsync(ct);
        var kept = request.Criteria.Select(c => c.Code.Trim()).ToHashSet(StringComparer.Ordinal);
        db.TierCriteria.RemoveRange(existing.Where(c => !kept.Contains(c.Code)));

        for (var i = 0; i < request.Criteria.Count; i++)
        {
            var dto = request.Criteria[i];
            var code = dto.Code.Trim();
            var row = existing.FirstOrDefault(c => c.Code == code);
            if (row is null)
            {
                row = new TierCriterion { Code = code };
                db.TierCriteria.Add(row);
            }

            row.Name = dto.Name.Trim();
            row.Metric = dto.Metric;
            row.Threshold = dto.Threshold;
            row.SortOrder = i;

            foreach (var tier in tierCodes)
            {
                var required = dto.Required.GetValueOrDefault(tier);
                var cell = row.Requirements.FirstOrDefault(r => r.TierCode == tier);
                if (cell is null)
                {
                    row.Requirements.Add(new TierCriterionRequirement { TierCode = tier, Required = required });
                }
                else
                {
                    cell.Required = required;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return await GetTierCriteriaAsync(ct);
    }

    private static void Validate(IReadOnlyList<TierCriterionDto>? criteria, HashSet<string> tierCodes)
    {
        if (criteria is null)
        {
            throw new ValidationException("Cần gửi danh sách tiêu chí.");
        }

        var errors = new List<string>();
        if (criteria.Count > MaxCriteria)
        {
            errors.Add($"Tối đa {MaxCriteria} tiêu chí.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in criteria)
        {
            var code = c?.Code?.Trim();
            if (c is null || string.IsNullOrEmpty(code) || code.Length > 50)
            {
                errors.Add("Mã tiêu chí là bắt buộc và tối đa 50 ký tự.");
                continue;
            }

            if (!seen.Add(code))
            {
                errors.Add($"Mã tiêu chí '{code}' bị trùng.");
            }

            if (string.IsNullOrWhiteSpace(c.Name) || c.Name.Trim().Length > 200)
            {
                errors.Add($"Tên tiêu chí '{code}' là bắt buộc và tối đa 200 ký tự.");
            }

            if (!TierCriterionMetrics.All.Contains(c.Metric))
            {
                errors.Add($"Chỉ số của tiêu chí '{code}' phải là một trong: {string.Join(", ", TierCriterionMetrics.All)}.");
            }
            else
            {
                var max = c.Metric == TierCriterionMetrics.OpenRiskEvents ? MaxRiskEvents : MaxScore;
                if (c.Threshold < 0 || c.Threshold > max)
                {
                    errors.Add($"Ngưỡng của tiêu chí '{code}' phải từ 0 đến {max:0}.");
                }
            }

            if (c.Required is null || c.Required.Keys.Any(k => !tierCodes.Contains(k)))
            {
                errors.Add($"Tiêu chí '{code}' có hạng không tồn tại trong cấu hình xếp hạng.");
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private async Task<List<TierDto>> LoadTiersAsync(CancellationToken ct)
        => await db.RankingTierThresholds.AsNoTracking()
            .OrderBy(t => t.RankOrder)
            .Select(t => new TierDto(t.TierCode, t.MinScore))
            .ToListAsync(ct);

    private static TierCriteriaMatrixDto ToMatrix(List<TierDto> tiers, List<TierCriterion> rows) => new(
        tiers,
        TierCriterionMetrics.All,
        rows.Select(c => new TierCriterionDto(
            c.Code,
            c.Name,
            c.Metric,
            c.Threshold,
            tiers.ToDictionary(t => t.Code, t => c.Requirements.Any(r => r.TierCode == t.Code && r.Required)))).ToList());
}
