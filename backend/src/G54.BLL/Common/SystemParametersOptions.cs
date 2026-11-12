namespace G54.BLL.Common;

/// <summary>
/// Operating limits. Defaults come from the "SystemParameters" section of appsettings; values an administrator
/// saves through SCR-ADM-04 are stored in <c>system_parameters</c> and override them at runtime
/// (see <c>SystemParameterStore</c>).
/// </summary>
public sealed class SystemParametersOptions
{
    public const string SectionName = "SystemParameters";

    public int ImportMaxFileMb { get; set; } = 5;
    public int AppealRejectedHours { get; set; } = 48;

    /// <summary>Max data rows accepted by a data-import upload (docs/api/DATA_IMPORT_CONTRACT.md).</summary>
    public int ImportMaxRows { get; set; } = 5000;

    /// <summary>BR-01: a handover request above this amount is rejected (VND).</summary>
    public decimal HandoverAmountThreshold { get; set; } = 500_000_000m;

    /// <summary>SCR-ADM-04 parameters with no consumer yet: stored and audited, enforced once the feature that uses them exists.</summary>
    public int ManualApprovalHours { get; set; } = 48;

    public int AppealWindowDays { get; set; } = 7;

    public int PreCloseCheckDays { get; set; } = 3;

    public SystemParametersOptions Clone() => (SystemParametersOptions)MemberwiseClone();
    // Performance dashboards SCR-PRF-02/03 (docs/api/DEPARTMENT_CONTRACT.md). Defaults are the proposed values;
    // they move to SystemParameterCatalog once GB-878 lands.

    /// <summary>PERF_ATTENTION_DROP_POINTS: a drop of at least this many points flags an employee (SCORE_DROP).</summary>
    public decimal PerfAttentionDropPoints { get; set; } = 1.5m;

    /// <summary>PERF_ATTENTION_LOW_SCORE: a total score below this flags an employee (LOW_SCORE).</summary>
    public decimal PerfAttentionLowScore { get; set; } = 80m;

    /// <summary>PERF_ATTENTION_TOP_N: max rows of the "employees needing attention" card.</summary>
    public int PerfAttentionTopN { get; set; } = 5;

    /// <summary>PERF_WEAK_GROUP_SCORE: a profile group score below this is weak.</summary>
    public decimal PerfWeakGroupScore { get; set; } = 85m;

    /// <summary>PERF_TREND_PERIOD_COUNT: number of periods in the performance trend.</summary>
    public int PerfTrendPeriodCount { get; set; } = 6;
}
