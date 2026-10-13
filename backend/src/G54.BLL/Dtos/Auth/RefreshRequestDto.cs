using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.Auth;

public sealed record RefreshRequestDto([param: Required, StringLength(4096, MinimumLength = 1)] string RefreshToken);
