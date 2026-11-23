namespace G54.DAL.Entities;

/// <summary>
/// Admin-editable override of a system parameter. Only the key and the raw value are stored; the catalog
/// (type, range, default) lives in the BLL so an unknown or out-of-range row is never trusted.
/// </summary>
public sealed class SystemParameter : BaseEntity
{
    public string Key { get; set; } = null!;

    public string Value { get; set; } = null!;
}
