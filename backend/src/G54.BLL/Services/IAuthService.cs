using G54.BLL.Dtos.Auth;

namespace G54.BLL.Services;

public interface IAuthService
{
    Task<AuthResult> AuthenticateAsync(LoginRequestDto request, string? ipAddress, CancellationToken cancellationToken);
    Task<AuthResponseDto?> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken);
    Task<bool> LogoutAsync(string accessToken, string refreshToken, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the password of <paramref name="accountId"/>, revokes every session and returns a fresh token pair for the
    /// session identified by <see cref="ChangePasswordRequestDto.RefreshToken"/>.
    /// </summary>
    /// <exception cref="G54.BLL.Common.Exceptions.AppException">
    /// 400 <c>INVALID_CURRENT_PASSWORD</c>, 400 <c>VALIDATION_ERROR</c>, 401 for a refresh token of another account, 403 for a locked account.
    /// </exception>
    Task<AuthResponseDto> ChangePasswordAsync(
        Guid accountId,
        ChangePasswordRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken);
}

public sealed record AuthResult(AuthResponseDto? Response, AuthFailureReason? FailureReason);

public enum AuthFailureReason
{
    InvalidCredentials,
    AccountLocked,
}
