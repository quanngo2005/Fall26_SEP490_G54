namespace G54.BLL.Services;

public interface IAdminAccessService
{
    /// <summary>
    /// Ensures the account is an admin; throws ForbiddenException if not.
    /// </summary>
    Task EnsureAdminAsync(Guid accountId, CancellationToken ct);
}
