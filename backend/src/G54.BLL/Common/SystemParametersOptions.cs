namespace G54.BLL.Common;

/// <summary>Operating limits bound from the "SystemParameters" section of appsettings.</summary>
public sealed class SystemParametersOptions
{
    public const string SectionName = "SystemParameters";

    public int ImportMaxFileMb { get; set; } = 5;
    public int AppealRejectedHours { get; set; } = 48;
}
