using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.Auth;

/// <param name="RefreshToken">Refresh token of the session that stays signed in; every other session is revoked.</param>
public sealed record ChangePasswordRequestDto(
    [param: Required, StringLength(128, MinimumLength = 1)] string CurrentPassword,
    [param: Required, StringLength(4096, MinimumLength = 1)] string NewPassword,
    [param: Required, StringLength(4096, MinimumLength = 1)] string RefreshToken);
