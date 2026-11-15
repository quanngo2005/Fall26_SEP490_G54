using G54.BLL.Common.Models;
using G54.BLL.Dtos.Admin;

namespace G54.BLL.Services;

public interface IAdminAccountService
{
    Task<IReadOnlyList<RoleSummaryDto>> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<UserAccountSummaryDto>> GetAccountsAsync(
        UserAccountFilterDto filter,
        CancellationToken cancellationToken = default);
}
