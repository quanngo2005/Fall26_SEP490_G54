namespace G54.BLL.Common;

/// <summary>The earlier period an evaluation is compared with (SCR-ADM-04, <c>evaluation_comparison_setting</c>).</summary>
public static class EvaluationComparisonBaselines
{
    public const string PreviousPeriod = "PREVIOUS_PERIOD";
    public const string SamePeriodLastYear = "SAME_PERIOD_LAST_YEAR";
    public const string AverageLastN = "AVERAGE_LAST_N";

    public const int MinLookback = 2;
    public const int MaxLookback = 12;

    public static readonly IReadOnlyList<string> All = [PreviousPeriod, SamePeriodLastYear, AverageLastN];
}
