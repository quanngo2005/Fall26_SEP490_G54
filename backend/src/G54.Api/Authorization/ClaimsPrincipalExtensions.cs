using System.Security.Claims;
using G54.BLL.Common.Exceptions;

namespace G54.Api.Authorization;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Reads the account id from the JWT "sub" claim; throws <see cref="UnauthorizedException"/> (401) when missing or malformed.
    /// </summary>
    public static Guid GetRequiredUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(sub) || !Guid.TryParse(sub, out var userId))
        {
            throw new UnauthorizedException("Không xác định được danh tính người dùng từ token xác thực.");
        }

        return userId;
    }
}
