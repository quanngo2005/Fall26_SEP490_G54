using G54.BLL.Services;
using G54.DAL.Entities;

namespace G54.UnitTests;

public sealed class PerformanceScoreFormulaTests
{
    private readonly PerformanceScoreFormula _formula = new();

    private static KpiScoreInput Kpi(
        KpiDirection direction,
        decimal target,
        decimal actual,
        decimal? max = null,
        KpiAggregationType aggregation = KpiAggregationType.Sum,
        decimal? denominator = null,
        string unit = "unit") => new(
            Guid.NewGuid(),
            "KPI",
            unit,
            direction,
            aggregation,
            max,
            target,
            actual,
            denominator);

    // ----- KPI ------------------------------------------------------------------------------

    [Theory]
    [InlineData(100, 40, 40)]
    [InlineData(100, 100, 100)]
    [InlineData(100, 150, 150)]
    [InlineData(80, 100, 125)]
    [InlineData(100, 0, 0)]
    public void CalculateKpi_HigherBetter_IsActualOverTarget(decimal target, decimal actual, decimal expected)
    {
        var result = _formula.CalculateKpi(Kpi(KpiDirection.HigherBetter, target, actual));

        Assert.Equal(expected, result.AchievementPercentRaw);
    }

    [Theory]
    [InlineData(5, 10, 50)]
    [InlineData(5, 5, 100)]
    [InlineData(10, 5, 200)]
    public void CalculateKpi_LowerBetter_IsTargetOverActual(decimal target, decimal actual, decimal expected)
    {
        var result = _formula.CalculateKpi(Kpi(KpiDirection.LowerBetter, target, actual));

        Assert.Equal(expected, result.AchievementPercentRaw);
    }

    [Fact]
    public void CalculateKpi_LowerBetterWithZeroActual_UsesTheMaximumWhenThereIsOne()
    {
        var result = _formula.CalculateKpi(Kpi(KpiDirection.LowerBetter, 5m, 0m, max: 120m));

        Assert.Equal(120m, result.AchievementPercentRaw);
        Assert.Equal(120m, result.AchievementPercentCapped);
    }

    [Fact]
    public void CalculateKpi_LowerBetterWithZeroActualAndNoMaximum_Is100()
    {
        var result = _formula.CalculateKpi(Kpi(KpiDirection.LowerBetter, 5m, 0m));

        Assert.Equal(100m, result.AchievementPercentRaw);
    }

    [Theory]
    [InlineData(100, 100, 100)]
    [InlineData(100, 90, 90)]
    [InlineData(100, 110, 90)]
    [InlineData(100, 300, 0)]
    public void CalculateKpi_OnTarget_FallsAwayFromTheTargetOnBothSides(decimal target, decimal actual, decimal expected)
    {
        var result = _formula.CalculateKpi(Kpi(KpiDirection.OnTarget, target, actual));

        Assert.Equal(expected, result.AchievementPercentRaw);
    }

    [Fact]
    public void CalculateKpi_NeverReturnsANegativeAchievement()
    {
        var result = _formula.CalculateKpi(Kpi(KpiDirection.OnTarget, 10m, 1000m));

        Assert.Equal(0m, result.AchievementPercentRaw);
        Assert.Equal(0m, result.ComponentScore);
    }

    // ----- Cap ------------------------------------------------------------------------------

    [Fact]
    public void CalculateKpi_CapsTheScoreAtTheKpiMaximum_AndKeepsTheRawValue()
    {
        var result = _formula.CalculateKpi(Kpi(KpiDirection.HigherBetter, 100m, 180m, max: 150m));

        Assert.Equal(180m, result.AchievementPercentRaw);
        Assert.Equal(150m, result.AchievementPercentCapped);
        Assert.Equal(150m, result.ComponentScore);
    }

    [Fact]
    public void CalculateKpi_WithoutAMaximum_LeavesTheScoreUncapped()
    {
        var result = _formula.CalculateKpi(Kpi(KpiDirection.HigherBetter, 100m, 180m));

        Assert.Equal(180m, result.AchievementPercentRaw);
        Assert.Equal(180m, result.AchievementPercentCapped);
    }

    [Fact]
    public void CalculateKpi_CappedNeverExceedsRaw()
    {
        var result = _formula.CalculateKpi(Kpi(KpiDirection.HigherBetter, 100m, 40m, max: 150m));

        Assert.Equal(40m, result.AchievementPercentRaw);
        Assert.Equal(40m, result.AchievementPercentCapped);
        Assert.True(result.AchievementPercentCapped <= result.AchievementPercentRaw);
    }

    // ----- Ratio ----------------------------------------------------------------------------

    [Fact]
    public void CalculateKpi_RatioInPercent_ComparesTheScaledRatioWithTheTarget()
    {
        var result = _formula.CalculateKpi(Kpi(
            KpiDirection.HigherBetter, 90m, 45m, aggregation: KpiAggregationType.Ratio, denominator: 50m, unit: "%"));

        Assert.Equal(100m, result.AchievementPercentRaw);
    }

    [Fact]
    public void CalculateKpi_RatioWithoutAPercentUnit_ComparesThePlainRatio()
    {
        var result = _formula.CalculateKpi(Kpi(
            KpiDirection.HigherBetter, 0.5m, 1m, aggregation: KpiAggregationType.Ratio, denominator: 4m));

        Assert.Equal(50m, result.AchievementPercentRaw);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void CalculateKpi_RatioWithoutADenominator_HasNoScore(int? denominator)
    {
        var result = _formula.CalculateKpi(Kpi(
            KpiDirection.HigherBetter, 90m, 45m, aggregation: KpiAggregationType.Ratio, denominator: denominator));

        Assert.Null(result.AchievementPercentRaw);
        Assert.Null(result.AchievementPercentCapped);
        Assert.Null(result.ComponentScore);
    }

    [Fact]
    public void CalculateKpi_WithAZeroTarget_HasNoScore()
    {
        var result = _formula.CalculateKpi(Kpi(KpiDirection.HigherBetter, 0m, 10m));

        Assert.Null(result.ComponentScore);
    }

    // ----- Group ----------------------------------------------------------------------------

    private static GroupScoreInput Group(params decimal?[] scores) => new(
        Guid.NewGuid(),
        "KD",
        70m,
        scores.Select(s => new KpiScoreEntry(Guid.NewGuid(), "K", new KpiScoreResult(s, s, s))).ToList());

    [Fact]
    public void CalculateGroup_IsTheMeanOfTheScoredKpis()
    {
        Assert.Equal(60m, _formula.CalculateGroup(Group(40m, 80m)));
    }

    [Fact]
    public void CalculateGroup_IgnoresKpisWithoutAScore()
    {
        Assert.Equal(80m, _formula.CalculateGroup(Group(null, 80m)));
    }

    [Fact]
    public void CalculateGroup_WithNoScoredKpi_HasNoScore()
    {
        Assert.Null(_formula.CalculateGroup(Group()));
        Assert.Null(_formula.CalculateGroup(Group(null, null)));
    }

    // ----- Total ----------------------------------------------------------------------------

    [Fact]
    public void CalculateTotal_SumsTheGroupScoresWeightedByTheProfile()
    {
        var groups = new List<GroupScoreEntry>
        {
            new(Guid.NewGuid(), 40m, 100m),
            new(Guid.NewGuid(), 30m, 50m),
            new(Guid.NewGuid(), 30m, 80m),
        };

        Assert.Equal(79m, _formula.CalculateTotal(groups));
    }

    [Fact]
    public void CalculateTotal_LeavesOutGroupsWithoutAScore()
    {
        var groups = new List<GroupScoreEntry>
        {
            new(Guid.NewGuid(), 40m, 100m),
            new(Guid.NewGuid(), 60m, null),
        };

        Assert.Equal(40m, _formula.CalculateTotal(groups));
    }

    [Fact]
    public void CalculateTotal_WithNoScoredGroup_HasNoScore()
    {
        Assert.Null(_formula.CalculateTotal([]));
        Assert.Null(_formula.CalculateTotal([new GroupScoreEntry(Guid.NewGuid(), 100m, null)]));
    }
}
