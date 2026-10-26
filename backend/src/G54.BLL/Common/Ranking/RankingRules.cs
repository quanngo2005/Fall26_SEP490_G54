using G54.DAL.Entities;

namespace G54.BLL.Common.Ranking;

/// <summary>The ranking settings read from the database: tiers best first, and the downrank.</summary>
public sealed record RankingConfiguration(
    IReadOnlyList<RankingTierThreshold> Tiers,
    RiskEventSeverity DownrankTriggerSeverity,
    string DownrankCapTierCode);

/// <summary>
/// Decides a ranking tier. Holds no figure of its own: thresholds, the downrank severity and the downrank cap tier all
/// come from <see cref="RankingConfiguration"/>.
/// </summary>
public static class RankingRules
{
    /// <summary>
    /// The tier a total score reaches, never above the downrank cap tier when the employee has a risk event of at least the
    /// configured severity that was not dismissed. The result is the lower of the score tier and the cap tier.
    /// </summary>
    public static string DetermineTier(
        decimal totalScore,
        IReadOnlyCollection<RiskEvent>? riskEvents,
        RankingConfiguration configuration)
    {
        var tiers = configuration.Tiers;
        if (tiers.Count == 0)
        {
            throw new InvalidOperationException("Chưa cấu hình các hạng xếp loại (ranking_tier_threshold).");
        }

        var scoreTier = tiers.FirstOrDefault(t => totalScore >= t.MinScore) ?? tiers[^1];
        if (!HasDownrankRiskEvent(riskEvents, configuration.DownrankTriggerSeverity))
        {
            return scoreTier.TierCode;
        }

        var capTier = tiers.FirstOrDefault(t => string.Equals(t.TierCode, configuration.DownrankCapTierCode, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Hạng trần '{configuration.DownrankCapTierCode}' của cấu hình hạ hạng không có trong ranking_tier_threshold.");

        return capTier.RankOrder > scoreTier.RankOrder ? capTier.TierCode : scoreTier.TierCode;
    }

    private static bool HasDownrankRiskEvent(IReadOnlyCollection<RiskEvent>? riskEvents, RiskEventSeverity triggerSeverity) =>
        riskEvents is not null
        && riskEvents.Any(e => e.Status != RiskEventStatus.Dismissed && e.Severity >= triggerSeverity);
}
