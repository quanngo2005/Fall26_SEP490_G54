using System.Globalization;
using G54.BLL.Common;
using G54.BLL.Dtos.SystemParameter;

namespace G54.BLL.Services;

/// <summary>
/// Metadata and typed parser/applier for one SCR-ADM-04 system parameter (GB-910).
/// </summary>
public sealed record SystemParameterDefinition(
    string Key,
    string Group,
    string ValueType,
    decimal? Min,
    decimal? Max,
    Func<SystemParametersOptions, object> Get,
    Action<SystemParametersOptions, string>? ApplyText)
{
    public const decimal HardMax = 1_000_000_000_000m;

    public bool Locked => ApplyText is null;

    public bool TryValidateAndFormat(object? rawValue, out string formattedText, out string? error)
    {
        formattedText = string.Empty;
        error = null;

        if (Locked)
        {
            error = $"Tham số '{Key}' được cố định theo quy định kỹ thuật, không thể thay đổi.";
            return false;
        }

        if (rawValue is null)
        {
            error = $"Tham số '{Key}' không được để trống.";
            return false;
        }

        switch (ValueType)
        {
            case SystemParameterValueTypes.Integer:
                if (!TryReadDecimal(rawValue, out var intDec) || intDec != decimal.Truncate(intDec))
                {
                    error = BuildRangeError(wholeNumber: true);
                    return false;
                }

                if (!InBounds(intDec))
                {
                    error = BuildRangeError(wholeNumber: true);
                    return false;
                }

                formattedText = decimal.Truncate(intDec).ToString(CultureInfo.InvariantCulture);
                return true;

            case SystemParameterValueTypes.Decimal:
            case SystemParameterValueTypes.Percent:
                if (!TryReadDecimal(rawValue, out var decVal) || decimal.Round(decVal, 4) != decVal || !InBounds(decVal))
                {
                    error = BuildRangeError(wholeNumber: false);
                    return false;
                }

                formattedText = decVal.ToString("0.####", CultureInfo.InvariantCulture);
                return true;

            case SystemParameterValueTypes.Time:
                if (rawValue is not string timeStr
                    || !TimeOnly.TryParseExact(timeStr.Trim(), ["HH:mm:ss", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime))
                {
                    error = $"Tham số '{Key}' phải đúng định dạng giờ HH:mm:ss (00:00:00 đến 23:59:59).";
                    return false;
                }

                formattedText = parsedTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                return true;

            case SystemParameterValueTypes.Boolean:
                if (!TryReadBool(rawValue, out var boolVal))
                {
                    error = $"Tham số '{Key}' phải có giá trị true hoặc false.";
                    return false;
                }

                formattedText = boolVal ? "true" : "false";
                return true;

            default:
                error = $"Kiểu dữ liệu của tham số '{Key}' không hỗ trợ chỉnh sửa.";
                return false;
        }
    }

    public bool TryValidateStoredText(string? text, out string normalizedText)
    {
        normalizedText = string.Empty;
        if (Locked || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        object candidate = ValueType switch
        {
            SystemParameterValueTypes.Boolean when bool.TryParse(text.Trim(), out var b) => b,
            SystemParameterValueTypes.Time => text.Trim(),
            _ when decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            _ => string.Empty,
        };

        if (candidate is string s && s.Length == 0 && ValueType != SystemParameterValueTypes.Time)
        {
            return false;
        }

        return TryValidateAndFormat(candidate, out normalizedText, out _);
    }

    private bool InBounds(decimal value)
        => value >= (Min ?? 0m) && value <= (Max ?? HardMax);

    private string BuildRangeError(bool wholeNumber)
    {
        var kind = wholeNumber ? "số nguyên" : "số hợp lệ (tối đa 4 chữ số thập phân)";
        return Max is null
            ? $"Tham số '{Key}' phải là {kind} từ {Min ?? 1m:0.####} trở lên."
            : $"Tham số '{Key}' phải là {kind} từ {Min ?? 0m:0.####} đến {Max:0.####}.";
    }

    private static bool TryReadDecimal(object raw, out decimal value)
    {
        switch (raw)
        {
            case decimal d:
                value = d;
                return true;
            case int i:
                value = i;
                return true;
            case long l:
                value = l;
                return true;
            case double db when !double.IsNaN(db) && !double.IsInfinity(db):
                value = (decimal)db;
                return true;
            case float f when !float.IsNaN(f) && !float.IsInfinity(f):
                value = (decimal)f;
                return true;
            case string s when decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed):
                value = parsed;
                return true;
            default:
                value = 0m;
                return false;
        }
    }

    private static bool TryReadBool(object raw, out bool value)
    {
        switch (raw)
        {
            case bool b:
                value = b;
                return true;
            case string s when bool.TryParse(s.Trim(), out var parsed):
                value = parsed;
                return true;
            default:
                value = false;
                return false;
        }
    }
}

/// <summary>Single source of truth for all system parameters exposed by SCR-ADM-04 (docs/api/SYSTEM_PARAMETER_CONTRACT.md).</summary>
public static class SystemParameterCatalog
{
    public static readonly IReadOnlyList<SystemParameterDefinition> All =
    [
        // Group 1: DEADLINES
        new("kpiLockTime", SystemParameterGroups.Deadlines, SystemParameterValueTypes.Time, null, null,
            o => o.KpiLockTime, (o, v) => o.KpiLockTime = v),
        Int("preCloseCheckDays", SystemParameterGroups.Deadlines, 1, 30,
            o => o.PreCloseCheckDays, (o, v) => o.PreCloseCheckDays = v),
        Int("preCloseDeadlineDays", SystemParameterGroups.Deadlines, 1, 60,
            o => o.PreCloseDeadlineDays, (o, v) => o.PreCloseDeadlineDays = v),
        Int("preCloseDeadlineHour", SystemParameterGroups.Deadlines, 0, 23,
            o => o.PreCloseDeadlineHour, (o, v) => o.PreCloseDeadlineHour = v),
        Int("manualApprovalHours", SystemParameterGroups.Deadlines, 1, 168,
            o => o.ManualApprovalHours, (o, v) => o.ManualApprovalHours = v),
        Bool("workOverdueEnabled", SystemParameterGroups.Deadlines,
            o => o.WorkOverdueEnabled, (o, v) => o.WorkOverdueEnabled = v),
        Int("workOverdueSweepMinutes", SystemParameterGroups.Deadlines, 1, 1440,
            o => o.WorkOverdueSweepMinutes, (o, v) => o.WorkOverdueSweepMinutes = v),
        Int("campaignExclusivityDays", SystemParameterGroups.Deadlines, 1, 90,
            o => o.CampaignExclusivityDays, (o, v) => o.CampaignExclusivityDays = v),

        // Group 2: APPEALS
        Int("appealRejectedHours", SystemParameterGroups.Appeals, 1, 720,
            o => o.AppealRejectedHours, (o, v) => o.AppealRejectedHours = v),
        Int("appealWindowDays", SystemParameterGroups.Appeals, 1, 30,
            o => o.AppealWindowDays, (o, v) => o.AppealWindowDays = v),
        Int("minReasonLength", SystemParameterGroups.Appeals, 5, 200,
            o => o.MinReasonLength, (o, v) => o.MinReasonLength = v),
        Int("maxEvidenceFiles", SystemParameterGroups.Appeals, 1, 20,
            o => o.MaxEvidenceFiles, (o, v) => o.MaxEvidenceFiles = v),

        // Group 3: SCORING_QA
        Pct("qaSampleRatePercent", SystemParameterGroups.ScoringQa, 0, 100,
            o => o.QaSampleRatePercent, (o, v) => o.QaSampleRatePercent = v),
        Dec("riskPenaltyLow", SystemParameterGroups.ScoringQa, 0, 100,
            o => o.RiskPenaltyLow, (o, v) => o.RiskPenaltyLow = v),
        Dec("riskPenaltyMedium", SystemParameterGroups.ScoringQa, 0, 100,
            o => o.RiskPenaltyMedium, (o, v) => o.RiskPenaltyMedium = v),
        Dec("riskPenaltyHigh", SystemParameterGroups.ScoringQa, 0, 100,
            o => o.RiskPenaltyHigh, (o, v) => o.RiskPenaltyHigh = v),
        Dec("riskPenaltyCritical", SystemParameterGroups.ScoringQa, 0, 100,
            o => o.RiskPenaltyCritical, (o, v) => o.RiskPenaltyCritical = v),
        Pct("unitUnderperformanceThresholdPercent", SystemParameterGroups.ScoringQa, 0, 100,
            o => o.UnitUnderperformanceThresholdPercent, (o, v) => o.UnitUnderperformanceThresholdPercent = v),
        Dec("unitPenaltyMultiplier", SystemParameterGroups.ScoringQa, 0.1m, 1m,
            o => o.UnitPenaltyMultiplier, (o, v) => o.UnitPenaltyMultiplier = v),
        Pct("evalKpiMetThresholdPercent", SystemParameterGroups.ScoringQa, 0, 200,
            o => o.EvalKpiMetThresholdPercent, (o, v) => o.EvalKpiMetThresholdPercent = v),
        Pct("evalKpiNearThresholdPercent", SystemParameterGroups.ScoringQa, 0, 200,
            o => o.EvalKpiNearThresholdPercent, (o, v) => o.EvalKpiNearThresholdPercent = v),
        Int("evalCommentMaxLength", SystemParameterGroups.ScoringQa, 50, 5000,
            o => o.EvalCommentMaxLength, (o, v) => o.EvalCommentMaxLength = v),

        // Group 4: QUOTA_RANKING
        Pct("tierAQuotaPercent", SystemParameterGroups.QuotaRanking, 1, 100,
            o => o.TierAQuotaPercent, (o, v) => o.TierAQuotaPercent = v),
        Pct("tierBTargetPercent", SystemParameterGroups.QuotaRanking, 0, 100,
            o => o.TierBTargetPercent, (o, v) => o.TierBTargetPercent = v),
        Pct("tierCTargetPercent", SystemParameterGroups.QuotaRanking, 0, 100,
            o => o.TierCTargetPercent, (o, v) => o.TierCTargetPercent = v),
        Pct("tierDTargetPercent", SystemParameterGroups.QuotaRanking, 0, 100,
            o => o.TierDTargetPercent, (o, v) => o.TierDTargetPercent = v),
        Int("minDeptSizeForOwnQuota", SystemParameterGroups.QuotaRanking, 1, 50,
            o => o.MinDeptSizeForOwnQuota, (o, v) => o.MinDeptSizeForOwnQuota = v),

        // Group 5: IMPORT_HANDOVER
        Int("importMaxFileMb", SystemParameterGroups.ImportHandover, 1, 50,
            o => o.ImportMaxFileMb, (o, v) => o.ImportMaxFileMb = v),
        Int("targetImportMaxFileMb", SystemParameterGroups.ImportHandover, 1, 50,
            o => o.TargetImportMaxFileMb, (o, v) => o.TargetImportMaxFileMb = v),
        Int("importMaxRows", SystemParameterGroups.ImportHandover, 100, 100000,
            o => o.ImportMaxRows, (o, v) => o.ImportMaxRows = v),
        Int("maxTransactionsPerWork", SystemParameterGroups.ImportHandover, 1, 50,
            o => o.MaxTransactionsPerWork, (o, v) => o.MaxTransactionsPerWork = v),
        DecInt("handoverThresholdVnd", SystemParameterGroups.ImportHandover, 1, null,
            o => o.HandoverAmountThreshold, (o, v) => o.HandoverAmountThreshold = v),
        new("importFormats", SystemParameterGroups.ImportHandover, SystemParameterValueTypes.String, null, null,
            _ => ".xlsx", null),

        // Group 6: DASHBOARD_SECURITY
        Dec("perfAttentionDropPoints", SystemParameterGroups.DashboardSecurity, 0.1m, 50m,
            o => o.PerfAttentionDropPoints, (o, v) => o.PerfAttentionDropPoints = v),
        Dec("perfAttentionLowScore", SystemParameterGroups.DashboardSecurity, 0m, 100m,
            o => o.PerfAttentionLowScore, (o, v) => o.PerfAttentionLowScore = v),
        Int("perfAttentionTopN", SystemParameterGroups.DashboardSecurity, 1, 50,
            o => o.PerfAttentionTopN, (o, v) => o.PerfAttentionTopN = v),
        Dec("perfWeakGroupScore", SystemParameterGroups.DashboardSecurity, 0m, 100m,
            o => o.PerfWeakGroupScore, (o, v) => o.PerfWeakGroupScore = v),
        Int("perfTrendPeriodCount", SystemParameterGroups.DashboardSecurity, 2, 24,
            o => o.PerfTrendPeriodCount, (o, v) => o.PerfTrendPeriodCount = v),
        Int("authMaxFailedAttempts", SystemParameterGroups.DashboardSecurity, 3, 20,
            o => o.AuthMaxFailedAttempts, (o, v) => o.AuthMaxFailedAttempts = v),
        Int("authLockoutMinutes", SystemParameterGroups.DashboardSecurity, 1, 1440,
            o => o.AuthLockoutMinutes, (o, v) => o.AuthLockoutMinutes = v),
        Int("loginRateLimitPerMinute", SystemParameterGroups.DashboardSecurity, 3, 120,
            o => o.LoginRateLimitPerMinute, (o, v) => o.LoginRateLimitPerMinute = v),
    ];

    public static SystemParameterDefinition? Find(string? key)
        => key is null ? null : All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));

    private static SystemParameterDefinition Int(
        string key,
        string group,
        decimal min,
        decimal? max,
        Func<SystemParametersOptions, int> getter,
        Action<SystemParametersOptions, int> setter)
        => new(
            key,
            group,
            SystemParameterValueTypes.Integer,
            min,
            max,
            o => getter(o),
            (o, text) => setter(o, int.Parse(text, CultureInfo.InvariantCulture)));

    private static SystemParameterDefinition DecInt(
        string key,
        string group,
        decimal min,
        decimal? max,
        Func<SystemParametersOptions, decimal> getter,
        Action<SystemParametersOptions, decimal> setter)
        => new(
            key,
            group,
            SystemParameterValueTypes.Integer,
            min,
            max,
            o => getter(o),
            (o, text) => setter(o, decimal.Parse(text, CultureInfo.InvariantCulture)));

    private static SystemParameterDefinition Dec(
        string key,
        string group,
        decimal min,
        decimal? max,
        Func<SystemParametersOptions, decimal> getter,
        Action<SystemParametersOptions, decimal> setter)
        => new(
            key,
            group,
            SystemParameterValueTypes.Decimal,
            min,
            max,
            o => getter(o),
            (o, text) => setter(o, decimal.Parse(text, CultureInfo.InvariantCulture)));

    private static SystemParameterDefinition Pct(
        string key,
        string group,
        decimal min,
        decimal? max,
        Func<SystemParametersOptions, decimal> getter,
        Action<SystemParametersOptions, decimal> setter)
        => new(
            key,
            group,
            SystemParameterValueTypes.Percent,
            min,
            max,
            o => getter(o),
            (o, text) => setter(o, decimal.Parse(text, CultureInfo.InvariantCulture)));

    private static SystemParameterDefinition Bool(
        string key,
        string group,
        Func<SystemParametersOptions, bool> getter,
        Action<SystemParametersOptions, bool> setter)
        => new(
            key,
            group,
            SystemParameterValueTypes.Boolean,
            null,
            null,
            o => getter(o),
            (o, text) => setter(o, bool.Parse(text)));
}
