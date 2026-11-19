using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Validation;
using G54.BLL.Dtos.TargetAllocation;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

/// <summary>Rounding direction for one suggested cell before the remainder pass closes the exact sum (D1/D8).</summary>
internal enum SuggestionRoundingMode
{
    Nearest,
    Up,
    Down,
}

/// <summary>How the rounding remainder is closed back onto the total (D1).</summary>
internal enum SuggestionRemainderMethod
{
    LargestRemainder,
    Buffer,
}

/// <summary>
/// Pure math for the suggestion engine: rounding to a step and closing the rounding remainder so a set of cells
/// always sums to exactly the parent total. No I/O, no DI — kept separate so it can be unit-tested directly
/// (docs/plans/TARGET_ALLOCATION_V3_DECISIONS.md, D1/D8). The no-history fallback weights are chosen by the caller.
/// </summary>
internal static class SuggestionMath
{
    /// <summary>No-history quarter shape for every tier: a progressive split Q1:Q2:Q3:Q4 = 1:2:3:4 (10/20/30/40%).</summary>
    public static readonly decimal[] ProgressiveQuarterWeights = [1m, 2m, 3m, 4m];

    /// <summary>The parent's own quarter shape when it has one, otherwise the progressive 1:2:3:4 shape.</summary>
    public static IReadOnlyList<decimal> QuarterShapeOrProgressive(IReadOnlyList<decimal> parentQuarters) =>
        parentQuarters.Any(v => v > 0) ? parentQuarters : ProgressiveQuarterWeights;

    /// <summary>Rounds <paramref name="value"/> to the nearest multiple of <paramref name="step"/> in the given direction. A non-positive step disables rounding (returns the value unchanged).</summary>
    public static decimal RoundToStep(decimal value, decimal step, SuggestionRoundingMode mode)
    {
        if (step <= 0)
        {
            return value;
        }

        var units = value / step;
        var roundedUnits = mode switch
        {
            SuggestionRoundingMode.Up => Math.Ceiling(units),
            SuggestionRoundingMode.Down => Math.Floor(units),
            _ => Math.Round(units, MidpointRounding.AwayFromZero),
        };

        return roundedUnits * step;
    }

    /// <summary>
    /// Splits <paramref name="total"/> across cells proportional to <paramref name="weights"/> (the basis shares,
    /// or the no-history fallback weights), rounds each cell to <paramref name="step"/>, and closes the rounding
    /// remainder by <paramref name="remainderMethod"/> so the returned amounts always sum to exactly
    /// <paramref name="total"/>. <paramref name="weights"/> must have a positive sum; the caller supplies the
    /// fallback weights when there is no history.
    /// </summary>
    public static IReadOnlyList<decimal> DistributeByShare(
        decimal total,
        IReadOnlyList<decimal> weights,
        decimal step,
        SuggestionRoundingMode mode,
        SuggestionRemainderMethod remainderMethod,
        int? bufferIndex = null)
    {
        if (weights.Count == 0)
        {
            return [];
        }

        var weightSum = weights.Sum();
        if (weightSum <= 0)
        {
            throw new ArgumentException("Weights must have a positive sum; an equal split is not supported here.", nameof(weights));
        }

        var ideal = weights.Select(w => total * w / weightSum).ToList();

        if (step <= 0)
        {
            // No rounding requested: the exact proportional split already sums to total up to decimal precision;
            // force the last cell to absorb any residual so the invariant (sum == total) always holds exactly.
            var exact = ideal.ToList();
            var residual = total - exact.Sum();
            if (residual != 0m)
            {
                exact[^1] += residual;
            }

            return exact;
        }

        var baseAmounts = ideal.Select(v => RoundToStep(v, step, mode)).ToList();
        var diff = total - baseAmounts.Sum();

        if (diff == 0m)
        {
            return baseAmounts;
        }

        if (remainderMethod == SuggestionRemainderMethod.Buffer)
        {
            var idx = bufferIndex is int b && b >= 0 && b < baseAmounts.Count ? b : baseAmounts.Count - 1;
            baseAmounts[idx] += diff;
            return baseAmounts;
        }

        // LARGEST_REMAINDER (default, D1). diff is a whole number of steps whenever total and the rounded cells
        // line up on the step grid (the normal case). When total itself is not a multiple of step, the single
        // largest-weight cell absorbs the leftover fraction so the sum is always exactly preserved.
        var stepUnits = diff / step;
        if (stepUnits != Math.Truncate(stepUnits))
        {
            var fallbackIndex = Enumerable.Range(0, weights.Count).OrderByDescending(i => weights[i]).First();
            baseAmounts[fallbackIndex] += diff;
            return baseAmounts;
        }

        var units = (int)stepUnits;
        var remainders = ideal
            .Select((v, i) => (Index: i, Remainder: v - baseAmounts[i], Weight: weights[i]))
            .ToList();

        if (units > 0)
        {
            // Cells rounded down the most relative to their ideal share get the next step first; ties broken by
            // larger weight, then lower index, for a stable and reproducible order.
            foreach (var r in remainders.OrderByDescending(r => r.Remainder).ThenByDescending(r => r.Weight).ThenBy(r => r.Index).Take(units))
            {
                baseAmounts[r.Index] += step;
            }
        }
        else
        {
            foreach (var r in remainders.OrderBy(r => r.Remainder).ThenByDescending(r => r.Weight).ThenBy(r => r.Index).Take(-units))
            {
                baseAmounts[r.Index] -= step;
            }
        }

        return baseAmounts;
    }

    /// <summary>
    /// Splits every row's year amount into 4 quarters so each row sums to its year AND each quarter column sums to
    /// the parent's quarter (<paramref name="columnTotals"/>), whenever the rows add up to the parent year. A row in
    /// <paramref name="fixedRows"/> keeps its own quarters untouched. Each free row starts from the parent's quarter
    /// shape, then amounts move between quarters inside a row until every column is exact. When the rows do not add
    /// up to the parent (or the parent has no quarters yet) each row just follows the parent's shape.
    /// </summary>
    public static decimal[][] SplitIntoQuarters(
        IReadOnlyList<decimal> rowTotals,
        IReadOnlyList<decimal[]?> fixedRows,
        IReadOnlyList<decimal> columnTotals,
        decimal step,
        SuggestionRoundingMode mode)
    {
        var shape = QuarterShapeOrProgressive(columnTotals);
        var rows = new decimal[rowTotals.Count][];
        var free = new List<int>();
        var remainingColumns = columnTotals.ToArray();
        for (var i = 0; i < rowTotals.Count; i++)
        {
            if (fixedRows[i] is { } fixedQuarters)
            {
                rows[i] = fixedQuarters;
                for (var q = 0; q < 4; q++)
                {
                    remainingColumns[q] -= fixedQuarters[q];
                }

                continue;
            }

            rows[i] = rowTotals[i] > 0
                ? DistributeByShare(rowTotals[i], shape, step, mode, SuggestionRemainderMethod.LargestRemainder).ToArray()
                : [0m, 0m, 0m, 0m];
            free.Add(i);
        }

        var freeTotal = free.Sum(i => rowTotals[i]);
        if (!columnTotals.Any(v => v > 0) || remainingColumns.Any(v => v < 0) || remainingColumns.Sum() != freeTotal)
        {
            return rows;
        }

        // Repair: move amounts between quarters inside a free row until each column hits its target. Each move
        // keeps the row sum, never goes negative, and fully closes a column gap or empties a cell.
        for (var guard = 0; guard < 10_000; guard++)
        {
            var gap = new decimal[4];
            for (var q = 0; q < 4; q++)
            {
                gap[q] = remainingColumns[q] - free.Sum(i => rows[i][q]);
            }

            var over = Array.FindIndex(gap, g => g < 0);
            var under = Array.FindIndex(gap, g => g > 0);
            if (over < 0 || under < 0)
            {
                break;
            }

            var donor = free.OrderByDescending(i => rows[i][over]).First();
            var move = Math.Min(Math.Min(-gap[over], gap[under]), rows[donor][over]);
            if (move <= 0)
            {
                break;
            }

            rows[donor][over] -= move;
            rows[donor][under] += move;
        }

        return rows;
    }
}

/// <summary>
/// Read-only suggestion engine (docs/plans/TARGET_ALLOCATION_V3_DECISIONS.md, D1/D4/D5/D6/D8/D9). Reuses the same
/// entities and read patterns as <c>TargetSetupService</c> but never writes: the caller reviews the suggestion and
/// saves it through the existing quarter-split / allocation / employee-allocation PUT endpoints.
/// </summary>
public sealed class TargetSuggestionService(
    IUnitOfWork uow, AppDbContext db, IPermissionService permissions, IOptions<SystemParametersOptions> systemParameters)
    : ITargetSuggestionService
{
    private const decimal DefaultMoneyStep = 10_000_000m;
    private const decimal DefaultCountStep = 1m;

    /// <summary>D9: ceiling/floor warning band. Read live (not captured at construction) so admin changes via
    /// SCR-ADM-04 apply to the next suggestion call without restarting the process.</summary>
    private decimal CeilingPercent => systemParameters.Value.TargetSuggestionCeilingPercent;

    public async Task<SuggestTargetsResponseDto> SuggestAsync(int year, SuggestTargetsRequest request, Guid userId, CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        return request.Tier switch
        {
            1 => await SuggestTier1Async(year, request, userId, cancellationToken),
            2 => await SuggestTier2Async(year, request, userId, cancellationToken),
            3 => await SuggestTier3Async(year, request, userId, cancellationToken),
            _ => throw new ValidationException("tier phải là 1, 2 hoặc 3."),
        };
    }

    private static void ValidateRequest(SuggestTargetsRequest request)
    {
        if (request.Basis != SuggestionBasis.TargetLastYear && request.Basis != SuggestionBasis.ActualLastYear)
        {
            throw new ValidationException("basis phải là TARGET_LAST_YEAR hoặc ACTUAL_LAST_YEAR.");
        }

        if (request.Overwrite != SuggestionOverwrite.EmptyOnly && request.Overwrite != SuggestionOverwrite.Unlocked)
        {
            throw new ValidationException("overwrite phải là EMPTY_ONLY hoặc UNLOCKED.");
        }

        var method = request.Remainder?.Method ?? SuggestionRemainderMethodWire.LargestRemainder;
        if (method == SuggestionRemainderMethodWire.Buffer && request.Remainder?.BufferUnitId is null)
        {
            throw new ValidationException("remainder.bufferUnitId là bắt buộc khi remainder.method = BUFFER.");
        }
        else if (method != SuggestionRemainderMethodWire.Buffer && method != SuggestionRemainderMethodWire.LargestRemainder)
        {
            throw new ValidationException("remainder.method phải là LARGEST_REMAINDER hoặc BUFFER.");
        }

        // D6/contract: actuals rarely land on a round number, so a rounding step is mandatory for this basis —
        // otherwise every cell would differ from its neighbours by odd cents forever.
        if (request.Basis == SuggestionBasis.ActualLastYear)
        {
            var moneyStep = request.Rounding?.MoneyStep ?? DefaultMoneyStep;
            var countStep = request.Rounding?.CountStep ?? DefaultCountStep;
            if (moneyStep <= 0 || countStep <= 0)
            {
                throw new ValidationException("Cần có bước làm tròn (rounding step > 0) khi basis = ACTUAL_LAST_YEAR.");
            }
        }
    }

    private static SuggestionRoundingMode ParseMode(string? mode) => mode switch
    {
        null or "" => SuggestionRoundingMode.Nearest,
        SuggestionRoundingModeWire.Nearest => SuggestionRoundingMode.Nearest,
        SuggestionRoundingModeWire.Up => SuggestionRoundingMode.Up,
        SuggestionRoundingModeWire.Down => SuggestionRoundingMode.Down,
        _ => throw new ValidationException($"rounding.mode '{mode}' không hợp lệ."),
    };

    private static SuggestionRemainderMethod ParseRemainderMethod(SuggestionRemainderDto? remainder) =>
        remainder?.Method switch
        {
            null or "" or SuggestionRemainderMethodWire.LargestRemainder => SuggestionRemainderMethod.LargestRemainder,
            SuggestionRemainderMethodWire.Buffer => SuggestionRemainderMethod.Buffer,
            _ => throw new ValidationException($"remainder.method '{remainder.Method}' không hợp lệ."),
        };

    /// <summary>Whole-number-friendly amount for warning text, grouped with `.` like the UI (1.234.567,5).</summary>
    private static string Amount(decimal value) =>
        value.ToString("#,##0.##", System.Globalization.CultureInfo.InvariantCulture)
            .Replace(',', '').Replace('.', ',').Replace('', '.');

    private static bool IsMoneyUnit(string unit) =>
        unit.Contains("VND", StringComparison.OrdinalIgnoreCase)
        || unit.Contains("đồng", StringComparison.OrdinalIgnoreCase)
        || unit.Contains("triệu", StringComparison.OrdinalIgnoreCase)
        || unit.Contains("tỷ", StringComparison.OrdinalIgnoreCase);

    /// <summary>The rounding step in the KPI's own unit: the money step is in VND, but a KPI measured in "tỷ" or
    /// "triệu" stores its values in billions or millions, so the step is scaled down to match.</summary>
    private static decimal StepFor(string unit, SuggestionRoundingDto? rounding)
    {
        if (!IsMoneyUnit(unit))
        {
            return rounding?.CountStep ?? DefaultCountStep;
        }

        var vndPerUnit = unit.Contains("tỷ", StringComparison.OrdinalIgnoreCase) ? 1_000_000_000m
            : unit.Contains("triệu", StringComparison.OrdinalIgnoreCase) ? 1_000_000m
            : 1m;
        return (rounding?.MoneyStep ?? DefaultMoneyStep) / vndPerUnit;
    }

    /// <summary>A cell the run left as it was keeps the quarters the caller already has for it (all four present).</summary>
    private static decimal[]?[] FixedQuarterRows(
        List<Guid> unitIds, bool[] overwritten, Dictionary<Guid, SuggestionCurrentValueDto> currentByUnit)
    {
        var rows = new decimal[]?[unitIds.Count];
        for (var i = 0; i < unitIds.Count; i++)
        {
            if (!overwritten[i]
                && currentByUnit.TryGetValue(unitIds[i], out var c)
                && c is { Q1: { } q1, Q2: { } q2, Q3: { } q3, Q4: { } q4 })
            {
                rows[i] = [q1, q2, q3, q4];
            }
        }

        return rows;
    }

    private static int? ResolveBufferIndex(List<Guid> unitIds, Guid? bufferUnitId)
    {
        if (bufferUnitId is null)
        {
            return null;
        }

        for (var i = 0; i < unitIds.Count; i++)
        {
            if (unitIds[i] == bufferUnitId.Value)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// Fills free (non-fixed) cells by weight and leaves fixed cells (locked, or already filled when
    /// <c>overwrite = EMPTY_ONLY</c>) untouched, so the final set of cells always sums to <paramref name="total"/>
    /// whenever the fixed cells don't already exceed it.
    /// </summary>
    private (decimal[] Amounts, bool[] Overwritten) ApplyPlan(
        decimal total,
        IReadOnlyList<decimal> weights,
        IReadOnlyList<decimal?> currentValues,
        IReadOnlyList<bool> locked,
        string overwrite,
        decimal step,
        SuggestionRoundingMode mode,
        SuggestionRemainderMethod remainderMethod,
        int? bufferIndex,
        List<string> warnings,
        List<string>? unitNames = null,
        bool historyBasis = true)
    {
        var n = weights.Count;
        var fixedMask = new bool[n];
        for (var i = 0; i < n; i++)
        {
            if (locked[i] || (overwrite == SuggestionOverwrite.EmptyOnly && currentValues[i].HasValue))
            {
                fixedMask[i] = true;
            }
        }

        var fixedTotal = 0m;
        for (var i = 0; i < n; i++)
        {
            if (fixedMask[i])
            {
                fixedTotal += currentValues[i] ?? 0m;
            }
        }

        var amounts = new decimal[n];
        var overwritten = new bool[n];
        for (var i = 0; i < n; i++)
        {
            if (fixedMask[i])
            {
                amounts[i] = currentValues[i] ?? 0m;
            }
        }

        var freeIndexes = Enumerable.Range(0, n).Where(i => !fixedMask[i]).ToList();
        if (freeIndexes.Count == 0)
        {
            if (fixedTotal != total)
            {
                warnings.Add($"Mọi ô đã bị khóa hoặc đã có giá trị; tổng = {Amount(fixedTotal)}, chỉ tiêu cần phân bổ = {Amount(total)}.");
            }

            return (amounts, overwritten);
        }

        var remainderTotal = total - fixedTotal;
        if (remainderTotal < 0m)
        {
            warnings.Add($"Các ô cố định đã vượt tổng cần phân bổ ({Amount(fixedTotal)} > {Amount(total)}); phần còn lại được đặt về 0.");
            remainderTotal = 0m;
        }

        var freeWeights = freeIndexes.Select(i => weights[i]).ToList();
        var freeWeightSum = freeWeights.Sum();

        List<decimal> distributed;
        if (freeWeightSum <= 0)
        {
            // No basis for any free unit: leave them at 0 rather than falling back to an equal split (out of scope).
            distributed = freeIndexes.Select(_ => 0m).ToList();
            warnings.Add("Không có dữ liệu lịch sử cho các ô còn trống; các ô này được giữ = 0.");
        }
        else
        {
            int? mappedBufferIndex = null;
            if (bufferIndex is int bi)
            {
                var mapped = freeIndexes.IndexOf(bi);
                mappedBufferIndex = mapped >= 0 ? mapped : null;
            }

            distributed = SuggestionMath
                .DistributeByShare(remainderTotal, freeWeights, step, mode, remainderMethod, mappedBufferIndex)
                .ToList();
        }

        for (var k = 0; k < freeIndexes.Count; k++)
        {
            amounts[freeIndexes[k]] = distributed[k];
            overwritten[freeIndexes[k]] = true;
        }

        // D9: ceiling/floor warning — a newly-suggested (overwritten, non-locked) cell that deviates from its own
        // basis value by more than the configured percent gets a non-blocking warning; it is never clamped.
        // Skipped for a no-history fallback: its weights (1:2:3:4, headcount, equal) are not last year's values.
        var ceiling = historyBasis ? CeilingPercent : 0m;
        if (ceiling > 0)
        {
            for (var i = 0; i < n; i++)
            {
                if (!overwritten[i] || weights[i] <= 0)
                {
                    continue;
                }

                var deviationPercent = Math.Abs(amounts[i] - weights[i]) / weights[i] * 100m;
                if (deviationPercent > ceiling)
                {
                    var name = unitNames is not null && i < unitNames.Count ? unitNames[i] : $"#{i + 1}";
                    warnings.Add(
                        $"{name}: giá trị gợi ý ({Amount(amounts[i])}) lệch {deviationPercent:0.#}% so với mốc năm trước ({Amount(weights[i])}), vượt ngưỡng cảnh báo ±{ceiling:0.#}%.");
                }
            }
        }

        return (amounts, overwritten);
    }

    // =========================================================================
    // Tier 1: branch -> quarters
    // =========================================================================

    private async Task<(Guid BranchId, List<TargetAssignment> BranchYearRows, EvaluationPeriod YearPeriod)> LoadBranchYearAsync(
        Guid branchYearId, int year, Guid userId, CancellationToken cancellationToken)
    {
        var anchor = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.EvaluationPeriod)
            .FirstOrDefaultAsync(t => t.Id == branchYearId, cancellationToken)
            ?? throw new NotFoundException("Branch-year target not found.");

        if (anchor.SplitDimension is not null || anchor.ParentAssignmentId is not null
            || anchor.EvaluationPeriod.PeriodType != PeriodType.Year || anchor.DepartmentId is null)
        {
            throw new NotFoundException("Invalid branch-year target.");
        }

        if (anchor.EvaluationPeriod.StartDate.Year != year)
        {
            throw new ValidationException($"Chỉ tiêu năm của chi nhánh không thuộc năm {year}.");
        }

        await DepartmentAccessGuard.EnsureDepartmentAccessAsync(uow, permissions, userId, anchor.DepartmentId, cancellationToken);

        var branchId = anchor.DepartmentId.Value;
        var rows = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.KpiDefinition).ThenInclude(k => k.KpiGroup)
            .Where(t => t.DepartmentId == branchId
                && t.EvaluationPeriodId == anchor.EvaluationPeriodId
                && t.ParentAssignmentId == null
                && t.SplitDimension == null)
            .OrderBy(t => t.KpiDefinition.KpiGroup.Code)
            .ThenBy(t => t.KpiDefinition.Code)
            .ToListAsync(cancellationToken);

        return (branchId, rows, anchor.EvaluationPeriod);
    }

    /// <summary>Walks the parent-assignment tree down from <paramref name="rootId"/> and sums VALID actuals per quarter (same walk as TargetSetupService's previous-year actual rollup).</summary>
    private async Task<decimal[]> SumActualsByQuarterAsync(
        Guid rootId, EvaluationPeriod previousYearPeriod, Dictionary<int, EvaluationPeriod> prevQByNum, CancellationToken cancellationToken)
    {
        var treeIds = await CollectDescendantIdsAsync(rootId, cancellationToken);

        var rows = await db.ActualProgresses
            .AsNoTracking()
            .Where(a => a.Status == ActualProgressStatus.Valid
                && treeIds.Contains(a.TargetAssignmentId)
                && a.ProgressDate >= previousYearPeriod.StartDate
                && a.ProgressDate <= previousYearPeriod.EndDate)
            .Select(a => new { a.ProgressDate, a.Value })
            .ToListAsync(cancellationToken);

        var sums = new decimal[4];
        foreach (var row in rows)
        {
            var quarterEntry = prevQByNum.FirstOrDefault(kv => row.ProgressDate >= kv.Value.StartDate && row.ProgressDate <= kv.Value.EndDate);
            if (quarterEntry.Key is >= 1 and <= 4)
            {
                sums[quarterEntry.Key - 1] += row.Value;
            }
        }

        return sums;
    }

    /// <summary>Walks the parent-assignment tree down from <paramref name="rootId"/> and sums every VALID actual in range (year total, no quarter split).</summary>
    private async Task<decimal> SumActualYearAsync(Guid rootId, EvaluationPeriod period, CancellationToken cancellationToken)
    {
        var treeIds = await CollectDescendantIdsAsync(rootId, cancellationToken);

        return await db.ActualProgresses
            .AsNoTracking()
            .Where(a => a.Status == ActualProgressStatus.Valid
                && treeIds.Contains(a.TargetAssignmentId)
                && a.ProgressDate >= period.StartDate
                && a.ProgressDate <= period.EndDate)
            .SumAsync(a => a.Value, cancellationToken);
    }

    private async Task<HashSet<Guid>> CollectDescendantIdsAsync(Guid rootId, CancellationToken cancellationToken)
    {
        var treeIds = new HashSet<Guid> { rootId };
        var currentLevelIds = new List<Guid> { rootId };
        while (currentLevelIds.Count > 0)
        {
            var nextLevelIds = await db.TargetAssignments
                .AsNoTracking()
                .Where(t => t.ParentAssignmentId != null && currentLevelIds.Contains(t.ParentAssignmentId.Value))
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            if (nextLevelIds.Count == 0)
            {
                break;
            }

            foreach (var id in nextLevelIds)
            {
                treeIds.Add(id);
            }

            currentLevelIds = nextLevelIds;
        }

        return treeIds;
    }

    private async Task<SuggestTargetsResponseDto> SuggestTier1Async(int year, SuggestTargetsRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var (branchId, branchYearRows, yearPeriod) = await LoadBranchYearAsync(request.AnchorId, year, userId, cancellationToken);
        var quarterPeriods = (await uow.EvaluationPeriods.GetInRangeAsync(PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken)).ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var quarterIds = quarterPeriods.Select(q => q.Id).ToList();

        var previousYearPeriod = await uow.EvaluationPeriods.GetYearAsync(year - 1, cancellationToken);
        var prevQByNum = new Dictionary<int, EvaluationPeriod>();
        if (previousYearPeriod is not null)
        {
            var prevQuarterPeriods = await uow.EvaluationPeriods.GetInRangeAsync(PeriodType.Quarter, previousYearPeriod.StartDate, previousYearPeriod.EndDate, cancellationToken);
            prevQByNum = prevQuarterPeriods.Select((p, idx) => (Number: idx + 1, Period: p)).ToDictionary(x => x.Number, x => x.Period);
        }

        var scopeKpiIds = request.KpiIds?.ToHashSet();
        var currentByUnit = (request.CurrentValues ?? []).ToDictionary(c => c.UnitId);
        var mode = ParseMode(request.Rounding?.Mode);
        var remainderMethod = ParseRemainderMethod(request.Remainder);
        var bufferIndex = ResolveBufferIndex(quarterIds, request.Remainder?.BufferUnitId);

        var kpiResults = new List<SuggestedKpiDto>();

        foreach (var branchYearRow in branchYearRows.Where(r => r.KpiDefinition.AggregationType == KpiAggregationType.Sum))
        {
            var kpi = branchYearRow.KpiDefinition;
            if (scopeKpiIds is not null && !scopeKpiIds.Contains(kpi.Id))
            {
                continue;
            }

            decimal[]? prevQuarters = null;
            if (previousYearPeriod is not null)
            {
                var prevRow = await db.TargetAssignments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.DepartmentId == branchId
                        && t.KpiDefinitionId == kpi.Id
                        && t.EvaluationPeriodId == previousYearPeriod.Id
                        && t.ParentAssignmentId == null
                        && t.SplitDimension == null
                        && t.Status == TargetAssignmentStatus.Active, cancellationToken);

                if (prevRow is not null)
                {
                    if (request.Basis == SuggestionBasis.TargetLastYear)
                    {
                        var prevQuarterRows = await db.TargetAssignments
                            .AsNoTracking()
                            .Where(t => t.ParentAssignmentId == prevRow.Id && t.SplitDimension == TargetSplitDimension.Period)
                            .ToListAsync(cancellationToken);
                        var byPeriod = prevQuarterRows.ToDictionary(r => r.EvaluationPeriodId, r => r.TargetValue);
                        prevQuarters = new decimal[4];
                        for (var i = 1; i <= 4; i++)
                        {
                            prevQuarters[i - 1] = prevQByNum.TryGetValue(i, out var p) && byPeriod.TryGetValue(p.Id, out var v) ? v : 0m;
                        }
                    }
                    else
                    {
                        prevQuarters = await SumActualsByQuarterAsync(prevRow.Id, previousYearPeriod, prevQByNum, cancellationToken);
                    }
                }
            }

            // No usable history: progressive Q1:Q2:Q3:Q4 = 1:2:3:4 (flagged through usedFallbackAverage).
            var noHistory = prevQuarters is null || prevQuarters.All(v => v == 0m);
            IReadOnlyList<decimal> quarterWeights = noHistory || prevQuarters is null
                ? SuggestionMath.ProgressiveQuarterWeights
                : prevQuarters;

            var warnings = new List<string>();
            var step = StepFor(kpi.Unit, request.Rounding);
            currentByUnit.TryGetValue(kpi.Id, out var current);

            var locked = new bool[4];
            for (var i = 0; i < 4; i++)
            {
                var periodLocked = quarterPeriods.Count > i && (quarterPeriods[i].Status != PeriodStatus.Open || quarterPeriods[i].EndDate < today);
                locked[i] = periodLocked || (current?.Locked ?? false);
            }

            var currentValues = new decimal?[] { current?.Q1, current?.Q2, current?.Q3, current?.Q4 };
            var (amounts, overwritten) = ApplyPlan(
                branchYearRow.TargetValue, quarterWeights, currentValues, locked, request.Overwrite, step, mode, remainderMethod, bufferIndex, warnings,
                ["Quý 1", "Quý 2", "Quý 3", "Quý 4"], historyBasis: !noHistory);

            var sum = amounts.Sum();
            var cell = new SuggestedCellDto(
                kpi.Id, kpi.Code, branchYearRow.TargetValue, amounts[0], amounts[1], amounts[2], amounts[3],
                UsedFallbackAverage: noHistory, Overwritten: overwritten.Any(o => o), Locked: locked.All(l => l),
                LockReason: locked.All(l => l) ? "period_locked" : null);

            kpiResults.Add(new SuggestedKpiDto(
                kpi.Id, kpi.Code, kpi.Name, kpi.Unit, kpi.AggregationType.ToEnumString(), [cell], [],
                branchYearRow.TargetValue, sum == branchYearRow.TargetValue, null, warnings));
        }

        return new SuggestTargetsResponseDto(1, request.Basis, request.Overwrite, kpiResults, []);
    }

    // =========================================================================
    // Tier 2: branch -> departments (one KPI at a time, same scoping as the Phase 2 allocation endpoints)
    // =========================================================================

    /// <summary>Same eligibility rule as TargetSetupService (active evaluation profile item for the KPI, falling back to the work-type link).</summary>
    private async Task<Dictionary<Guid, int>> GetEligibleDepartmentHeadcountsAsync(Guid branchId, Guid kpiId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var departments = await db.Departments
            .AsNoTracking()
            .Where(d => d.ParentDepartmentId == branchId && d.LevelType == DepartmentLevelType.Department)
            .ToListAsync(cancellationToken);

        // Department id -> number of employees eligible for the KPI (the no-history weight of tier 2).
        var eligible = new Dictionary<Guid, int>();
        foreach (var dept in departments)
        {
            var (eligibleEmployees, _, _) = await GetEligibleEmployeesAsync(dept.Id, kpiId, asOf, cancellationToken);
            if (eligibleEmployees.Count > 0)
            {
                eligible[dept.Id] = eligibleEmployees.Count;
            }
        }

        return eligible;
    }

    /// <summary>Splits a department's active employees into those eligible for a KPI (active evaluation-profile item, falling back to the work-type link) and those who aren't (D7/BR rule).</summary>
    private async Task<(List<Employee> Eligible, List<Employee> Ineligible, Department Department)> GetEligibleEmployeesAsync(
        Guid departmentId, Guid kpiId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var department = await db.Departments.AsNoTracking().FirstAsync(d => d.Id == departmentId, cancellationToken);
        var employees = await db.Employees
            .AsNoTracking()
            .Include(e => e.JobPosition)
            .Where(e => e.DepartmentId == departmentId && e.Status == EmployeeStatus.Active)
            .OrderBy(e => e.EmployeeCode)
            .ToListAsync(cancellationToken);

        if (employees.Count == 0)
        {
            return ([], [], department);
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

        var jobPosWithoutProfile = jobPositionIds.Where(jp => !jobPosWithProfile.Contains(jp)).ToList();
        if (jobPosWithoutProfile.Count > 0)
        {
            var workTypeIds = await db.WorkTypeKpis
                .AsNoTracking()
                .Where(wk => wk.KpiDefinitionId == kpiId && wk.EffectiveFrom <= asOf && (wk.EffectiveTo == null || wk.EffectiveTo >= asOf))
                .Select(wk => wk.WorkTypeId)
                .ToListAsync(cancellationToken);

            if (workTypeIds.Count > 0)
            {
                var matching = await db.JobPositionWorkTypes
                    .AsNoTracking()
                    .Where(jpwt => jobPosWithoutProfile.Contains(jpwt.JobPositionId) && workTypeIds.Contains(jpwt.WorkTypeId))
                    .Select(jpwt => jpwt.JobPositionId)
                    .ToListAsync(cancellationToken);

                foreach (var jp in matching)
                {
                    eligibleJobPositionIds.Add(jp);
                }
            }
        }

        var eligible = employees.Where(e => eligibleJobPositionIds.Contains(e.JobPositionId)).ToList();
        var ineligible = employees.Where(e => !eligibleJobPositionIds.Contains(e.JobPositionId)).ToList();
        return (eligible, ineligible, department);
    }

    private async Task<SuggestTargetsResponseDto> SuggestTier2Async(int year, SuggestTargetsRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var (branchId, branchYearRows, yearPeriod) = await LoadBranchYearAsync(request.AnchorId, year, userId, cancellationToken);
        var quarterPeriods = (await uow.EvaluationPeriods.GetInRangeAsync(PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken)).ToList();
        var previousYearPeriod = await uow.EvaluationPeriods.GetYearAsync(year - 1, cancellationToken);

        var scopeKpiIds = request.KpiIds?.ToHashSet();
        var currentByUnit = (request.CurrentValues ?? []).ToDictionary(c => c.UnitId);
        var mode = ParseMode(request.Rounding?.Mode);
        var remainderMethod = ParseRemainderMethod(request.Remainder);

        var kpiResults = new List<SuggestedKpiDto>();

        foreach (var branchYearRow in branchYearRows.Where(r => r.KpiDefinition.AggregationType == KpiAggregationType.Sum))
        {
            var kpi = branchYearRow.KpiDefinition;
            if (scopeKpiIds is not null && !scopeKpiIds.Contains(kpi.Id))
            {
                continue;
            }

            var warnings = new List<string>();
            var headcountByDept = await GetEligibleDepartmentHeadcountsAsync(branchId, kpi.Id, yearPeriod.StartDate, cancellationToken);
            var eligibleDeptIds = headcountByDept.Keys.ToList();
            var allDepts = await db.Departments
                .AsNoTracking()
                .Where(d => d.ParentDepartmentId == branchId && d.LevelType == DepartmentLevelType.Department)
                .OrderBy(d => d.Code)
                .ToListAsync(cancellationToken);
            var nameById = allDepts.ToDictionary(d => d.Id, d => d.Name);

            var lockedUnits = allDepts
                .Where(d => !eligibleDeptIds.Contains(d.Id))
                .Select(d => new SuggestionLockedUnitDto(d.Id, d.Name, "no_headcount"))
                .ToList();

            if (eligibleDeptIds.Count == 0)
            {
                kpiResults.Add(new SuggestedKpiDto(kpi.Id, kpi.Code, kpi.Name, kpi.Unit, kpi.AggregationType.ToEnumString(), [], lockedUnits, 0m, false, "no_history", []));
                continue;
            }

            // Previous-year weight per eligible department (basis-dependent).
            var weightByDept = new Dictionary<Guid, decimal>();
            var usedFallback = new HashSet<Guid>();

            if (previousYearPeriod is not null)
            {
                var prevDeptYearRows = await db.TargetAssignments
                    .AsNoTracking()
                    .Where(t => t.DepartmentId != null && eligibleDeptIds.Contains(t.DepartmentId.Value)
                        && t.SplitDimension == TargetSplitDimension.Org
                        && t.EmployeeId == null
                        && t.KpiDefinitionId == kpi.Id
                        && t.Status == TargetAssignmentStatus.Active
                        && t.EvaluationPeriodId == previousYearPeriod.Id)
                    .ToListAsync(cancellationToken);

                if (request.Basis == SuggestionBasis.TargetLastYear)
                {
                    foreach (var row in prevDeptYearRows)
                    {
                        if (row.TargetValue > 0)
                        {
                            weightByDept[row.DepartmentId!.Value] = row.TargetValue;
                        }
                    }
                }
                else
                {
                    foreach (var row in prevDeptYearRows)
                    {
                        var actual = await SumActualYearAsync(row.Id, previousYearPeriod, cancellationToken);
                        if (actual > 0)
                        {
                            weightByDept[row.DepartmentId!.Value] = actual;
                        }
                    }
                }
            }

            // No history for the KPI in any department: split the year by headcount. Otherwise only departments with
            // their own history are suggested (the rest are named in a warning and left untouched).
            var noHistory = weightByDept.Count == 0;
            if (noHistory)
            {
                foreach (var (deptId, headcount) in headcountByDept)
                {
                    weightByDept[deptId] = headcount;
                    usedFallback.Add(deptId);
                }
            }
            else
            {
                // A department without its own history (e.g. newly created) gets the per-head average of the
                // departments that have one, times its headcount, so it is suggested instead of left at 0.
                var perHead = weightByDept.Sum(kv => kv.Value) / weightByDept.Sum(kv => headcountByDept.GetValueOrDefault(kv.Key, 1));
                foreach (var deptId in eligibleDeptIds.Where(id => !weightByDept.ContainsKey(id)))
                {
                    weightByDept[deptId] = perHead * headcountByDept[deptId];
                    usedFallback.Add(deptId);
                }
            }

            var orderedDeptIds = eligibleDeptIds.Where(weightByDept.ContainsKey).ToList();
            var weights = orderedDeptIds.Select(id => weightByDept[id]).ToList();
            var currentValues = orderedDeptIds.Select(id => currentByUnit.TryGetValue(id, out var c) ? c.Year : (decimal?)null).ToList();
            var locked = orderedDeptIds.Select(id => currentByUnit.TryGetValue(id, out var c) && c.Locked).ToList();
            var bufferIndex = ResolveBufferIndex(orderedDeptIds, request.Remainder?.BufferUnitId);
            var step = StepFor(kpi.Unit, request.Rounding);

            var deptUnitNames = orderedDeptIds.Select(id => nameById.GetValueOrDefault(id, id.ToString())).ToList();
            var (yearAmounts, overwritten) = ApplyPlan(
                branchYearRow.TargetValue, weights, currentValues, locked, request.Overwrite, step, mode, remainderMethod, bufferIndex, warnings,
                deptUnitNames, !noHistory);

            // Branch's own quarter shape (ratios), used to split each department's suggested year value into
            // quarters so that department's own quarters always sum to its year value exactly; progressive 1:2:3:4
            // when the branch has no quarter split yet.
            var branchQuarterValues = new decimal[4];
            if (kpi.AggregationType == KpiAggregationType.Sum)
            {
                var branchQuarterRows = await db.TargetAssignments
                    .AsNoTracking()
                    .Where(t => t.ParentAssignmentId == branchYearRow.Id && t.SplitDimension == TargetSplitDimension.Period)
                    .ToListAsync(cancellationToken);
                var byPeriod = branchQuarterRows.ToDictionary(r => r.EvaluationPeriodId, r => r.TargetValue);
                for (var i = 0; i < 4 && i < quarterPeriods.Count; i++)
                {
                    branchQuarterValues[i] = byPeriod.TryGetValue(quarterPeriods[i].Id, out var v) ? v : 0m;
                }
            }

            var quarterRows = SuggestionMath.SplitIntoQuarters(
                yearAmounts, FixedQuarterRows(orderedDeptIds, overwritten, currentByUnit), branchQuarterValues, step, mode);
            var cells = new List<SuggestedCellDto>();
            for (var i = 0; i < orderedDeptIds.Count; i++)
            {
                var deptId = orderedDeptIds[i];
                var deptQuarters = quarterRows[i];

                cells.Add(new SuggestedCellDto(
                    deptId, nameById.GetValueOrDefault(deptId, deptId.ToString()), yearAmounts[i],
                    deptQuarters[0], deptQuarters[1], deptQuarters[2], deptQuarters[3],
                    UsedFallbackAverage: usedFallback.Contains(deptId), Overwritten: overwritten[i], Locked: locked[i],
                    LockReason: locked[i] ? "period_locked" : null));
            }

            var total = yearAmounts.Sum();
            var deptQuarterTotals = new decimal[4];
            foreach (var cell in cells)
            {
                deptQuarterTotals[0] += cell.Q1;
                deptQuarterTotals[1] += cell.Q2;
                deptQuarterTotals[2] += cell.Q3;
                deptQuarterTotals[3] += cell.Q4;
            }

            for (var i = 0; i < 4 && branchQuarterValues.Any(v => v > 0); i++)
            {
                if (deptQuarterTotals[i] != branchQuarterValues[i])
                {
                    warnings.Add($"Quý {i + 1}: tổng gợi ý của các phòng = {Amount(deptQuarterTotals[i])}, chỉ tiêu quý của chi nhánh = {Amount(branchQuarterValues[i])}. Vui lòng kiểm tra lại trước khi lưu.");
                }
            }

            kpiResults.Add(new SuggestedKpiDto(
                kpi.Id, kpi.Code, kpi.Name, kpi.Unit, kpi.AggregationType.ToEnumString(), cells, lockedUnits,
                branchYearRow.TargetValue, total == branchYearRow.TargetValue, null, warnings));
        }

        return new SuggestTargetsResponseDto(2, request.Basis, request.Overwrite, kpiResults, []);
    }

    // =========================================================================
    // Tier 3: department -> employees (one department-year KPI row at a time, mirrors tier 2 one level down)
    // =========================================================================

    private async Task<(TargetAssignment DeptYear, Guid DepartmentId, int Year, EvaluationPeriod YearPeriod)> LoadDeptYearAsync(
        Guid deptYearId, int year, Guid userId, CancellationToken cancellationToken)
    {
        var deptYear = await db.TargetAssignments
            .AsNoTracking()
            .Include(t => t.KpiDefinition)
            .Include(t => t.EvaluationPeriod)
            .FirstOrDefaultAsync(t => t.Id == deptYearId, cancellationToken)
            ?? throw new NotFoundException("Department-year target not found.");

        if (deptYear.SplitDimension != TargetSplitDimension.Org || deptYear.ParentAssignmentId is null
            || deptYear.EmployeeId is not null || deptYear.EvaluationPeriod.PeriodType != PeriodType.Year || deptYear.DepartmentId is null)
        {
            throw new NotFoundException("Invalid department-year target.");
        }

        if (deptYear.EvaluationPeriod.StartDate.Year != year)
        {
            throw new ValidationException($"Chỉ tiêu năm của phòng ban không thuộc năm {year}.");
        }

        await DepartmentAccessGuard.EnsureDepartmentAccessAsync(uow, permissions, userId, deptYear.DepartmentId, cancellationToken);

        return (deptYear, deptYear.DepartmentId.Value, year, deptYear.EvaluationPeriod);
    }

    private async Task<SuggestTargetsResponseDto> SuggestTier3Async(int year, SuggestTargetsRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var (deptYear, departmentId, _, yearPeriod) = await LoadDeptYearAsync(request.AnchorId, year, userId, cancellationToken);
        var kpi = deptYear.KpiDefinition;
        var quarterPeriods = (await uow.EvaluationPeriods.GetInRangeAsync(PeriodType.Quarter, yearPeriod.StartDate, yearPeriod.EndDate, cancellationToken)).ToList();
        var previousYearPeriod = await uow.EvaluationPeriods.GetYearAsync(year - 1, cancellationToken);

        if (deptYear.Status != TargetAssignmentStatus.Active)
        {
            // Not a hard block (this endpoint never writes), but the suggestion is only meaningful once tier 2 is published.
        }

        var currentByUnit = (request.CurrentValues ?? []).ToDictionary(c => c.UnitId);
        var mode = ParseMode(request.Rounding?.Mode);
        var remainderMethod = ParseRemainderMethod(request.Remainder);
        var warnings = new List<string>();
        if (deptYear.Status != TargetAssignmentStatus.Active)
        {
            warnings.Add("Chỉ tiêu phòng ban (cấp 2) chưa được phát hành; gợi ý vẫn được tính nhưng chưa thể lưu cho đến khi phát hành.");
        }

        var (eligibleEmployees, ineligibleEmployees, _) = await GetEligibleEmployeesAsync(departmentId, kpi.Id, yearPeriod.StartDate, cancellationToken);
        var lockedUnits = ineligibleEmployees
            .Select(e => new SuggestionLockedUnitDto(e.Id, e.FullName, "not_responsible_position"))
            .ToList();

        if (eligibleEmployees.Count == 0)
        {
            var kpiResult = new SuggestedKpiDto(kpi.Id, kpi.Code, kpi.Name, kpi.Unit, kpi.AggregationType.ToEnumString(), [], lockedUnits, 0m, false, "no_history", warnings);
            return new SuggestTargetsResponseDto(3, request.Basis, request.Overwrite, [kpiResult], []);
        }

        // Previous-year weight per eligible employee; an employee without their own history falls back to the
        // average of other employees of the same job position in this department who do have one (D6/D7).
        var weightByEmployee = new Dictionary<Guid, decimal>();
        var usedFallback = new HashSet<Guid>();
        var newEmployeeIds = new HashSet<Guid>(eligibleEmployees.Select(e => e.Id));

        if (previousYearPeriod is not null)
        {
            var eligibleIds = eligibleEmployees.Select(e => e.Id).ToList();
            var prevEmployeeYearRows = await db.TargetAssignments
                .AsNoTracking()
                .Where(t => t.EmployeeId != null && eligibleIds.Contains(t.EmployeeId.Value)
                    && t.SplitDimension == TargetSplitDimension.Org
                    && t.KpiDefinitionId == kpi.Id
                    && t.Status == TargetAssignmentStatus.Active
                    && t.EvaluationPeriodId == previousYearPeriod.Id)
                .ToListAsync(cancellationToken);

            foreach (var row in prevEmployeeYearRows)
            {
                newEmployeeIds.Remove(row.EmployeeId!.Value);
            }

            if (request.Basis == SuggestionBasis.TargetLastYear)
            {
                foreach (var row in prevEmployeeYearRows)
                {
                    if (row.TargetValue > 0)
                    {
                        weightByEmployee[row.EmployeeId!.Value] = row.TargetValue;
                    }
                }
            }
            else
            {
                foreach (var row in prevEmployeeYearRows)
                {
                    var actual = await SumActualYearAsync(row.Id, previousYearPeriod, cancellationToken);
                    if (actual > 0)
                    {
                        weightByEmployee[row.EmployeeId!.Value] = actual;
                    }
                }
            }
        }

        var byPosition = eligibleEmployees.ToDictionary(e => e.Id, e => e.JobPositionId);
        var positionAverages = weightByEmployee
            .GroupBy(kv => byPosition.GetValueOrDefault(kv.Key))
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Value).Average());
        var overallAverage = weightByEmployee.Count > 0 ? weightByEmployee.Values.Average() : 0m;

        foreach (var emp in eligibleEmployees)
        {
            if (weightByEmployee.ContainsKey(emp.Id))
            {
                continue;
            }

            var fallback = positionAverages.TryGetValue(emp.JobPositionId, out var posAvg) ? posAvg : overallAverage;
            if (fallback > 0)
            {
                weightByEmployee[emp.Id] = fallback;
                usedFallback.Add(emp.Id);
            }
        }

        // No usable history for any eligible employee: an equal split of the year (quarters then follow the department's
        // own shape, or the progressive 1:2:3:4).
        var noHistory = weightByEmployee.Count == 0;
        if (noHistory)
        {
            foreach (var emp in eligibleEmployees)
            {
                weightByEmployee[emp.Id] = 1m;
                usedFallback.Add(emp.Id);
            }
        }

        var employeesInScope = request.Employees == SuggestionEmployeeScope.NewOnly
            ? eligibleEmployees.Where(e => newEmployeeIds.Contains(e.Id)).ToList()
            : eligibleEmployees;

        var orderedEmployeeIds = employeesInScope.Select(e => e.Id).Where(weightByEmployee.ContainsKey).ToList();
        if (request.Employees == SuggestionEmployeeScope.NewOnly)
        {
            // Employees out of scope keep their existing value fixed (treated as locked for this suggestion run).
            foreach (var emp in eligibleEmployees.Where(e => !newEmployeeIds.Contains(e.Id)))
            {
                if (!orderedEmployeeIds.Contains(emp.Id))
                {
                    orderedEmployeeIds.Add(emp.Id);
                }
            }
        }

        var weights = orderedEmployeeIds.Select(id => weightByEmployee.GetValueOrDefault(id, 0m)).ToList();
        var currentValues = orderedEmployeeIds.Select(id => currentByUnit.TryGetValue(id, out var c) ? c.Year : (decimal?)null).ToList();
        var locked = orderedEmployeeIds
            .Select(id => (currentByUnit.TryGetValue(id, out var c) && c.Locked)
                || (request.Employees == SuggestionEmployeeScope.NewOnly && !newEmployeeIds.Contains(id)))
            .ToList();
        var bufferIndex = ResolveBufferIndex(orderedEmployeeIds, request.Remainder?.BufferUnitId);
        var step = StepFor(kpi.Unit, request.Rounding);
        var employeeNameById = eligibleEmployees.ToDictionary(e => e.Id, e => e.FullName);
        var employeeUnitNames = orderedEmployeeIds.Select(id => employeeNameById.GetValueOrDefault(id, id.ToString())).ToList();

        var (yearAmounts, overwritten) = ApplyPlan(
            deptYear.TargetValue, weights, currentValues, locked, request.Overwrite, step, mode, remainderMethod, bufferIndex, warnings,
            employeeUnitNames, !noHistory);

        var deptQuarterValues = new decimal[4];
        if (kpi.AggregationType == KpiAggregationType.Sum)
        {
            var deptQuarterRows = await db.TargetAssignments
                .AsNoTracking()
                .Where(t => t.ParentAssignmentId == deptYear.Id && t.SplitDimension == TargetSplitDimension.Period && t.EmployeeId == null)
                .ToListAsync(cancellationToken);
            var byPeriod = deptQuarterRows.ToDictionary(r => r.EvaluationPeriodId, r => r.TargetValue);
            for (var i = 0; i < 4 && i < quarterPeriods.Count; i++)
            {
                deptQuarterValues[i] = byPeriod.TryGetValue(quarterPeriods[i].Id, out var v) ? v : 0m;
            }
        }

        var nameById = eligibleEmployees.ToDictionary(e => e.Id, e => e.FullName);
        var quarterRows = SuggestionMath.SplitIntoQuarters(
            yearAmounts, FixedQuarterRows(orderedEmployeeIds, overwritten, currentByUnit), deptQuarterValues, step, mode);
        var cells = new List<SuggestedCellDto>();
        for (var i = 0; i < orderedEmployeeIds.Count; i++)
        {
            var empId = orderedEmployeeIds[i];
            var empQuarters = quarterRows[i];

            cells.Add(new SuggestedCellDto(
                empId, nameById.GetValueOrDefault(empId, empId.ToString()), yearAmounts[i],
                empQuarters[0], empQuarters[1], empQuarters[2], empQuarters[3],
                UsedFallbackAverage: usedFallback.Contains(empId), Overwritten: overwritten[i], Locked: locked[i],
                LockReason: locked[i] ? (request.Employees == SuggestionEmployeeScope.NewOnly ? "not_new" : "period_locked") : null));
        }

        var total = yearAmounts.Sum();
        var result = new SuggestedKpiDto(
            kpi.Id, kpi.Code, kpi.Name, kpi.Unit, kpi.AggregationType.ToEnumString(), cells, lockedUnits,
            deptYear.TargetValue, total == deptYear.TargetValue, null, warnings);

        return new SuggestTargetsResponseDto(3, request.Basis, request.Overwrite, [result], []);
    }
}
