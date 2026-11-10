namespace G54.DAL.Entities;

/// <summary>An administrator-managed key/value setting.</summary>
public partial class SystemSetting
{
    public string Key { get; set; } = null!;

    public string Value { get; set; } = null!;

    public DateTimeOffset UpdatedAt { get; set; }
}

