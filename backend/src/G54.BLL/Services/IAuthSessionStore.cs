namespace G54.BLL.Services;

public interface IAuthSessionStore
{
    Task<bool> StoreAsync(
        Guid accountId,
        Guid refreshTokenId,
        string refreshTokenHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    Task<bool> RotateAsync(
        Guid accountId,
        Guid currentTokenId,
        string currentTokenHash,
        Guid nextTokenId,
        string nextTokenHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    Task<bool> RevokeAsync(
        Guid accountId,
        Guid refreshTokenId,
        string refreshTokenHash,
        CancellationToken cancellationToken);
}
