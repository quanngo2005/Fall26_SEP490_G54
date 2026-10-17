namespace G54.DAL.Entities;

public sealed class SidebarConfiguration : BaseEntity
{
    public string Role { get; set; } = string.Empty;
    public string[] ItemIds { get; set; } = [];
    public int Version { get; set; }
    public string RoutesJson { get; set; } = "{}";
}
