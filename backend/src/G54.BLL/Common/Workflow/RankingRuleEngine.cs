using G54.BLL.Dtos.SystemParameter;
using G54.DAL.Entities;

namespace G54.BLL.Common.Workflow;

public sealed record RankingSubjectInput(
    Guid EmployeeId,
    string EmployeeCode,
    string JobPositionCode,
    decimal? TotalScore,
    decimal? WeightedScore = null,
    decimal? QuantitativeScore = null,
    decimal? QualitativeScore = null,
    decimal? BusinessScore = null,
    decimal? ServiceQualityScore = null,
    decimal? OperationsScore = null,
    decimal RiskPenalty = 0m,
    decimal? ScoreChangeVsPrev = null,
    decimal? DepartmentAvgScore = null,
    IReadOnlyCollection<RiskEvent>? RiskEvents = null);

public sealed record RankingSubjectOutput(
    Guid EmployeeId,
    decimal? EffectiveScore,
    int? Rank,
    decimal? Percentile,
    string? BaseTier,
    string? ProposedTier,
    bool CoachingFlag,
    string? WarningLabel,
    IReadOnlyList<string> MatchedRules,
    IReadOnlyList<string> Explanation);

/// <summary>
/// Executes a <see cref="RankingPolicyDefinitionDto"/> (formula + ordered tiers + tier criteria + visual IF/AND/OR
/// automation rules + configurable tie-breakers) over a roster of employees in pure memory (GB-910).
/// </summary>
public static class RankingRuleEngine
{
    public const int MaxConditionNestingDepth = 3;
    public const int MaxRulesCount = 50;

    public static RankingPolicyDefinitionDto DefaultDefinition() => new(
        ScoreFormula: "TotalScore",
        Tiers:
        [
            new("A1", 1, 95m),
            new("A2", 2, 90m),
            new("A3", 3, 85m),
            new("B", 4, 70m),
            new("C", 5, 0m),
        ],
        TieBreakers:
        [
            "EFFECTIVE_SCORE_DESC",
            "BUSINESS_SCORE_DESC",
            "SERVICE_QUALITY_SCORE_DESC",
            "EMPLOYEE_CODE_ASC",
        ],
        Rules:
        [
            new(
                Id: "rule-critical-risk-cap",
                Name: "Hạ trần tối đa hạng B khi có sự kiện rủi ro Nghiêm trọng",
                Enabled: true,
                StopOnMatch: false,
                Conditions: new(
                    "AND",
                    [new("MAX_RISK_SEVERITY", "GTE", 4m)],
                    []),
                Actions:
                [
                    new("CAP_MAX_TIER", TierCode: "B", Message: "Hạ trần tối đa hạng B do phát sinh sự kiện rủi ro mức CRITICAL"),
                ]),
            new(
                Id: "rule-low-tier-coaching",
                Name: "Tự động gắn cờ Kèm cặp (Coaching) cho hạng C trở xuống",
                Enabled: true,
                StopOnMatch: false,
                Conditions: new(
                    "AND",
                    [new("CURRENT_TIER", "IN", Values: ["C", "D"])],
                    []),
                Actions:
                [
                    new("SET_COACHING_FLAG", FlagValue: true, Message: "Đề xuất kèm cặp do xếp hạng từ C trở xuống"),
                ]),
        ]);

    public static IReadOnlyList<string> ValidateDefinition(RankingPolicyDefinitionDto? definition)
    {
        if (definition is null)
        {
            return ["Thiếu định nghĩa chính sách xếp hạng (definition)."];
        }

        var errors = new List<string>();
        errors.AddRange(RankingFormulaEvaluator.Validate(definition.ScoreFormula));

        if (definition.Tiers is not { Count: >= 2 and <= 10 })
        {
            errors.Add("Danh sách hạng (tiers) phải có từ 2 đến 10 bậc.");
            return errors;
        }

        var orderedTiers = definition.Tiers.OrderBy(t => t.RankOrder).ToList();
        var tierCodes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < orderedTiers.Count; i++)
        {
            var tier = orderedTiers[i];
            var code = tier.TierCode?.Trim();
            if (string.IsNullOrEmpty(code) || code.Length > 10)
            {
                errors.Add("Mã hạng bắt buộc và tối đa 10 ký tự.");
                continue;
            }

            if (!tierCodes.Add(code))
            {
                errors.Add($"Mã hạng '{code}' bị trùng.");
            }

            if (tier.RankOrder != i + 1)
            {
                errors.Add("Thứ tự hạng (rankOrder) phải liên tiếp bắt đầu từ 1.");
            }

            if (tier.MinScore is < 0m or > 100m)
            {
                errors.Add($"Điểm sàn của hạng '{code}' phải từ 0 đến 100.");
            }

            if (i > 0 && tier.MinScore >= orderedTiers[i - 1].MinScore)
            {
                errors.Add($"Điểm sàn của hạng '{code}' ({tier.MinScore}) phải nhỏ hơn hạng đứng trên '{orderedTiers[i - 1].TierCode}' ({orderedTiers[i - 1].MinScore}).");
            }
        }

        if (definition.TieBreakers is null || definition.TieBreakers.Count == 0)
        {
            errors.Add("Cần chọn ít nhất một tiêu chí ưu tiên phá đồng điểm (tieBreakers).");
        }
        else if (definition.TieBreakers.Any(tb => !RankingRuleCatalog.TieBreakerFields.Contains(tb)))
        {
            errors.Add($"Tie-breaker chỉ chấp nhận các trường: {string.Join(", ", RankingRuleCatalog.TieBreakerFields)}.");
        }

        if (definition.Rules is { Count: > MaxRulesCount })
        {
            errors.Add($"Tối đa {MaxRulesCount} quy tắc tự động hóa.");
        }

        foreach (var rule in definition.Rules ?? [])
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Trim().Length > 200)
            {
                errors.Add("Mỗi quy tắc phải có tên (tối đa 200 ký tự).");
                continue;
            }

            ValidateConditionGroup(rule.Conditions, rule.Name, 1, errors);

            if (rule.Actions is not { Count: > 0 })
            {
                errors.Add($"Quy tắc '{rule.Name}' cần ít nhất một hành động THEN.");
                continue;
            }

            foreach (var action in rule.Actions)
            {
                ValidateAction(action, rule.Name, tierCodes, errors);
            }
        }

        return errors;
    }

    public static IReadOnlyDictionary<Guid, RankingSubjectOutput> EvaluateRoster(
        IReadOnlyList<RankingSubjectInput> subjects,
        RankingPolicyDefinitionDto policy,
        IReadOnlyList<TierCriterionRule>? tierCriteria = null)
    {
        if (subjects.Count == 0)
        {
            return new Dictionary<Guid, RankingSubjectOutput>();
        }

        var orderedTiers = policy.Tiers.OrderBy(t => t.RankOrder).ToList();
        var tierIndexByCode = orderedTiers
            .Select((t, idx) => (t.TierCode, idx))
            .ToDictionary(x => x.TierCode, x => x.idx, StringComparer.OrdinalIgnoreCase);

        var states = new List<SubjectWorkingState>(subjects.Count);
        var defaultDeptAvg = subjects.Where(s => s.TotalScore.HasValue).Select(s => s.TotalScore!.Value).DefaultIfEmpty(100m).Average();

        foreach (var subject in subjects)
        {
            if (!subject.TotalScore.HasValue)
            {
                states.Add(new SubjectWorkingState(subject) { EffectiveScore = null });
                continue;
            }

            var activeRisks = (subject.RiskEvents ?? []).Where(r => r.Status != RiskEventStatus.Dismissed).ToList();
            var criticalCount = activeRisks.Count(r => r.Severity == RiskEventSeverity.Critical);
            var highCount = activeRisks.Count(r => r.Severity >= RiskEventSeverity.High);
            var vars = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["TotalScore"] = subject.TotalScore.Value,
                ["WeightedScore"] = subject.WeightedScore ?? subject.TotalScore.Value,
                ["QuantitativeScore"] = subject.QuantitativeScore ?? subject.TotalScore.Value,
                ["QualitativeScore"] = subject.QualitativeScore ?? subject.TotalScore.Value,
                ["BusinessScore"] = subject.BusinessScore ?? subject.TotalScore.Value,
                ["ServiceQualityScore"] = subject.ServiceQualityScore ?? subject.TotalScore.Value,
                ["OperationsScore"] = subject.OperationsScore ?? subject.TotalScore.Value,
                ["RiskPenalty"] = subject.RiskPenalty,
                ["ScoreChangeVsPrev"] = subject.ScoreChangeVsPrev ?? 0m,
                ["OpenRiskEvents"] = activeRisks.Count,
                ["CriticalRiskCount"] = criticalCount,
                ["HighRiskCount"] = highCount,
                ["DepartmentAvgScore"] = subject.DepartmentAvgScore ?? defaultDeptAvg,
            };

            var effective = RankingFormulaEvaluator.Evaluate(policy.ScoreFormula, vars, subject.TotalScore.Value);
            var state = new SubjectWorkingState(subject)
            {
                EffectiveScore = effective,
                ActiveRisks = activeRisks,
                MaxSeverityLevel = activeRisks.Count > 0 ? activeRisks.Max(r => SeverityToNumber(r.Severity)) : 0,
                CriticalRiskCount = criticalCount,
                HighOrAboveRiskCount = highCount,
            };

            if (Math.Round(subject.TotalScore.Value, 2) != effective)
            {
                state.Explanation.Add($"Công thức điểm xét hạng: {subject.TotalScore.Value:0.##} → {effective:0.##} ({policy.ScoreFormula})");
            }

            var baseTier = orderedTiers.FirstOrDefault(t => effective >= t.MinScore) ?? orderedTiers[^1];
            state.BaseTier = baseTier.TierCode;
            state.CurrentTierIndex = tierIndexByCode[baseTier.TierCode];
            state.Explanation.Add($"Điểm xét hạng {effective:0.##} đạt ngưỡng hạng gốc {baseTier.TierCode} (≥ {baseTier.MinScore:0.##})");

            if (tierCriteria is { Count: > 0 })
            {
                var metrics = new TierMetrics(
                    effective,
                    subject.BusinessScore ?? effective,
                    subject.ServiceQualityScore ?? effective,
                    subject.OperationsScore ?? effective,
                    activeRisks.Count);
                while (state.CurrentTierIndex < orderedTiers.Count - 1
                    && !MeetsTierCriteria(orderedTiers[state.CurrentTierIndex].TierCode, tierCriteria, metrics))
                {
                    state.CurrentTierIndex++;
                }

                if (orderedTiers[state.CurrentTierIndex].TierCode != baseTier.TierCode)
                {
                    state.Explanation.Add($"Điều chỉnh xuống {orderedTiers[state.CurrentTierIndex].TierCode} do chưa đạt ma trận điều kiện bắt buộc của hạng trên");
                }
            }

            states.Add(state);
        }

        // Sort scorable subjects using configurable tie-breakers
        var scoredStates = states.Where(s => s.EffectiveScore.HasValue).ToList();
        scoredStates.Sort((a, b) => CompareByTieBreakers(a, b, policy.TieBreakers));

        var groupSize = scoredStates.Count;
        for (var i = 0; i < groupSize; i++)
        {
            var s = scoredStates[i];
            s.Rank = i + 1;
            s.Percentile = Math.Round((i + 1) * 100m / groupSize, 2);
            s.GroupSize = groupSize;
        }

        // Execute visual automation rules
        foreach (var rule in (policy.Rules ?? []).Where(r => r.Enabled))
        {
            foreach (var state in scoredStates.Where(s => !s.Stopped))
            {
                if (!MatchesGroup(state, orderedTiers, rule.Conditions))
                {
                    continue;
                }

                var appliedAny = ApplyActions(state, rule, orderedTiers, tierIndexByCode);
                if (appliedAny)
                {
                    state.MatchedRules.Add(rule.Name);
                }

                if (rule.StopOnMatch)
                {
                    state.Stopped = true;
                }
            }
        }

        var outputs = new Dictionary<Guid, RankingSubjectOutput>(states.Count);
        foreach (var state in states)
        {
            var finalTier = state.EffectiveScore.HasValue ? orderedTiers[state.CurrentTierIndex].TierCode : null;
            if (finalTier is not null)
            {
                state.Explanation.Add($"Gợi ý cuối cùng: {finalTier}{(state.CoachingFlag ? " · Kèm cặp" : string.Empty)}");
            }

            outputs[state.Input.EmployeeId] = new RankingSubjectOutput(
                state.Input.EmployeeId,
                state.EffectiveScore,
                state.Rank,
                state.Percentile,
                state.BaseTier,
                finalTier,
                state.CoachingFlag,
                state.WarningLabel,
                state.MatchedRules,
                state.Explanation);
        }

        return outputs;
    }

    private static bool ApplyActions(
        SubjectWorkingState state,
        RankingAutomationRuleDto rule,
        List<RankingTierDefinitionDto> orderedTiers,
        Dictionary<string, int> tierIndexByCode)
    {
        var touched = false;
        foreach (var action in rule.Actions)
        {
            var beforeIdx = state.CurrentTierIndex;
            switch (action.Type)
            {
                case "CAP_MAX_TIER":
                    if (action.TierCode is not null && tierIndexByCode.TryGetValue(action.TierCode, out var capIdx) && capIdx > state.CurrentTierIndex)
                    {
                        state.CurrentTierIndex = capIdx;
                        state.Explanation.Add(action.Message ?? $"Rule [{rule.Name}]: Hạ trần tối đa xuống hạng {orderedTiers[capIdx].TierCode}");
                        touched = true;
                    }

                    break;

                case "SET_MIN_TIER":
                    if (action.TierCode is not null && tierIndexByCode.TryGetValue(action.TierCode, out var floorIdx) && floorIdx < state.CurrentTierIndex)
                    {
                        state.CurrentTierIndex = floorIdx;
                        state.Explanation.Add(action.Message ?? $"Rule [{rule.Name}]: Nâng tối thiểu lên hạng {orderedTiers[floorIdx].TierCode}");
                        touched = true;
                    }

                    break;

                case "SET_EXACT_TIER":
                    if (action.TierCode is not null && tierIndexByCode.TryGetValue(action.TierCode, out var exactIdx) && exactIdx != state.CurrentTierIndex)
                    {
                        state.CurrentTierIndex = exactIdx;
                        state.Explanation.Add(action.Message ?? $"Rule [{rule.Name}]: Gán trực tiếp hạng {orderedTiers[exactIdx].TierCode}");
                        touched = true;
                    }

                    break;

                case "DEMOTE_STEPS":
                    {
                        var steps = Math.Max(1, (int)(action.NumericValue ?? 1m));
                        var nextIdx = Math.Min(orderedTiers.Count - 1, state.CurrentTierIndex + steps);
                        if (nextIdx != beforeIdx)
                        {
                            state.CurrentTierIndex = nextIdx;
                            state.Explanation.Add(action.Message ?? $"Rule [{rule.Name}]: Hạ {steps} bậc xuống hạng {orderedTiers[nextIdx].TierCode}");
                            touched = true;
                        }

                        break;
                    }

                case "PROMOTE_STEPS":
                    {
                        var steps = Math.Max(1, (int)(action.NumericValue ?? 1m));
                        var nextIdx = Math.Max(0, state.CurrentTierIndex - steps);
                        if (nextIdx != beforeIdx)
                        {
                            state.CurrentTierIndex = nextIdx;
                            state.Explanation.Add(action.Message ?? $"Rule [{rule.Name}]: Nâng {steps} bậc lên hạng {orderedTiers[nextIdx].TierCode}");
                            touched = true;
                        }

                        break;
                    }

                case "ENFORCE_GROUP_QUOTA_PERCENT":
                    {
                        var quotaPct = Math.Clamp(action.NumericValue ?? 20m, 1m, 100m);
                        var maxSlots = state.GroupSize > 0 ? Math.Max(1, (int)Math.Ceiling(state.GroupSize * quotaPct / 100m)) : 0;
                        if (state.Rank.HasValue
                            && state.Rank.Value > maxSlots
                            && action.TierCode is not null
                            && tierIndexByCode.TryGetValue(action.TierCode, out var fallbackIdx)
                            && fallbackIdx > state.CurrentTierIndex)
                        {
                            state.CurrentTierIndex = fallbackIdx;
                            state.Explanation.Add(action.Message ?? $"Rule [{rule.Name}]: Hạng #{state.Rank}/{state.GroupSize} vượt hạn ngạch {quotaPct:0.#}% ({maxSlots} suất) → chuyển xuống {orderedTiers[fallbackIdx].TierCode}");
                            touched = true;
                        }

                        break;
                    }

                case "SET_COACHING_FLAG":
                    {
                        var nextFlag = action.FlagValue ?? true;
                        if (state.CoachingFlag != nextFlag)
                        {
                            state.CoachingFlag = nextFlag;
                            if (!string.IsNullOrWhiteSpace(action.Message))
                            {
                                state.Explanation.Add(action.Message!);
                            }

                            touched = true;
                        }

                        break;
                    }

                case "SET_WARNING_LABEL":
                    if (!string.IsNullOrWhiteSpace(action.Message))
                    {
                        state.WarningLabel = action.Message.Trim();
                        touched = true;
                    }

                    break;
            }
        }

        return touched;
    }

    private static bool MatchesGroup(
        SubjectWorkingState state,
        List<RankingTierDefinitionDto> orderedTiers,
        RankingRuleConditionGroupDto? group)
    {
        if (group is null)
        {
            return true;
        }

        var results = new List<bool>();
        foreach (var item in group.Items ?? [])
        {
            results.Add(MatchesItem(state, orderedTiers, item));
        }

        foreach (var child in group.Groups ?? [])
        {
            results.Add(MatchesGroup(state, orderedTiers, child));
        }

        if (results.Count == 0)
        {
            return true;
        }

        return string.Equals(group.LogicalOperator, "OR", StringComparison.OrdinalIgnoreCase)
            ? results.Any(r => r)
            : results.All(r => r);
    }

    private static bool MatchesItem(
        SubjectWorkingState state,
        List<RankingTierDefinitionDto> orderedTiers,
        RankingRuleConditionItemDto item)
    {
        if (RankingRuleCatalog.StringMetrics.Contains(item.Metric))
        {
            var currentStr = item.Metric == "CURRENT_TIER"
                ? orderedTiers[state.CurrentTierIndex].TierCode
                : state.Input.JobPositionCode;

            return item.Operator switch
            {
                "IN" => (item.Values ?? []).Any(v => string.Equals(v?.Trim(), currentStr, StringComparison.OrdinalIgnoreCase)),
                "EQ" => item.Values is { Count: > 0 } && string.Equals(item.Values[0]?.Trim(), currentStr, StringComparison.OrdinalIgnoreCase),
                "NEQ" => (item.Values ?? []).All(v => !string.Equals(v?.Trim(), currentStr, StringComparison.OrdinalIgnoreCase)),
                _ => false,
            };
        }

        var metricVal = ResolveNumericMetric(state, item.Metric);
        if (!metricVal.HasValue)
        {
            return false;
        }

        var v = metricVal.Value;
        var target = item.Value ?? 0m;
        return item.Operator switch
        {
            "GTE" => v >= target,
            "GT" => v > target,
            "LTE" => v <= target,
            "LT" => v < target,
            "EQ" => v == target,
            "NEQ" => v != target,
            "BETWEEN" => item.SecondaryValue.HasValue
                && v >= Math.Min(target, item.SecondaryValue.Value)
                && v <= Math.Max(target, item.SecondaryValue.Value),
            _ => false,
        };
    }

    private static decimal? ResolveNumericMetric(SubjectWorkingState state, string metric) => metric switch
    {
        "TOTAL_SCORE" => state.Input.TotalScore,
        "EFFECTIVE_SCORE" => state.EffectiveScore,
        "QUANTITATIVE_SCORE" => state.Input.QuantitativeScore ?? state.Input.TotalScore,
        "QUALITATIVE_SCORE" => state.Input.QualitativeScore ?? state.Input.TotalScore,
        "BUSINESS_SCORE" => state.Input.BusinessScore ?? state.Input.TotalScore,
        "SERVICE_QUALITY_SCORE" => state.Input.ServiceQualityScore ?? state.Input.TotalScore,
        "OPERATIONS_SCORE" => state.Input.OperationsScore ?? state.Input.TotalScore,
        "MIN_COMPONENT_SCORE" => MinComponent(state.Input),
        "SCORE_CHANGE_VS_PREV" => state.Input.ScoreChangeVsPrev ?? 0m,
        "OPEN_RISK_EVENTS" => state.ActiveRisks.Count,
        "MAX_RISK_SEVERITY" => state.MaxSeverityLevel,
        "CRITICAL_RISK_COUNT" => state.CriticalRiskCount,
        "HIGH_OR_ABOVE_RISK_COUNT" => state.HighOrAboveRiskCount,
        "PERCENTILE_IN_GROUP" => state.Percentile,
        "RANK_IN_GROUP" => state.Rank,
        "GROUP_SIZE" => state.GroupSize,
        _ => null,
    };

    private static decimal? MinComponent(RankingSubjectInput input)
    {
        var values = new[] { input.BusinessScore, input.ServiceQualityScore, input.OperationsScore, input.QualitativeScore }
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();
        return values.Count > 0 ? values.Min() : input.TotalScore;
    }

    private static int CompareByTieBreakers(SubjectWorkingState a, SubjectWorkingState b, IReadOnlyList<string>? tieBreakers)
    {
        foreach (var field in tieBreakers ?? RankingRuleCatalog.TieBreakerFields)
        {
            var cmp = field switch
            {
                "EFFECTIVE_SCORE_DESC" => Nullable.Compare(b.EffectiveScore, a.EffectiveScore),
                "TOTAL_SCORE_DESC" => Nullable.Compare(b.Input.TotalScore, a.Input.TotalScore),
                "BUSINESS_SCORE_DESC" => Nullable.Compare(b.Input.BusinessScore, a.Input.BusinessScore),
                "SERVICE_QUALITY_SCORE_DESC" => Nullable.Compare(b.Input.ServiceQualityScore, a.Input.ServiceQualityScore),
                "OPERATIONS_SCORE_DESC" => Nullable.Compare(b.Input.OperationsScore, a.Input.OperationsScore),
                "QUALITATIVE_SCORE_DESC" => Nullable.Compare(b.Input.QualitativeScore, a.Input.QualitativeScore),
                "RISK_PENALTY_ASC" => a.Input.RiskPenalty.CompareTo(b.Input.RiskPenalty),
                "EMPLOYEE_CODE_ASC" => string.Compare(a.Input.EmployeeCode, b.Input.EmployeeCode, StringComparison.OrdinalIgnoreCase),
                _ => 0,
            };
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return string.Compare(a.Input.EmployeeCode, b.Input.EmployeeCode, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MeetsTierCriteria(string tierCode, IReadOnlyList<TierCriterionRule> criteria, TierMetrics metrics) =>
        criteria.Where(c => c.RequiredForTiers.Contains(tierCode)).All(c => c.Metric switch
        {
            TierCriterionMetrics.TotalScore => metrics.TotalScore >= c.Threshold,
            TierCriterionMetrics.BusinessScore => metrics.BusinessScore >= c.Threshold,
            TierCriterionMetrics.ServiceQualityScore => metrics.ServiceQualityScore >= c.Threshold,
            TierCriterionMetrics.OperationsScore => metrics.OperationsScore >= c.Threshold,
            TierCriterionMetrics.OpenRiskEvents => metrics.OpenRiskEvents <= c.Threshold,
            _ => false,
        });

    private static int SeverityToNumber(RiskEventSeverity severity) => severity switch
    {
        RiskEventSeverity.Low => 1,
        RiskEventSeverity.Medium => 2,
        RiskEventSeverity.High => 3,
        RiskEventSeverity.Critical => 4,
        _ => 0,
    };

    private static void ValidateConditionGroup(
        RankingRuleConditionGroupDto? group,
        string ruleName,
        int depth,
        List<string> errors)
    {
        if (group is null)
        {
            errors.Add($"Quy tắc '{ruleName}' thiếu nhóm điều kiện (conditions).");
            return;
        }

        if (depth > MaxConditionNestingDepth)
        {
            errors.Add($"Quy tắc '{ruleName}' lồng nhóm điều kiện vượt quá {MaxConditionNestingDepth} cấp.");
            return;
        }

        if (group.LogicalOperator is not ("AND" or "OR"))
        {
            errors.Add($"Quy tắc '{ruleName}' có toán tử kết hợp không hợp lệ '{group.LogicalOperator}' (chỉ hỗ trợ AND hoặc OR).");
        }

        foreach (var item in group.Items ?? [])
        {
            if (item is null || !RankingRuleCatalog.Metrics.Contains(item.Metric))
            {
                errors.Add($"Quy tắc '{ruleName}' dùng chỉ số điều kiện không hợp lệ '{item?.Metric}'.");
                continue;
            }

            if (!RankingRuleCatalog.Operators.Contains(item.Operator))
            {
                errors.Add($"Quy tắc '{ruleName}' dùng toán tử so sánh không hợp lệ '{item.Operator}'.");
                continue;
            }

            if (RankingRuleCatalog.StringMetrics.Contains(item.Metric))
            {
                if (item.Operator is not ("IN" or "EQ" or "NEQ") || item.Values is not { Count: > 0 })
                {
                    errors.Add($"Chỉ số chuỗi '{item.Metric}' trong quy tắc '{ruleName}' chỉ dùng toán tử IN/EQ/NEQ kèm danh sách giá trị (values).");
                }
            }
            else if (item.Operator == "BETWEEN")
            {
                if (!item.Value.HasValue || !item.SecondaryValue.HasValue)
                {
                    errors.Add($"Toán tử BETWEEN trong quy tắc '{ruleName}' cần đủ cả value và secondaryValue.");
                }
            }
            else if (!item.Value.HasValue)
            {
                errors.Add($"Điều kiện '{item.Metric}' trong quy tắc '{ruleName}' thiếu ngưỡng so sánh (value).");
            }
        }

        foreach (var child in group.Groups ?? [])
        {
            ValidateConditionGroup(child, ruleName, depth + 1, errors);
        }
    }

    private static void ValidateAction(
        RankingRuleActionDto? action,
        string ruleName,
        HashSet<string> tierCodes,
        List<string> errors)
    {
        if (action is null || !RankingRuleCatalog.ActionTypes.Contains(action.Type))
        {
            errors.Add($"Quy tắc '{ruleName}' có hành động không hợp lệ '{action?.Type}'.");
            return;
        }

        switch (action.Type)
        {
            case "CAP_MAX_TIER" or "SET_MIN_TIER" or "SET_EXACT_TIER":
                if (string.IsNullOrWhiteSpace(action.TierCode) || !tierCodes.Contains(action.TierCode.Trim()))
                {
                    errors.Add($"Hành động {action.Type} trong quy tắc '{ruleName}' phải chọn hạng hợp lệ trong danh sách tiers.");
                }

                break;

            case "ENFORCE_GROUP_QUOTA_PERCENT":
                if (string.IsNullOrWhiteSpace(action.TierCode) || !tierCodes.Contains(action.TierCode.Trim()))
                {
                    errors.Add($"Hành động ENFORCE_GROUP_QUOTA_PERCENT trong quy tắc '{ruleName}' cần chọn hạng hạ xuống (tierCode) hợp lệ.");
                }

                if (action.NumericValue is null or <= 0m or > 100m)
                {
                    errors.Add($"Hành động ENFORCE_GROUP_QUOTA_PERCENT trong quy tắc '{ruleName}' cần tỷ lệ hạn ngạch từ 1 đến 100%.");
                }

                break;

            case "DEMOTE_STEPS" or "PROMOTE_STEPS":
                if (action.NumericValue is null or < 1m or > 10m)
                {
                    errors.Add($"Hành động {action.Type} trong quy tắc '{ruleName}' cần số bậc từ 1 đến 10.");
                }

                break;

            case "SET_WARNING_LABEL":
                if (string.IsNullOrWhiteSpace(action.Message) || action.Message.Trim().Length > 200)
                {
                    errors.Add($"Hành động SET_WARNING_LABEL trong quy tắc '{ruleName}' cần nội dung nhãn cảnh báo (tối đa 200 ký tự).");
                }

                break;
        }
    }

    private sealed class SubjectWorkingState(RankingSubjectInput input)
    {
        public RankingSubjectInput Input { get; } = input;
        public decimal? EffectiveScore { get; set; }
        public string? BaseTier { get; set; }
        public int CurrentTierIndex { get; set; }
        public int? Rank { get; set; }
        public decimal? Percentile { get; set; }
        public int GroupSize { get; set; }
        public List<RiskEvent> ActiveRisks { get; set; } = [];
        public int MaxSeverityLevel { get; set; }
        public int CriticalRiskCount { get; set; }
        public int HighOrAboveRiskCount { get; set; }
        public bool CoachingFlag { get; set; }
        public string? WarningLabel { get; set; }
        public bool Stopped { get; set; }
        public List<string> MatchedRules { get; } = [];
        public List<string> Explanation { get; } = [];
    }
}
