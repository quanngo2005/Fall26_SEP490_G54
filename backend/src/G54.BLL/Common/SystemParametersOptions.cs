namespace G54.BLL.Common;

/// <summary>Operating limits bound from the "SystemParameters" section of appsettings.</summary>
public sealed class SystemParametersOptions
{
    public const string SectionName = "SystemParameters";

    public int ImportMaxFileMb { get; set; } = 5;
    public int AppealRejectedHours { get; set; } = 48;

    /// <summary>Max data rows accepted by a data-import upload (docs/api/DATA_IMPORT_CONTRACT.md).</summary>
    public int ImportMaxRows { get; set; } = 5000;

    /// <summary>TTL, in minutes, of a data-import preview session kept in Redis.</summary>
    public int ImportPreviewTtlMinutes { get; set; } = 30;
}
