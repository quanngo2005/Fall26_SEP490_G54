using G54.BLL.Common.Workflow;
using G54.DAL.Entities;
using Xunit;

namespace G54.UnitTests;

public sealed class TierDeterminerTests
{
    private static RiskEvent Risk(RiskEventSeverity severity, RiskEventStatus status = RiskEventStatus.Confirmed) => new()
    {
        Id = Guid.NewGuid(),
        Severity = severity,
        Status = status,
    };

    [Theory]
    [InlineData(100, "A1")]
    [InlineData(95, "A1")]
    [InlineData(94.99, "A2")]
    [InlineData(90, "A2")]
    [InlineData(85, "A3")]
    [InlineData(84.99, "B")]
    [InlineData(70, "B")]
    [InlineData(69.99, "C")]
    [InlineData(0, "C")]
    public void WithoutRiskEvents_TheTierFollowsTheScoreThresholds(double score, string expected)
    {
        var tier = TierDeterminer.DetermineTier((decimal)score, null, DowngradeTestData.Configuration());

        Assert.Equal(expected, tier);
    }

    [Theory]
    [InlineData(100, "B")]
    [InlineData(95, "B")]
    [InlineData(85, "B")]
    [InlineData(70, "B")]
    [InlineData(69.99, "C")]
    [InlineData(10, "C")]
    public void ASevereRiskEvent_CapsTheTierWhateverTheScore(double score, string expected)
    {
        var tier = TierDeterminer.DetermineTier(
            (decimal)score, [Risk(RiskEventSeverity.Critical)], DowngradeTestData.Configuration());

        Assert.Equal(expected, tier);
    }

    [Fact]
    public void ADismissedSevereRiskEvent_DoesNotCapTheTier()
    {
        var tier = TierDeterminer.DetermineTier(
            98m, [Risk(RiskEventSeverity.Critical, RiskEventStatus.Dismissed)], DowngradeTestData.Configuration());

        Assert.Equal("A1", tier);
    }

    [Theory]
    [InlineData(RiskEventSeverity.Low)]
    [InlineData(RiskEventSeverity.Medium)]
    [InlineData(RiskEventSeverity.High)]
    public void ARiskEventBelowTheTriggerSeverity_DoesNotCapTheTier(RiskEventSeverity severity)
    {
        var tier = TierDeterminer.DetermineTier(98m, [Risk(severity)], DowngradeTestData.Configuration());

        Assert.Equal("A1", tier);
    }

    [Fact]
    public void AnyOneSevereRiskEventAmongOthers_CapsTheTier()
    {
        var tier = TierDeterminer.DetermineTier(
            98m,
            [Risk(RiskEventSeverity.Low), Risk(RiskEventSeverity.Critical), Risk(RiskEventSeverity.Medium)],
            DowngradeTestData.Configuration());

        Assert.Equal("B", tier);
    }

    [Fact]
    public void AnEmptyRiskEventList_DoesNotCapTheTier()
    {
        var tier = TierDeterminer.DetermineTier(98m, [], DowngradeTestData.Configuration());

        Assert.Equal("A1", tier);
    }

    [Fact]
    public void TheCapNeverRaisesATierThatIsAlreadyLower()
    {
        var tier = TierDeterminer.DetermineTier(
            30m, [Risk(RiskEventSeverity.Critical)], DowngradeTestData.Configuration());

        Assert.Equal("C", tier);
    }

    // ----- Values read from the configuration, different from the defaults ---------------------

    [Fact]
    public void ThresholdsComeFromTheConfiguration()
    {
        (string, int, decimal)[] tiers = [("GOLD", 1, 50m), ("SILVER", 2, 20m), ("BRONZE", 3, 0m)];
        var configuration = DowngradeTestData.Configuration(tiers, downgradeCapTier: "SILVER");

        Assert.Equal("GOLD", TierDeterminer.DetermineTier(50m, null, configuration));
        Assert.Equal("SILVER", TierDeterminer.DetermineTier(49.99m, null, configuration));
        Assert.Equal("BRONZE", TierDeterminer.DetermineTier(19.99m, null, configuration));
    }

    [Fact]
    public void TheCapTierComesFromTheConfiguration()
    {
        var configuration = DowngradeTestData.Configuration(downgradeCapTier: "A3");

        var tier = TierDeterminer.DetermineTier(98m, [Risk(RiskEventSeverity.Critical)], configuration);

        Assert.Equal("A3", tier);
    }

    [Fact]
    public void TheTriggerSeverityComesFromTheConfiguration()
    {
        var configuration = DowngradeTestData.Configuration(downgradeSeverity: RiskEventSeverity.High);

        Assert.Equal("B", TierDeterminer.DetermineTier(98m, [Risk(RiskEventSeverity.High)], configuration));
        Assert.Equal("B", TierDeterminer.DetermineTier(98m, [Risk(RiskEventSeverity.Critical)], configuration));
        Assert.Equal("A1", TierDeterminer.DetermineTier(98m, [Risk(RiskEventSeverity.Medium)], configuration));
    }

    [Fact]
    public void ThresholdsOfAnotherSize_StillResolveByRankOrder()
    {
        (string, int, decimal)[] tiers = [("A", 1, 80m), ("B", 2, 40m), ("C", 3, 0m)];
        var configuration = DowngradeTestData.Configuration(tiers, downgradeCapTier: "B");

        Assert.Equal("A", TierDeterminer.DetermineTier(90m, null, configuration));
        Assert.Equal("B", TierDeterminer.DetermineTier(90m, [Risk(RiskEventSeverity.Critical)], configuration));
        Assert.Equal("C", TierDeterminer.DetermineTier(10m, [Risk(RiskEventSeverity.Critical)], configuration));
    }

    [Fact]
    public void WithoutAnyTier_TheRuleReportsAMissingConfigurationInsteadOfGuessing()
    {
        var configuration = new DowngradeConfiguration([], RiskEventSeverity.Critical, "B");

        Assert.Throws<InvalidOperationException>(() => TierDeterminer.DetermineTier(90m, null, configuration));
    }

    [Fact]
    public void ACapTierMissingFromTheTiers_IsReportedOnlyWhenTheDowngradeApplies()
    {
        var configuration = DowngradeTestData.Configuration(downgradeCapTier: "NOPE");

        Assert.Equal("A1", TierDeterminer.DetermineTier(98m, null, configuration));
        Assert.Throws<InvalidOperationException>(() =>
            TierDeterminer.DetermineTier(98m, [Risk(RiskEventSeverity.Critical)], configuration));
    }
}
