namespace G54.DAL.Entities;

/// <summary>
/// The downgrade: a risk event of at least <see cref="TriggerSeverity"/> stops the employee from being placed
/// above <see cref="CapTierCode"/>, whatever the score. The table holds a single row.
/// </summary>
public partial class Downgrade
{
    public Guid Id { get; set; }

    public RiskEventSeverity TriggerSeverity { get; set; }

    public string CapTierCode { get; set; } = null!;

    public DateTimeOffset UpdatedAt { get; set; }
}
