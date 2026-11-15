using G54.DAL.Entities;

namespace G54.BLL.Common.Workflow;

/// <summary>The metrics of an evaluation a tier criterion is checked against; a metric that is not known is null.</summary>
public sealed record TierMetrics(
    decimal? TotalScore,
    decimal? BusinessScore,
    decimal? ServiceQualityScore,
    decimal? OperationsScore,
    int OpenRiskEvents);

/// <summary>A tier criterion read from the database and the tiers for which it must be met.</summary>
public sealed record TierCriterionRule(
    string Code,
    string Metric,
    decimal Threshold,
    IReadOnlyCollection<string> RequiredForTiers);

/// <summary>The ranking settings read from the database: tiers best first, the downgrade, and the tier criteria matrix.</summary>
public sealed record DowngradeConfiguration(
    IReadOnlyList<RankingTierThreshold> Tiers,
    RiskEventSeverity DowngradeTriggerSeverity,
    string DowngradeCapTierCode,
    IReadOnlyList<TierCriterionRule>? Criteria = null);

/// <summary>
/// Decides a tier. Holds no figure of its own: thresholds, the downgrade severity and the downgrade cap tier all
/// come from <see cref="DowngradeConfiguration"/>.
/// </summary>
public static class TierDeterminer
{
    /// <summary>
    /// The tier a total score reaches, never above the downgrade cap tier when the employee has a risk event of at least the
    /// configured severity that was not dismissed. The result is the lower of the score tier and the cap tier. When
    /// <paramref name="metrics"/> is given, the tier is also lowered until every criterion required for it is met.
    /// </summary>
    public static string DetermineTier(
        decimal totalScore,
        IReadOnlyCollection<RiskEvent>? riskEvents,
        DowngradeConfiguration configuration,
        TierMetrics? metrics = null)
    {
        var tiers = configuration.Tiers;
        if (tiers.Count == 0)
        {
            throw new InvalidOperationException("Chưa cấu hình các hạng xếp loại (ranking_tier_threshold).");
        }

        var scoreTier = tiers.FirstOrDefault(t => totalScore >= t.MinScore) ?? tiers[^1];
        if (metrics is not null)
        {
            scoreTier = tiers.Where(t => t.RankOrder >= scoreTier.RankOrder)
                .FirstOrDefault(t => MeetsRequiredCriteria(t.TierCode, configuration.Criteria, metrics)) ?? tiers[^1];
        }

        if (!HasDowngradeRiskEvent(riskEvents, configuration.DowngradeTriggerSeverity))
        {
            return scoreTier.TierCode;
        }

        var capTier = tiers.FirstOrDefault(t => string.Equals(t.TierCode, configuration.DowngradeCapTierCode, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Hạng trần '{configuration.DowngradeCapTierCode}' của cấu hình hạ hạng không có trong ranking_tier_threshold.");

        return capTier.RankOrder > scoreTier.RankOrder ? capTier.TierCode : scoreTier.TierCode;
    }

    private static bool MeetsRequiredCriteria(string tierCode, IReadOnlyList<TierCriterionRule>? criteria, TierMetrics metrics) =>
        criteria is null
        || criteria.Where(c => c.RequiredForTiers.Contains(tierCode)).All(c => IsMet(c, metrics));

    /// <summary>A score criterion is met at or above its threshold, the risk-event count at or below it; an unknown metric is not met.</summary>
    private static bool IsMet(TierCriterionRule criterion, TierMetrics metrics) => criterion.Metric switch
    {
        TierCriterionMetrics.TotalScore => metrics.TotalScore >= criterion.Threshold,
        TierCriterionMetrics.BusinessScore => metrics.BusinessScore >= criterion.Threshold,
        TierCriterionMetrics.ServiceQualityScore => metrics.ServiceQualityScore >= criterion.Threshold,
        TierCriterionMetrics.OperationsScore => metrics.OperationsScore >= criterion.Threshold,
        TierCriterionMetrics.OpenRiskEvents => metrics.OpenRiskEvents <= criterion.Threshold,
        _ => false,
    };

    private static bool HasDowngradeRiskEvent(IReadOnlyCollection<RiskEvent>? riskEvents, RiskEventSeverity triggerSeverity) =>
        riskEvents is not null
        && riskEvents.Any(e => e.Status != RiskEventStatus.Dismissed && e.Severity >= triggerSeverity);
}

/// <summary>The metrics a tier criterion can be built on.</summary>
public static class TierCriterionMetrics
{
    public const string TotalScore = "TOTAL_SCORE";
    public const string BusinessScore = "BUSINESS_SCORE";
    public const string ServiceQualityScore = "SERVICE_QUALITY_SCORE";
    public const string OperationsScore = "OPERATIONS_SCORE";
    public const string OpenRiskEvents = "OPEN_RISK_EVENTS";

    public static readonly IReadOnlyList<string> All =
        [TotalScore, BusinessScore, ServiceQualityScore, OperationsScore, OpenRiskEvents];
}
