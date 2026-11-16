namespace G54.BLL.Dtos.SystemParameter;

public static class RankingRuleCatalog
{
    public static readonly IReadOnlyList<string> FormulaVariables =
    [
        "TotalScore",
        "WeightedScore",
        "QuantitativeScore",
        "QualitativeScore",
        "BusinessScore",
        "ServiceQualityScore",
        "OperationsScore",
        "RiskPenalty",
        "ScoreChangeVsPrev",
        "OpenRiskEvents",
        "CriticalRiskCount",
        "HighRiskCount",
        "DepartmentAvgScore",
    ];

    public static readonly IReadOnlyList<string> FormulaFunctions =
        ["MIN", "MAX", "ROUND", "ABS", "CLAMP", "IF"];

    public static readonly IReadOnlyList<string> Metrics =
    [
        "TOTAL_SCORE",
        "EFFECTIVE_SCORE",
        "QUANTITATIVE_SCORE",
        "QUALITATIVE_SCORE",
        "BUSINESS_SCORE",
        "SERVICE_QUALITY_SCORE",
        "OPERATIONS_SCORE",
        "MIN_COMPONENT_SCORE",
        "SCORE_CHANGE_VS_PREV",
        "OPEN_RISK_EVENTS",
        "MAX_RISK_SEVERITY",
        "CRITICAL_RISK_COUNT",
        "HIGH_OR_ABOVE_RISK_COUNT",
        "PERCENTILE_IN_GROUP",
        "RANK_IN_GROUP",
        "GROUP_SIZE",
        "CURRENT_TIER",
        "JOB_POSITION_CODE",
    ];

    public static readonly IReadOnlyList<string> StringMetrics =
        ["CURRENT_TIER", "JOB_POSITION_CODE"];

    public static readonly IReadOnlyList<string> Operators =
        ["GTE", "GT", "LTE", "LT", "EQ", "NEQ", "BETWEEN", "IN"];

    public static readonly IReadOnlyList<string> ActionTypes =
    [
        "CAP_MAX_TIER",
        "SET_MIN_TIER",
        "SET_EXACT_TIER",
        "DEMOTE_STEPS",
        "PROMOTE_STEPS",
        "ENFORCE_GROUP_QUOTA_PERCENT",
        "SET_COACHING_FLAG",
        "SET_WARNING_LABEL",
    ];

    public static readonly IReadOnlyList<string> TieBreakerFields =
    [
        "EFFECTIVE_SCORE_DESC",
        "TOTAL_SCORE_DESC",
        "BUSINESS_SCORE_DESC",
        "SERVICE_QUALITY_SCORE_DESC",
        "OPERATIONS_SCORE_DESC",
        "QUALITATIVE_SCORE_DESC",
        "RISK_PENALTY_ASC",
        "EMPLOYEE_CODE_ASC",
    ];

    public static RankingPolicyCatalogDto ToDto() => new(
        FormulaVariables,
        FormulaFunctions,
        Metrics,
        Operators,
        ActionTypes,
        TieBreakerFields);
}

public sealed record RankingTierDefinitionDto(
    string TierCode,
    int RankOrder,
    decimal MinScore);

public sealed record RankingRuleConditionItemDto(
    string Metric,
    string Operator,
    decimal? Value = null,
    decimal? SecondaryValue = null,
    IReadOnlyList<string>? Values = null);

public sealed record RankingRuleConditionGroupDto(
    string LogicalOperator,
    IReadOnlyList<RankingRuleConditionItemDto> Items,
    IReadOnlyList<RankingRuleConditionGroupDto>? Groups = null);

public sealed record RankingRuleActionDto(
    string Type,
    string? TierCode = null,
    decimal? NumericValue = null,
    bool? FlagValue = null,
    string? Message = null);

public sealed record RankingAutomationRuleDto(
    string Id,
    string Name,
    bool Enabled,
    bool StopOnMatch,
    RankingRuleConditionGroupDto Conditions,
    IReadOnlyList<RankingRuleActionDto> Actions);

public sealed record RankingPolicyDefinitionDto(
    string ScoreFormula,
    IReadOnlyList<RankingTierDefinitionDto> Tiers,
    IReadOnlyList<string> TieBreakers,
    IReadOnlyList<RankingAutomationRuleDto> Rules);

public sealed record RankingPolicyCatalogDto(
    IReadOnlyList<string> FormulaVariables,
    IReadOnlyList<string> FormulaFunctions,
    IReadOnlyList<string> Metrics,
    IReadOnlyList<string> Operators,
    IReadOnlyList<string> ActionTypes,
    IReadOnlyList<string> TieBreakerFields);

public sealed record RankingPolicyDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int Version,
    Guid? EvaluationPeriodId,
    bool HasPeriodOverride,
    bool IsLockedSnapshot,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    RankingPolicyDefinitionDto Definition,
    RankingPolicyCatalogDto Catalog);

public sealed record UpdateRankingPolicyDto(
    string Name,
    string? Description,
    Guid? EvaluationPeriodId,
    RankingPolicyDefinitionDto Definition);

public sealed record SimulateRankingPolicyRequestDto(
    Guid? PeriodId,
    Guid? DepartmentId,
    RankingPolicyDefinitionDto Definition);

public sealed record SimulatedTierDistributionItemDto(
    string TierCode,
    int BeforeCount,
    decimal BeforePercent,
    int AfterCount,
    decimal AfterPercent);

public sealed record SimulatedEmployeeRankingRowDto(
    Guid EmployeeId,
    string EmployeeCode,
    string FullName,
    string JobPositionCode,
    string JobPositionTitle,
    string DepartmentName,
    decimal TotalScore,
    decimal EffectiveScore,
    decimal? BusinessScore,
    decimal? ServiceQualityScore,
    decimal? OperationsScore,
    decimal? QualitativeScore,
    int OpenRiskEvents,
    string MaxRiskSeverity,
    int BeforeRank,
    int AfterRank,
    string BeforeTier,
    string AfterTier,
    bool Changed,
    bool CoachingFlag,
    string? WarningLabel,
    IReadOnlyList<string> MatchedRules,
    IReadOnlyList<string> Explanation);

public sealed record SimulateRankingPolicyResultDto(
    Guid? PeriodId,
    string PeriodLabel,
    bool IsSyntheticSample,
    int TotalEmployees,
    int ChangedCount,
    IReadOnlyList<SimulatedTierDistributionItemDto> TierSummary,
    IReadOnlyList<SimulatedEmployeeRankingRowDto> Rows);
