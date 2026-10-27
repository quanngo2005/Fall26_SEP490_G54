namespace G54.DAL.Entities;

public sealed class SystemParameterChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public Guid ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}
