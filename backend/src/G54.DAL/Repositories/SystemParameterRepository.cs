using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL.Repositories;

public sealed class SystemParameterRepository(AppDbContext dbContext) : Repository<SystemParameter>(dbContext), ISystemParameterRepository
{
    public async Task<IReadOnlyList<SystemParameter>> GetAllAsync(CancellationToken ct = default)
    {
        return await DbSet
            .AsNoTracking()
            .OrderBy(p => p.Key)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SystemParameter>> GetByKeysForUpdateAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        return await DbSet
            .Where(p => keys.Contains(p.Key))
            .ToListAsync(ct);
    }

    public async Task<SystemParameter?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        return await DbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == key, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetUsernamesAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default)
    {
        return await DbContext.Accounts
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Username, ct);
    }
}
