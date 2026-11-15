using G54.BLL.Common.Workflow;
using G54.DAL.Entities;
using Xunit;

namespace G54.UnitTests;

/// <summary>The tier criteria matrix of <see cref="TierDeterminer"/>: a tier is only reached when every criterion required for it is met.</summary>
public sealed class TierCriteriaDeterminerTests
{
    private static DowngradeConfiguration ConfigWith(params TierCriterionRule[] criteria)
    {
        var plain = DowngradeTestData.Configuration();
        return plain with { Criteria = criteria };
    }

    private static TierMetrics Metrics(
        decimal? total = 100m, decimal? business = 100m, decimal? quality = 100m, decimal? operations = 100m, int openRisks = 0) =>
        new(total, business, quality, operations, openRisks);

    [Fact]
    public void WithoutCriteria_MetricsDoNotChangeTheScoreTier()
    {
        var config = DowngradeTestData.Configuration();

        Assert.Equal("A1", TierDeterminer.DetermineTier(98m, null, config, Metrics(business: 0m)));
    }

    [Fact]
    public void ACriterionThatIsMet_KeepsTheTier()
    {
        var config = ConfigWith(new TierCriterionRule("BUSINESS", TierCriterionMetrics.BusinessScore, 90m, ["A1"]));

        Assert.Equal("A1", TierDeterminer.DetermineTier(98m, null, config, Metrics(business: 90m)));
    }

    [Fact]
    public void ACriterionThatIsNotMet_LowersTheTierUntilNothingRequiredFails()
    {
        // Only A1 requires the business score: A2 has no requirement, so the employee falls one tier.
        var config = ConfigWith(new TierCriterionRule("BUSINESS", TierCriterionMetrics.BusinessScore, 90m, ["A1"]));

        Assert.Equal("A2", TierDeterminer.DetermineTier(98m, null, config, Metrics(business: 89.99m)));
    }

    [Fact]
    public void Criteria_AreOnlyCheckedForTheTiersTheyAreRequiredFor()
    {
        var config = ConfigWith(new TierCriterionRule("QUALITY", TierCriterionMetrics.ServiceQualityScore, 95m, ["A1", "A2"]));

        // Fails for A1 and A2, A3 has no requirement.
        Assert.Equal("A3", TierDeterminer.DetermineTier(98m, null, config, Metrics(quality: 50m)));
    }

    [Fact]
    public void WhenEveryTierFailsItsCriteria_TheLowestTierIsReturned()
    {
        var config = ConfigWith(new TierCriterionRule("TOTAL", TierCriterionMetrics.TotalScore, 101m, ["A1", "A2", "A3", "B", "C"]));

        Assert.Equal("C", TierDeterminer.DetermineTier(98m, null, config, Metrics(total: 98m)));
    }

    [Theory]
    [InlineData(TierCriterionMetrics.TotalScore, 80, 79)]
    [InlineData(TierCriterionMetrics.BusinessScore, 80, 79)]
    [InlineData(TierCriterionMetrics.ServiceQualityScore, 80, 79)]
    [InlineData(TierCriterionMetrics.OperationsScore, 80, 79)]
    public void ScoreCriteria_AreMetAtOrAboveTheThreshold(string metric, double threshold, double below)
    {
        var config = ConfigWith(new TierCriterionRule("C1", metric, (decimal)threshold, ["A1"]));

        Assert.Equal("A1", TierDeterminer.DetermineTier(98m, null, config, WithMetric(metric, (decimal)threshold)));
        Assert.Equal("A2", TierDeterminer.DetermineTier(98m, null, config, WithMetric(metric, (decimal)below)));
    }

    private static TierMetrics WithMetric(string metric, decimal value) => metric switch
    {
        TierCriterionMetrics.TotalScore => Metrics(total: value),
        TierCriterionMetrics.BusinessScore => Metrics(business: value),
        TierCriterionMetrics.ServiceQualityScore => Metrics(quality: value),
        TierCriterionMetrics.OperationsScore => Metrics(operations: value),
        _ => throw new ArgumentOutOfRangeException(nameof(metric)),
    };

    [Fact]
    public void OpenRiskEventCriterion_IsMetAtOrBelowTheThreshold()
    {
        var config = ConfigWith(new TierCriterionRule("RISK", TierCriterionMetrics.OpenRiskEvents, 1m, ["A1"]));

        Assert.Equal("A1", TierDeterminer.DetermineTier(98m, null, config, Metrics(openRisks: 1)));
        Assert.Equal("A2", TierDeterminer.DetermineTier(98m, null, config, Metrics(openRisks: 2)));
    }

    [Fact]
    public void AnUnknownMetricValue_DoesNotMeetAScoreCriterion()
    {
        var config = ConfigWith(new TierCriterionRule("BUSINESS", TierCriterionMetrics.BusinessScore, 0m, ["A1"]));

        Assert.Equal("A2", TierDeterminer.DetermineTier(98m, null, config, Metrics(business: null)));
    }

    [Fact]
    public void ACriterionWithAnUnknownMetricName_IsNeverMet()
    {
        var config = ConfigWith(new TierCriterionRule("WEIRD", "NO_SUCH_METRIC", 0m, ["A1"]));

        Assert.Equal("A2", TierDeterminer.DetermineTier(98m, null, config, Metrics()));
    }

    [Fact]
    public void TheCapStillApplies_OnTopOfTheCriteria()
    {
        var config = ConfigWith(new TierCriterionRule("BUSINESS", TierCriterionMetrics.BusinessScore, 90m, ["A1"]));
        var critical = new RiskEvent { Id = Guid.NewGuid(), Severity = RiskEventSeverity.Critical, Status = RiskEventStatus.Confirmed };

        Assert.Equal("B", TierDeterminer.DetermineTier(98m, [critical], config, Metrics(business: 100m)));
    }

    [Fact]
    public void TheMetricListContainsEveryMetricTheDeterminerUnderstands()
    {
        Assert.Equal(5, TierCriterionMetrics.All.Count);
        Assert.Contains(TierCriterionMetrics.OpenRiskEvents, TierCriterionMetrics.All);
    }
}
