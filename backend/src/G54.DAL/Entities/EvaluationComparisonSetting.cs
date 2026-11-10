namespace G54.DAL.Entities;

/// <summary>
/// The single row that says which earlier period the evaluation screens (EVL-01, EVL-02) compare a period with.
/// It is read on every request, so a change applies at once without restarting the API.
/// </summary>
public sealed class EvaluationComparisonSetting : BaseEntity
{
    /// <summary>One of <c>PREVIOUS_PERIOD</c>, <c>SAME_PERIOD_LAST_YEAR</c> or <c>AVERAGE_LAST_N</c>.</summary>
    public string Baseline { get; set; } = null!;

    /// <summary>How many earlier periods are averaged when <see cref="Baseline"/> is <c>AVERAGE_LAST_N</c>.</summary>
    public int LookbackPeriods { get; set; }
}
