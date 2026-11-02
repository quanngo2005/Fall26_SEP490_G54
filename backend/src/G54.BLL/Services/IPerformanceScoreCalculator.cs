using G54.BLL.Dtos.Evaluation;
using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>
/// The one place that scores employees for an evaluation period: KPI scores from targets and actual progress, group
/// scores through <see cref="IPerformanceScoreFormula"/>, the qualitative scores of groups without a KPI, the risk
/// penalty and the appeal adjustments. It only reads; persisting the outcome is the caller's job
/// (<c>PerformanceEvaluationService.StageAsync</c>), so the evaluation form, the form save and the calculation endpoint all show
/// and store the same numbers.
/// </summary>
public interface IPerformanceScoreCalculator
{
    /// <summary>
    /// Scores every subject in the period with one query per table. Subjects must have distinct employees; the result is
    /// keyed by employee id.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, PerformanceScoreResult>> CalculateAsync(
        IReadOnlyCollection<PerformanceScoreSubject> subjects,
        EvaluationPeriod period,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// An employee to score. <paramref name="Profile"/> must be loaded with its items and their KPI definitions;
/// <paramref name="QualitativeScores"/> are the reviewer's scores keyed by the root group they score.
/// </summary>
public sealed record PerformanceScoreSubject(
    Guid EmployeeId,
    Guid DepartmentId,
    EvaluationProfile Profile,
    IReadOnlyDictionary<Guid, QualitativeScoreInputDto> QualitativeScores);

/// <summary>
/// One active KPI leaf of a group. Target, actual and scores are null when the data to produce them is missing (shown
/// as N/A); in the group score such a KPI counts as 0 and keeps its share.
/// <see cref="SharePercent"/> is its configured share of the root group among the active KPIs (see
/// <see cref="EvaluationProfileTree.LeafShares"/>), whether or not it has a score.
/// </summary>
public sealed record ScoredKpi(
    Guid KpiDefinitionId,
    string Code,
    string Name,
    string Unit,
    decimal ItemWeightPercent,
    decimal SharePercent,
    Guid? TargetAssignmentId,
    decimal? TargetValue,
    int? TargetVersion,
    decimal? ActualValue,
    decimal? ActualDenominator,
    Guid? AnchorProgressId,
    decimal? AchievementPercentRaw,
    decimal? AchievementPercentCapped,
    decimal? ComponentScore)
{
    /// <summary>Both a target and progress exist, so the KPI is recorded in the KPI detail table.</summary>
    public bool HasContribution => TargetAssignmentId.HasValue && ActualValue.HasValue;
}

/// <summary>
/// A root group of the profile tree. An active KPI that cannot be scored (no target or no progress) and a qualitative
/// group the reviewer has not scored yet both count as 0 and keep their weight (awaiting BA confirmation), so every
/// root group has a <see cref="Score"/>.
/// </summary>
public sealed record ScoredGroup(
    Guid EvaluationProfileItemId,
    string Name,
    string CatalogGroupCode,
    decimal WeightPercent,
    bool IsQualitative,
    decimal? QualitativeScore,
    string? QualitativeNotes,
    decimal? Score,
    IReadOnlyList<ScoredKpi> Kpis);

/// <summary>
/// The scored evaluation of one employee. <see cref="WeightedScore"/> is the weighted sum of the root group scores
/// before the risk penalty; <see cref="TotalScore"/> applies the penalty (never below 0) and the appeal adjustments.
/// Both are null only when the profile has no root group. <see cref="QuantitativeScore"/> is the mean of the scores of
/// the groups that own an active KPI, weighted by their weight; it leaves out the qualitative groups, the penalty and
/// the appeal adjustments, and is null when the profile has no such group.
/// </summary>
public sealed record PerformanceScoreResult(
    Guid EmployeeId,
    IReadOnlyList<ScoredGroup> Groups,
    decimal? WeightedScore,
    IReadOnlyList<RiskEvent> RiskEvents,
    decimal RiskPenalty,
    decimal? TotalScore,
    decimal? QuantitativeScore)
{
    /// <summary>
    /// The Business, Service and Operations scores shown on the form and the calibration roster: the score of the root
    /// group whose catalog KPI group code names that component, null when the profile has no such group. The one place
    /// the component of a group is decided, shared by every screen that shows them. The match is on the code text, as it
    /// was before; it is not a configured classification yet (awaiting a configured group type).
    /// </summary>
    public (decimal? Business, decimal? ServiceQuality, decimal? Operations) ComponentScores()
    {
        decimal? business = null;
        decimal? serviceQuality = null;
        decimal? operations = null;

        foreach (var group in Groups)
        {
            var code = group.CatalogGroupCode.ToUpperInvariant();
            if (code.Contains("KD") || code.Contains("SALES") || code.Contains("BUSINESS"))
            {
                business = group.Score;
            }
            else if (code.Contains("CLDV") || code.Contains("SERVICE") || code.Contains("CSAT"))
            {
                serviceQuality = group.Score;
            }
            else if (code.Contains("TT") || code.Contains("OPER") || code.Contains("VAN_HANH"))
            {
                operations = group.Score;
            }
        }

        return (business, serviceQuality, operations);
    }
}
