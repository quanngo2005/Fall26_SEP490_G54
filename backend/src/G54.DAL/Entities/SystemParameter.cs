namespace G54.DAL.Entities;

public sealed class SystemParameter : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public long? NumericValue { get; set; }
    public string? TextValue { get; set; }
    public long? MinValue { get; set; }
    public long? MaxValue { get; set; }
    public bool IsLocked { get; set; }
    public int Version { get; set; }
}
