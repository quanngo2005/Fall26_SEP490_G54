namespace G54.DAL.Entities;

/// <summary>
/// The ranking veto: a risk event of at least <see cref="TriggerSeverity"/> stops the employee from ranking
/// above <see cref="CapTierCode"/>, whatever the score. The table holds a single row.
/// </summary>
public partial class RankingVetoSetting
{
    public Guid Id { get; set; }

    public RiskEventSeverity TriggerSeverity { get; set; }

    public string CapTierCode { get; set; } = null!;

    public DateTimeOffset UpdatedAt { get; set; }
}
