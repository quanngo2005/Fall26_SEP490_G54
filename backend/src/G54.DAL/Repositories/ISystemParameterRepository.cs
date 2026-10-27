using G54.DAL.Entities;

namespace G54.DAL.Repositories;

public interface ISystemParameterRepository : IRepository<SystemParameter>
{
    /// <summary>
    /// All parameters ordered by Key. AsNoTracking.
    /// </summary>
    Task<IReadOnlyList<SystemParameter>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Tracked entities for the given keys (for update).
    /// </summary>
    Task<IReadOnlyList<SystemParameter>> GetByKeysForUpdateAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default);

    /// <summary>
    /// Single value by key, AsNoTracking; null when missing.
    /// </summary>
    Task<SystemParameter?> GetByKeyAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Username per account id (for "updatedBy" display).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetUsernamesAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default);
}
