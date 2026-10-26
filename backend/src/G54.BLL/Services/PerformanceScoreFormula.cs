using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>
/// KPI score = achievement percent of the weighted actual against the target (capped by the KPI maximum).
/// Group score = mean of its scored KPIs. Total score = sum of group scores weighted by the profile.
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
        if (actual is null || input.TargetValue == 0m)
        {
            return new KpiScoreResult(null, null, null);
        }

        var raw = Math.Min(Math.Max(AchievementPercent(input, actual.Value), 0m), MaxStoredPercent);
        var capped = input.MaxAchievementPercent is { } cap ? Math.Min(raw, cap) : raw;

        return new KpiScoreResult(raw, capped, capped);
    }

    public decimal? CalculateGroup(GroupScoreInput input)
    {
        var scores = input.Kpis
            .Where(k => k.Score.ComponentScore.HasValue)
            .Select(k => k.Score.ComponentScore!.Value)
            .ToList();

        return scores.Count == 0 ? null : scores.Average();
    }

    public decimal? CalculateTotal(IReadOnlyList<GroupScoreEntry> groups)
    {
        var scored = groups.Where(g => g.ComponentScore.HasValue).ToList();

        return scored.Count == 0
            ? null
            : scored.Sum(g => g.ComponentScore!.Value * g.WeightPercent / 100m);
    }

    /// <summary>The value compared with the target: a ratio KPI divides by its denominator, others use the sum or mean.</summary>
    private static decimal? ResolveActual(KpiScoreInput input)
    {
        if (input.AggregationType != KpiAggregationType.Ratio)
        {
            return input.ActualValue;
        }

        if (input.ActualDenominator is not > 0m)
        {
            return null;
        }

        var ratio = input.ActualValue / input.ActualDenominator.Value;
        return input.Unit.Trim() == "%" ? ratio * 100m : ratio;
    }

    private static decimal AchievementPercent(KpiScoreInput input, decimal actual)
    {
        var target = input.TargetValue;

        switch (input.Direction)
        {
            case KpiDirection.HigherBetter:
                return actual / target * 100m;
            case KpiDirection.LowerBetter:
                return actual == 0m
                    ? input.MaxAchievementPercent ?? ZeroActualPercent
                    : target / actual * 100m;
            default:
                return 100m - Math.Abs(actual - target) / Math.Abs(target) * 100m;
        }
    }
}
