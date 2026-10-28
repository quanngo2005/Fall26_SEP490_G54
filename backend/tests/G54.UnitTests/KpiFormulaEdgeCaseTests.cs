using G54.BLL.Services;
using G54.DAL.Entities;

namespace G54.UnitTests;

/// <summary>
/// Edge cases of <see cref="PerformanceScoreFormula"/> that <see cref="PerformanceScoreFormulaTests"/> leaves out:
/// the storage clamp, negative targets, ratio KPIs in every direction, and how the cap interacts with the scale.
/// </summary>
public sealed class KpiFormulaEdgeCaseTests
{
    private const decimal MaxStoredPercent = 999999.9999m;

    private readonly PerformanceScoreFormula _formula = new();

    private static KpiScoreInput Kpi(
        KpiDirection direction,
        decimal target,
        decimal actual,
        decimal? max = null,
        KpiAggregationType aggregation = KpiAggregationType.Sum,
        decimal? denominator = null,
        string unit = "unit") => new(
            Guid.NewGuid(), "KPI", unit, direction, aggregation, max, target, actual, denominator);

    // ----- Storage clamp --------------------------------------------------------------------

    [Fact]
    public void CalculateKpi_ClampsAnAbsurdAchievementToWhatTheColumnCanStore()
    {
        var result = _formula.CalculateKpi(Kpi(KpiDirection.HigherBetter, 0.0001m, 1_000_000m));

        Assert.Equal(MaxStoredPercent, result.AchievementPercentRaw);
        Assert.Equal(MaxStoredPercent, result.AchievementPercentCapped);
    }

    [Fact]
    public void CalculateKpi_LeavesAnAchievementJustBelowTheStorageLimitUntouched()
    {
        // 9999.99 / 0.01 * 100 = 99,999,000 would overflow; 9999.99 / 1 * 100 stays well inside the limit.
        var result = _formula.CalculateKpi(Kpi(KpiDirection.HigherBetter, 1m, 9999.99m));

        Assert.Equal(999999m, result.AchievementPercentRaw);
    }

    [Fact]
    public void CalculateKpi_ACapLowerThanTheClampWinsOverTheClamp()
    {
        var result = _formula.CalculateKpi(Kpi(KpiDirection.HigherBetter, 0.0001m, 1_000_000m, max: 150m));

        Assert.Equal(MaxStoredPercent, result.AchievementPercentRaw);
        Assert.Equal(150m, result.AchievementPercentCapped);
        Assert.Equal(150m, result.ComponentScore);
    }

    // ----- Cap boundary ---------------------------------------------------------------------

    [Theory]
    [InlineData(150, 150, 150)] // exactly at the cap
    [InlineData(150, 149.9999, 149.9999)]
    [InlineData(150, 150.0001, 150)]
    [InlineData(0, 100, 0)] // a zero cap is a real cap, not "no cap"
    public void CalculateKpi_AppliesTheCapAtItsBoundary(decimal cap, decimal achievement, decimal expectedCapped)
    {
        // target 100, so the actual equals the achievement percent
        var result = _formula.CalculateKpi(Kpi(KpiDirection.HigherBetter, 100m, achievement, max: cap));

        Assert.Equal(achievement, result.AchievementPercentRaw);
        Assert.Equal(expectedCapped, result.AchievementPercentCapped);
        Assert.Equal(expectedCapped, result.ComponentScore);
    }

    // ----- Direction edge cases -------------------------------------------------------------

    [Fact]
    public void CalculateKpi_OnTargetWithANegativeTarget_MeasuresTheDistanceAgainstItsMagnitude()
    {
        var onTarget = _formula.CalculateKpi(Kpi(KpiDirection.OnTarget, -100m, -100m));
        var tenPercentOff = _formula.CalculateKpi(Kpi(KpiDirection.OnTarget, -100m, -90m));
        var farAway = _formula.CalculateKpi(Kpi(KpiDirection.OnTarget, -100m, 500m));

        Assert.Equal(100m, onTarget.AchievementPercentRaw);
        Assert.Equal(90m, tenPercentOff.AchievementPercentRaw);
        Assert.Equal(0m, farAway.AchievementPercentRaw); // never negative
    }

    [Fact]
    public void CalculateKpi_OnTargetScoresASmallMissBelowAndAboveTheTargetTheSame()
    {
        var below = _formula.CalculateKpi(Kpi(KpiDirection.OnTarget, 200m, 190m));
        var above = _formula.CalculateKpi(Kpi(KpiDirection.OnTarget, 200m, 210m));

        Assert.Equal(95m, below.AchievementPercentRaw);
        Assert.Equal(below.AchievementPercentRaw, above.AchievementPercentRaw);
    }

    [Fact]
    public void CalculateKpi_LowerBetterWithAZeroActualIsLimitedByAMaximumBelow100()
    {
        var result = _formula.CalculateKpi(Kpi(KpiDirection.LowerBetter, 5m, 0m, max: 80m));

        Assert.Equal(80m, result.AchievementPercentRaw);
        Assert.Equal(80m, result.AchievementPercentCapped);
    }

    [Fact]
    public void CalculateKpi_LowerBetterBeatingTheTargetIsCappedButKeepsItsRawValue()
    {
        var result = _formula.CalculateKpi(Kpi(KpiDirection.LowerBetter, 10m, 2m, max: 150m));

        Assert.Equal(500m, result.AchievementPercentRaw);
        Assert.Equal(150m, result.AchievementPercentCapped);
    }

    [Fact]
    public void CalculateKpi_ANegativeActualOnAHigherBetterKpiScoresZeroNotANegativeNumber()
    {
        var result = _formula.CalculateKpi(Kpi(KpiDirection.HigherBetter, 100m, -50m));

        Assert.Equal(0m, result.AchievementPercentRaw);
        Assert.Equal(0m, result.AchievementPercentCapped);
        Assert.Equal(0m, result.ComponentScore);
    }

    // ----- Ratio in every direction ---------------------------------------------------------

    [Fact]
    public void CalculateKpi_RatioInPercentWithLowerBetter_ComparesTheScaledRatioInverted()
    {
        // 5 / 50 = 10 % against a 5 % target: the rate is twice the allowed rate, so half the score.
        var result = _formula.CalculateKpi(Kpi(
            KpiDirection.LowerBetter, 5m, 5m, aggregation: KpiAggregationType.Ratio, denominator: 50m, unit: "%"));

        Assert.Equal(50m, result.AchievementPercentRaw);
    }

    [Fact]
    public void CalculateKpi_RatioInPercentWithOnTarget_MeasuresTheDistanceOfTheScaledRatio()
    {
        // 45 / 50 = 90 % against a 100 % target is 10 points away.
        var result = _formula.CalculateKpi(Kpi(
            KpiDirection.OnTarget, 100m, 45m, aggregation: KpiAggregationType.Ratio, denominator: 50m, unit: "%"));

        Assert.Equal(90m, result.AchievementPercentRaw);
    }

    [Fact]
    public void CalculateKpi_RatioWithAZeroNumerator_ScoresZeroUnlessLowerBetter()
    {
        var higher = _formula.CalculateKpi(Kpi(
            KpiDirection.HigherBetter, 50m, 0m, aggregation: KpiAggregationType.Ratio, denominator: 10m, unit: "%"));
        var lower = _formula.CalculateKpi(Kpi(
            KpiDirection.LowerBetter, 5m, 0m, aggregation: KpiAggregationType.Ratio, denominator: 10m, unit: "%", max: 120m));

        Assert.Equal(0m, higher.AchievementPercentRaw);
        Assert.Equal(120m, lower.AchievementPercentRaw); // a zero rate is the best possible, bounded by the maximum
    }

    [Fact]
    public void CalculateKpi_RatioWithANegativeDenominatorHasNoScore()
    {
        var result = _formula.CalculateKpi(Kpi(
            KpiDirection.HigherBetter, 90m, 45m, aggregation: KpiAggregationType.Ratio, denominator: -50m, unit: "%"));

        Assert.Null(result.ComponentScore);
    }

    [Fact]
    public void CalculateKpi_RatioTreatsAPaddedPercentSignLikeThePercentUnit()
    {
        var result = _formula.CalculateKpi(Kpi(
            KpiDirection.HigherBetter, 90m, 45m, aggregation: KpiAggregationType.Ratio, denominator: 50m, unit: " % "));

        Assert.Equal(100m, result.AchievementPercentRaw);
    }

    [Fact]
    public void CalculateKpi_NonRatioKpisIgnoreADenominator()
    {
        var result = _formula.CalculateKpi(Kpi(
            KpiDirection.HigherBetter, 100m, 50m, aggregation: KpiAggregationType.Sum, denominator: 999m));

        Assert.Equal(50m, result.AchievementPercentRaw);
    }

    // ----- Zero and missing data ------------------------------------------------------------

    [Theory]
    [InlineData(KpiDirection.HigherBetter)]
    [InlineData(KpiDirection.LowerBetter)]
    [InlineData(KpiDirection.OnTarget)]
    public void CalculateKpi_WithAZeroTarget_HasNoScoreInEveryDirection(KpiDirection direction)
    {
        var result = _formula.CalculateKpi(Kpi(direction, 0m, 10m));

        Assert.Null(result.AchievementPercentRaw);
        Assert.Null(result.AchievementPercentCapped);
        Assert.Null(result.ComponentScore);
    }

    // ----- Group and total ------------------------------------------------------------------

    private static GroupScoreInput Group(params decimal?[] scores) => new(
        Guid.NewGuid(),
        "G",
        100m,
        scores.Select(s => new KpiScoreEntry(Guid.NewGuid(), "K", new KpiScoreResult(s, s, s))).ToList());

    [Fact]
    public void CalculateGroup_AZeroScoreCountsAndPullsTheMeanDown()
    {
        // A KPI that scored 0 is different from a KPI without data: it is part of the mean.
        Assert.Equal(50m, _formula.CalculateGroup(Group(0m, 100m)));
    }

    [Fact]
    public void CalculateGroup_WithASingleScoredKpi_IsThatScore()
    {
        Assert.Equal(73.5m, _formula.CalculateGroup(Group(73.5m)));
    }

    [Fact]
    public void CalculateTotal_AGroupThatScoredZeroStillTakesItsShareOfTheWeight()
    {
        var groups = new List<GroupScoreEntry>
        {
            new(Guid.NewGuid(), 50m, 100m),
            new(Guid.NewGuid(), 50m, 0m),
        };

        Assert.Equal(50m, _formula.CalculateTotal(groups));
    }

    [Fact]
    public void CalculateTotal_DoesNotRescaleWhenTheWeightsDoNotAddUpTo100()
    {
        // Weights are the profile's business; the formula only applies them.
        var groups = new List<GroupScoreEntry>
        {
            new(Guid.NewGuid(), 30m, 100m),
            new(Guid.NewGuid(), 30m, 100m),
        };

        Assert.Equal(60m, _formula.CalculateTotal(groups));
    }

    [Fact]
    public void CalculateTotal_KeepsTheDecimalsOfTheWeightedSum()
    {
        var groups = new List<GroupScoreEntry>
        {
            new(Guid.NewGuid(), 33.3333m, 90m),
            new(Guid.NewGuid(), 66.6667m, 60m),
        };

        Assert.Equal(69.99999m, _formula.CalculateTotal(groups)); // 29.99997 + 40.00002
    }
}
