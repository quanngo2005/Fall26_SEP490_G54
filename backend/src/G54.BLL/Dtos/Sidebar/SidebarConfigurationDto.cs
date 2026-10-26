using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.Sidebar;

public sealed record SidebarConfigurationDto(string Role, string[] ItemIds, int Version, IReadOnlyDictionary<string, string>? Routes = null);

public sealed class SaveSidebarConfigurationDto
{
    [Required]
    [MinLength(1)]
    [MaxLength(64)]
    public string[] ItemIds { get; set; } = [];

    [Range(0, int.MaxValue - 1)]
    public int Version { get; set; }
    public Dictionary<string, string>? Routes { get; set; }
}
