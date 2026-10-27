namespace G54.DAL.Repositories;

public interface IAdminAccessRepository
{
    /// <summary>
    /// Role codes of an active, unlocked account whose employee is Active; null if not eligible.
    /// </summary>
    Task<IReadOnlyList<string>?> GetActiveRoleCodesAsync(Guid accountId, CancellationToken ct = default);
}
