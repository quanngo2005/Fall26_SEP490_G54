namespace G54.BLL.Services;

// Waiting for the KPI score formula to be confirmed. Until then every score returns null,
// so calculation persists the target and actual snapshots without producing any score.
public sealed class PerformanceScoreFormula : IPerformanceScoreFormula
{
    public KpiScoreResult CalculateKpi(KpiScoreInput input) => new(null, null, null);

    public decimal? CalculateGroup(GroupScoreInput input) => null;

    public decimal? CalculateTotal(IReadOnlyList<GroupScoreEntry> groups) => null;
}
