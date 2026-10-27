using G54.DAL.Common;
using G54.DAL.Entities;

namespace G54.DAL.Repositories;

/// <summary>Account state filter of SCR-ADM-01; a terminated employee outranks the locked flag.</summary>
public enum UserAccountState
{
    Active,
    Locked,
    EmployeeTerminated,
}

public sealed record UserAccountSearch(
    string? Keyword,
    Guid? RoleId,
    Guid? DepartmentId,
    UserAccountState? State,
    int Page,
    int PageSize);

public sealed record UserAccountCounts(int Active, int Locked, int EmployeesWithoutAccount);

public interface IUserAccountRepository : IRepository<Account>
{
    /// <summary>Page of accounts with employee, department, position and role loaded (AsNoTracking), ordered by username.</summary>
    Task<PagedResult<Account>> SearchAsync(UserAccountSearch search, CancellationToken ct = default);

    Task<UserAccountCounts> CountAsync(CancellationToken ct = default);

    /// <summary>Active employees that have no account yet, ordered by employee code (AsNoTracking).</summary>
    Task<IReadOnlyList<Employee>> ListEligibleEmployeesAsync(CancellationToken ct = default);

    /// <summary>Account with employee, department, position, role and history loaded. Tracked when <paramref name="track"/>.</summary>
    Task<Account?> GetDetailAsync(Guid id, bool track, CancellationToken ct = default);

    Task<Employee?> FindEmployeeAsync(Guid employeeId, CancellationToken ct = default);

    Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default);

    Task<bool> HasAccountAsync(Guid employeeId, CancellationToken ct = default);

    Task<Role?> FindRoleAsync(Guid roleId, CancellationToken ct = default);

    /// <summary>Non-archived roles with the number of accounts using each (AsNoTracking).</summary>
    Task<IReadOnlyList<(Role Role, int AccountCount)>> ListRolesAsync(CancellationToken ct = default);

    /// <summary>Latest successful login per account id (from the auth audit log); accounts that never logged in are absent.</summary>
    Task<IReadOnlyDictionary<Guid, DateTimeOffset>> GetLastLoginsAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default);

    /// <summary>Username per account id, for history display.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetUsernamesAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default);
}
