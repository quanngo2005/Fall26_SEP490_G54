using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL.Repositories;

public sealed class AdminAccessRepository(AppDbContext dbContext) : IAdminAccessRepository
{
    private readonly AppDbContext _dbContext = dbContext;

    public async Task<IReadOnlyList<string>?> GetActiveRoleCodesAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await _dbContext.Accounts
            .AsNoTracking()
            .Include(a => a.Employee)
            .Include(a => a.UserRoles)
            .ThenInclude(r => r.Role)
            .SingleOrDefaultAsync(a => a.Id == accountId, ct);

        if (account is null || account.IsLocked || account.Employee.Status != EmployeeStatus.Active)
        {
            return null;
        }

        return account.UserRoles.Select(r => r.Role.Code).ToArray();
    }
}
