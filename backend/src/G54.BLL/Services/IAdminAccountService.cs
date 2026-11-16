using G54.BLL.Common.Models;
using G54.BLL.Dtos.Admin;

namespace G54.BLL.Services;

public interface IAdminAccountService
{
    Task<IReadOnlyList<RoleSummaryDto>> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<UserAccountSummaryDto>> GetAccountsAsync(
        UserAccountFilterDto filter,
        CancellationToken cancellationToken = default);

    Task<UserAccountSummaryCountsDto> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EligibleEmployeeDto>> GetEligibleEmployeesAsync(CancellationToken cancellationToken = default);

    Task<UserAccountDetailDto> GetAccountDetailAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CreateUserAccountResultDto> CreateAccountAsync(
        CreateUserAccountRequestDto request,
        CancellationToken cancellationToken = default);

    Task<UserAccountDetailDto> UpdateAccountAsync(
        Guid id,
        UpdateUserAccountRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the password with a new one-time password and signs the account out everywhere.</summary>
    Task<ResetUserPasswordResultDto> ResetPasswordAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Blocks sign-in until unlocked and signs the account out everywhere. An admin cannot lock themselves.</summary>
    Task<UserAccountDetailDto> LockAccountAsync(
        Guid actorAccountId,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<UserAccountDetailDto> UnlockAccountAsync(Guid id, CancellationToken cancellationToken = default);
}
