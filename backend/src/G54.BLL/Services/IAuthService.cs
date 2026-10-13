using G54.BLL.Dtos.Auth;

namespace G54.BLL.Services;

public interface IAuthService
{
    Task<AuthResult> AuthenticateAsync(LoginRequestDto request, string? ipAddress, CancellationToken cancellationToken);
    Task<AuthResponseDto?> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken);
    Task<bool> LogoutAsync(string accessToken, string refreshToken, string? ipAddress, CancellationToken cancellationToken);
}

public sealed record AuthResult(AuthResponseDto? Response, AuthFailureReason? FailureReason);

public enum AuthFailureReason
{
    InvalidCredentials,
    AccountLocked,
}
