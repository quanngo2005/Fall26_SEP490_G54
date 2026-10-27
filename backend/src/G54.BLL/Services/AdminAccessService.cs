using G54.BLL.Common.Exceptions;
using G54.DAL.Uow;

namespace G54.BLL.Services;

public sealed class AdminAccessService(IUnitOfWork uow) : IAdminAccessService
{
    public async Task EnsureAdminAsync(Guid accountId, CancellationToken ct)
    {
        var roleCodes = await uow.AdminAccess.GetActiveRoleCodesAsync(accountId, ct);
        if (roleCodes is null || !roleCodes.Any(code => SidebarCatalog.MapRole(code) == "admin"))
        {
            throw new ForbiddenException();
        }
    }
}
