using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>
/// KPI score = achievement percent of the actual against the target (capped by the KPI maximum).
/// Group score = mean of its scored parts (KPI leaves and sub-groups) weighted by their weight among siblings,
/// normalised over the parts that have a score. Total score = sum of root group scores weighted by the profile.
/// </summary>
public sealed class PerformanceScoreFormula : IPerformanceScoreFormula
{
    /// <summary>Largest percent the achievement columns can store (numeric(10,4)).</summary>
    private const decimal MaxStoredPercent = 999999.9999m;

    /// <summary>Achievement of a lower-is-better KPI whose actual is zero and that has no maximum.</summary>
    private const decimal ZeroActualPercent = 100m;

    public KpiScoreResult CalculateKpi(KpiScoreInput input)
    {
        var actual = ResolveActual(input);
        var threshold = ResolveThreshold(input);
        if (actual is null || threshold is null)
        {
            return new KpiScoreResult(null, null, null);
        }

        if (input.Direction != KpiDirection.LowerBetter && threshold.Value <= 0m)
        {
            return new KpiScoreResult(null, null, null);
        }

        if (input.Direction == KpiDirection.LowerBetter && threshold.Value < 0m)
        {
            return new KpiScoreResult(null, null, null);
        }

        var raw = Math.Min(Math.Max(AchievementPercent(input, actual.Value, threshold.Value), 0m), MaxStoredPercent);
        var capped = input.MaxAchievementPercent is { } cap ? Math.Min(raw, cap) : raw;

        return new KpiScoreResult(raw, capped, capped);
    }

    public decimal? CalculateGroup(GroupScoreInput input) =>
        EvaluationProfileTree.WeightedMean(input.Parts.Select(p => (p.WeightPercent, p.Score)));

    public decimal? CalculateTotal(IReadOnlyList<GroupScoreEntry> groups)
    {
        var scored = groups.Where(g => g.ComponentScore.HasValue).ToList();

        return scored.Count == 0
            ? null
            : scored.Sum(g => g.ComponentScore!.Value * g.WeightPercent / 100m);
    }

    /// <summary>The value compared with the target: a ratio KPI divides by its denominator, others use the sum or mean.</summary>
    public static decimal? ResolveActual(
        KpiAggregationType aggregationType,
        string unit,
        decimal? actualValue,
        decimal? actualDenominator)
    {
        if (!actualValue.HasValue)
        {
            return null;
        }

        if (aggregationType != KpiAggregationType.Ratio)
        {
            return actualValue.Value;
        }

        if (actualDenominator is not > 0m)
        {
            return null;
        }

        var ratio = actualValue.Value / actualDenominator.Value;
        return unit.Trim() == "%" ? ratio * 100m : ratio;
    }

    private static decimal? ResolveActual(KpiScoreInput input) =>
        ResolveActual(input.AggregationType, input.Unit, input.ActualValue, input.ActualDenominator);

    private static decimal? ResolveThreshold(KpiScoreInput input) =>
        input.TargetValue is > 0m ? input.TargetValue : input.BaselineValue;

    private static decimal AchievementPercent(KpiScoreInput input, decimal actual, decimal threshold)
    {
        switch (input.Direction)
        {
            case KpiDirection.HigherBetter:
                return actual / threshold * 100m;
            case KpiDirection.LowerBetter:
                var ceiling = input.MaxAchievementPercent ?? ZeroActualPercent;
                if (actual <= threshold)
                {
                    return ceiling;
                }

                return threshold == 0m || input.Unit.Trim() == "%"
                    ? ceiling - (actual - threshold)
                    : threshold / actual * 100m;
            default:
                return 100m - Math.Abs(actual - threshold) / Math.Abs(threshold) * 100m;
        }
    }
}
