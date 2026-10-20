using G54.BLL.Dtos.Sidebar;

namespace G54.BLL.Services;

public interface ISidebarConfigurationService
{
    Task<IReadOnlyList<SidebarConfigurationDto>> GetAsync(Guid accountId, bool allRoles, CancellationToken cancellationToken);
    Task<SidebarConfigurationDto> SaveAsync(Guid accountId, string role, SaveSidebarConfigurationDto request, CancellationToken cancellationToken);
}
