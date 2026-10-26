namespace G54.DAL.Entities;

/// <summary>Minimum total score a ranking tier needs. Tiers are ordered best first by <see cref="RankOrder"/>.</summary>
public partial class RankingTierThreshold
{
    public Guid Id { get; set; }

    public string TierCode { get; set; } = null!;

    /// <summary>1 is the best tier; a larger number is a lower tier.</summary>
    public int RankOrder { get; set; }

    public decimal MinScore { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
