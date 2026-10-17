using G54.BLL.Common.Constants;

namespace G54.BLL.Common.Exceptions;

public class UnauthorizedException : AppException
{
    public UnauthorizedException(string? message = null)
        : base(message ?? ErrorCodes.GetDefaultMessage(ErrorCodes.Unauthorized), ErrorCodes.Unauthorized, 401)
    {
    }
}
