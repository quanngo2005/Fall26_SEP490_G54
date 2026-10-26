using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;

namespace G54.UnitTests;

/// <summary>Seeds the downgrade configuration that the migration seeds in a real database (InMemory runs no migration).</summary>
internal static class DowngradeTestData
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
        RiskEventSeverity downgradeSeverity = RiskEventSeverity.Critical,
        string downgradeCapTier = "B")
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

        context.Downgrades.Add(new Downgrade
        {
            Id = Guid.NewGuid(),
            TriggerSeverity = downgradeSeverity,
            CapTierCode = downgradeCapTier,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
    }

    public static DowngradeConfiguration Configuration(
        (string Code, int Order, decimal MinScore)[]? tiers = null,
        RiskEventSeverity downgradeSeverity = RiskEventSeverity.Critical,
        string downgradeCapTier = "B") =>
        new(
            (tiers ?? StandardTiers)
                .OrderBy(t => t.Order)
                .Select(t => new RankingTierThreshold { TierCode = t.Code, RankOrder = t.Order, MinScore = t.MinScore })
                .ToList(),
            downgradeSeverity,
            downgradeCapTier);
}
