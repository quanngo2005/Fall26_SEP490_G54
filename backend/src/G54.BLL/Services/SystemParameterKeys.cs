namespace G54.BLL.Services;

public static class SystemParameterKeys
{
    public const string ManualApprovalHours = "manualApprovalHours";
    public const string ImportMaxFileMb = "importMaxFileMb";
    public const string ImportFormats = "importFormats";
    public const string KpiLockTime = "kpiLockTime";
    public const string AppealWindowDays = "appealWindowDays";
    public const string PreCloseCheckDays = "preCloseCheckDays";
    public const string AppealRejectedHours = "appealRejectedHours";
    public const string HandoverThresholdVnd = "handoverThresholdVnd";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ManualApprovalHours,
        ImportMaxFileMb,
        ImportFormats,
        KpiLockTime,
        AppealWindowDays,
        PreCloseCheckDays,
        AppealRejectedHours,
        HandoverThresholdVnd,
    };
}
