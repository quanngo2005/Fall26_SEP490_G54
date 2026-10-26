using G54.BLL.Common.Ranking;
using G54.DAL;
using G54.DAL.Entities;

namespace G54.UnitTests;

/// <summary>Seeds the ranking configuration that the migration seeds in a real database (InMemory runs no migration).</summary>
internal static class RankingTestData
{
    public static readonly (string Code, int Order, decimal MinScore)[] StandardTiers =
    [
        ("A1", 1, 95m),
        ("A2", 2, 90m),
        ("A3", 3, 85m),
        ("B", 4, 70m),
        ("C", 5, 0m),
    ];

    public static void Seed(
        AppDbContext context,
        (string Code, int Order, decimal MinScore)[]? tiers = null,
        RiskEventSeverity downrankSeverity = RiskEventSeverity.Critical,
        string downrankCapTier = "B")
    {
        foreach (var (code, order, minScore) in tiers ?? StandardTiers)
        {
            context.RankingTierThresholds.Add(new RankingTierThreshold
            {
                Id = Guid.NewGuid(),
                TierCode = code,
                RankOrder = order,
                MinScore = minScore,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        context.RankingDownrankSettings.Add(new RankingDownrankSetting
        {
            Id = Guid.NewGuid(),
            TriggerSeverity = downrankSeverity,
            CapTierCode = downrankCapTier,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
    }

    public static RankingConfiguration Configuration(
        (string Code, int Order, decimal MinScore)[]? tiers = null,
        RiskEventSeverity downrankSeverity = RiskEventSeverity.Critical,
        string downrankCapTier = "B") =>
        new(
            (tiers ?? StandardTiers)
                .OrderBy(t => t.Order)
                .Select(t => new RankingTierThreshold { TierCode = t.Code, RankOrder = t.Order, MinScore = t.MinScore })
                .ToList(),
            downrankSeverity,
            downrankCapTier);
}
