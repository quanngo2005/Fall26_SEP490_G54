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
}
