namespace G54.BLL.Common;

/// <summary>Operating limits bound from the "SystemParameters" section of appsettings.</summary>
public sealed class SystemParametersOptions
{
    public const string SectionName = "SystemParameters";

    public int ImportMaxFileMb { get; set; } = 5;
    public int AppealRejectedHours { get; set; } = 48;

    /// <summary>
    /// Highest value of the single qualitative score a reviewer can enter on the evaluation form. No default here: it must
    /// be set in appsettings and is checked at startup.
    /// </summary>
    public decimal QualitativeScoreMax { get; set; }

    /// <summary>
    /// Highest value of the qualitative score a reviewer enters for one criteria group. No default here: it must be set in
    /// appsettings and is checked at startup.
    /// </summary>
    public decimal QualitativeGroupScoreMax { get; set; }

    /// <summary>Max data rows accepted by a data-import upload (docs/api/DATA_IMPORT_CONTRACT.md).</summary>
    public int ImportMaxRows { get; set; } = 5000;
}
