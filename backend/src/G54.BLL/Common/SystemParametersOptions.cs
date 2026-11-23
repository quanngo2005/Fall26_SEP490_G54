namespace G54.BLL.Common;

/// <summary>
/// Branch-wide operating parameters (SCR-ADM-04 / GB-910). Canonical defaults live here; administrator changes are
/// persisted in <c>system_parameters</c>, synchronized through Redis, and served live via <c>SystemParameterStore</c>.
/// </summary>
public sealed class SystemParametersOptions
{
    public const string SectionName = "SystemParameters";

    // Group 1: Deadlines & Period Lifecycle (DEADLINES)
    public string KpiLockTime { get; set; } = "23:59:59";
    public int PreCloseCheckDays { get; set; } = 3;
    public int PreCloseDeadlineDays { get; set; } = 12;
    public int PreCloseDeadlineHour { get; set; } = 17;
    public int ManualApprovalHours { get; set; } = 48;
    public bool WorkOverdueEnabled { get; set; } = true;
    public int WorkOverdueSweepMinutes { get; set; } = 15;
    public int RankingLockDay { get; set; } = 20;
    public int CampaignExclusivityDays { get; set; } = 7;

    // Group 2: Appeals & Disputes (APPEALS)
    public int AppealRejectedHours { get; set; } = 48;
    public int AppealWindowDays { get; set; } = 7;
    public int MinReasonLength { get; set; } = 10;
    public int MaxEvidenceFiles { get; set; } = 10;

    // Group 3: Scoring, Risk & QA (SCORING_QA)
    public decimal QaSampleRatePercent { get; set; } = 5m;
    public decimal RiskPenaltyLow { get; set; } = 2m;
    public decimal RiskPenaltyMedium { get; set; } = 5m;
    public decimal RiskPenaltyHigh { get; set; } = 10m;
    public decimal RiskPenaltyCritical { get; set; } = 20m;
    public decimal UnitUnderperformanceThresholdPercent { get; set; } = 70m;
    public decimal UnitPenaltyMultiplier { get; set; } = 0.8m;
    public decimal EvalKpiMetThresholdPercent { get; set; } = 100m;
    public decimal EvalKpiNearThresholdPercent { get; set; } = 85m;
    public int EvalCommentMaxLength { get; set; } = 1000;

    // Group 4: Quota & Ranking Distribution (QUOTA_RANKING)
    public decimal TierAQuotaPercent { get; set; } = 20m;
    public decimal TierBTargetPercent { get; set; } = 60m;
    public decimal TierCTargetPercent { get; set; } = 15m;
    public decimal TierDTargetPercent { get; set; } = 5m;
    public int MinDeptSizeForOwnQuota { get; set; } = 5;

    // Group 5: Import, Transactions & Handover (IMPORT_HANDOVER)
    public int ImportMaxFileMb { get; set; } = 5;
    public int TargetImportMaxFileMb { get; set; } = 10;
    public int ImportMaxRows { get; set; } = 5000;
    public int MaxTransactionsPerWork { get; set; } = 10;
    public decimal HandoverAmountThreshold { get; set; } = 500_000_000m;

    // Group 6: Dashboard Alerts & Auth Security (DASHBOARD_SECURITY)
    public decimal PerfAttentionDropPoints { get; set; } = 1.5m;
    public decimal PerfAttentionLowScore { get; set; } = 80m;
    public int PerfAttentionTopN { get; set; } = 5;
    public decimal PerfWeakGroupScore { get; set; } = 85m;
    public int PerfTrendPeriodCount { get; set; } = 6;
    public int AuthMaxFailedAttempts { get; set; } = 5;
    public int AuthLockoutMinutes { get; set; } = 15;
    public int LoginRateLimitPerMinute { get; set; } = 5;

    public TimeOnly ResolveKpiLockTimeOnly() =>
        TimeOnly.TryParseExact(KpiLockTime, "HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : new TimeOnly(23, 59, 59);

    public SystemParametersOptions Clone() => (SystemParametersOptions)MemberwiseClone();
}
