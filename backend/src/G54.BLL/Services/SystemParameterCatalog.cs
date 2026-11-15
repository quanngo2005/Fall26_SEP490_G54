using G54.BLL.Common;

namespace G54.BLL.Services;

/// <summary>
/// One SCR-ADM-04 parameter. Editable parameters are bounded whole numbers applied to <see cref="SystemParametersOptions"/>;
/// locked ones are fixed by a business rule and only reported.
/// </summary>
public sealed record SystemParameterDefinition(
    string Key,
    decimal? Min,
    decimal? Max,
    Func<SystemParametersOptions, object> Get,
    Action<SystemParametersOptions, decimal>? Set)
{
    /// <summary>Upper bound used for validation when <see cref="Max"/> is unbounded in the contract.</summary>
    public const decimal HardMax = 1_000_000_000_000m;

    public bool Locked => Set is null;
}

/// <summary>Single source of truth for the parameters exposed by SCR-ADM-04 (docs/api/SYSTEM_PARAMETER_CONTRACT.md).</summary>
public static class SystemParameterCatalog
{
    public static readonly IReadOnlyList<SystemParameterDefinition> All =
    [
        new("manualApprovalHours", 1, 168, o => o.ManualApprovalHours, (o, v) => o.ManualApprovalHours = (int)v),
        new("importMaxFileMb", 1, 10, o => o.ImportMaxFileMb, (o, v) => o.ImportMaxFileMb = (int)v),
        new("importFormats", null, null, _ => ".xlsx", null),
        new("kpiLockTime", null, null, _ => "23:59:59", null),
        new("appealWindowDays", 1, 30, o => o.AppealWindowDays, (o, v) => o.AppealWindowDays = (int)v),
        new("preCloseCheckDays", 1, 30, o => o.PreCloseCheckDays, (o, v) => o.PreCloseCheckDays = (int)v),
        new("appealRejectedHours", null, null, o => o.AppealRejectedHours, null),
        new("handoverThresholdVnd", 1, null, o => o.HandoverAmountThreshold, (o, v) => o.HandoverAmountThreshold = v),
    ];

    public static SystemParameterDefinition? Find(string key)
        => All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));
}
