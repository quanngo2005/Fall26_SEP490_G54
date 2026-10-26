using G54.DAL.Entities;

namespace G54.BLL.Common.Downgrade;

/// <summary>The downgrade settings read from the database: tiers best first, and the downgrade.</summary>
public sealed record DowngradeConfiguration(
    IReadOnlyList<RankingTierThreshold> Tiers,
    RiskEventSeverity DowngradeTriggerSeverity,
    string DowngradeCapTierCode);

/// <summary>
/// Decides a tier. Holds no figure of its own: thresholds, the downgrade severity and the downgrade cap tier all
/// come from <see cref="DowngradeConfiguration"/>.
/// </summary>
public static class DowngradeRules
{
    /// <summary>
    /// The tier a total score reaches, never above the downgrade cap tier when the employee has a risk event of at least the
    /// configured severity that was not dismissed. The result is the lower of the score tier and the cap tier.
    /// </summary>
    public static string DetermineTier(
        decimal totalScore,
        IReadOnlyCollection<RiskEvent>? riskEvents,
        DowngradeConfiguration configuration)
    {
        var tiers = configuration.Tiers;
        if (tiers.Count == 0)
        {
            throw new InvalidOperationException("Chưa cấu hình các hạng xếp loại (ranking_tier_threshold).");
        }

        var scoreTier = tiers.FirstOrDefault(t => totalScore >= t.MinScore) ?? tiers[^1];
        if (!HasDowngradeRiskEvent(riskEvents, configuration.DowngradeTriggerSeverity))
        {
            return scoreTier.TierCode;
        }

        var capTier = tiers.FirstOrDefault(t => string.Equals(t.TierCode, configuration.DowngradeCapTierCode, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Hạng trần '{configuration.DowngradeCapTierCode}' của cấu hình hạ hạng không có trong ranking_tier_threshold.");

        return capTier.RankOrder > scoreTier.RankOrder ? capTier.TierCode : scoreTier.TierCode;
    }

    private static bool HasDowngradeRiskEvent(IReadOnlyCollection<RiskEvent>? riskEvents, RiskEventSeverity triggerSeverity) =>
        riskEvents is not null
        && riskEvents.Any(e => e.Status != RiskEventStatus.Dismissed && e.Severity >= triggerSeverity);
}
