using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.Auth;

public sealed record LoginRequestDto(
    [param: Required, EmailAddress, StringLength(255)] string Email,
    [param: Required, StringLength(128, MinimumLength = 1)] string Password,
    bool RememberMe);
